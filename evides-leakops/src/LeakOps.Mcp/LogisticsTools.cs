using LeakOps.Contracts;
using LeakOps.Contracts.Logic;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace LeakOps.Mcp;

/// <summary>Tools for the Procurement agent: stock levels, reservations and purchase orders.</summary>
public class LogisticsTools(LeakOpsStore store, ILogger<LogisticsTools> logger)
{
    [Function(nameof(GetInventory))]
    public async Task<string> GetInventory(
        [McpToolTrigger("get-inventory", "Gets stock levels for spare parts and heavy equipment: quantity on hand, reserved and available, reorder level, price, warehouse (with position) and supplier lead times. Equipment that is not back yet at the requested time shows 0 available and an 'availableFromUtc'.")]
        ToolInvocationContext context,
        [McpToolProperty("skus", "Comma separated SKUs, for example CLP-CI-300,GSK-300,EQP-EXC-8T. Leave empty to list everything.", isRequired: false)] string? skus,
        [McpToolProperty("atUtc", "Moment the stock is needed, ISO 8601 UTC. Default now.", isRequired: false)] string? atUtc)
    {
        var at = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(atUtc) && !ToolJson.TryTime(atUtc, "atUtc", out at, out var error)) return error;

        var wanted = ToolJson.SplitList(skus);
        var warehouses = await store.GetWarehousesAsync();
        var suppliers = await store.GetSuppliersAsync();
        var items = (await store.ListInventoryAsync())
            .Where(i => wanted.Count == 0 || wanted.Contains(i.Id, StringComparer.OrdinalIgnoreCase))
            .Select(i => StockViews.Create(i, at, warehouses, suppliers))
            .ToList();
        logger.LogInformation("get-inventory [{Skus}] -> {Count}", string.Join(",", wanted), items.Count);

        var unknown = wanted.Where(w => !items.Any(i => i.Sku.Equals(w, StringComparison.OrdinalIgnoreCase))).ToList();
        return ToolJson.Ok(new { atUtc = at, count = items.Count, unknownSkus = unknown, items });
    }

    [Function(nameof(GetIncidentPackage))]
    public async Task<string> GetIncidentPackage(
        [McpToolTrigger("get-incident-package", "Lists what an incident has already reserved and ordered. Use it before reserving or ordering again, so nothing is counted twice.")]
        ToolInvocationContext context,
        [McpToolProperty("incidentId", "Incident id, for example INC-2026-1114-0001", isRequired: true)] string incidentId)
    {
        if (string.IsNullOrWhiteSpace(incidentId)) return ToolJson.Error("'incidentId' is required.");
        var reservations = await store.ListReservationsAsync(incidentId);
        var orders = await store.ListPurchaseOrdersAsync(incidentId);
        logger.LogInformation("get-incident-package {IncidentId} -> {Reservations} reservations, {Orders} orders", incidentId, reservations.Count, orders.Count);
        return ToolJson.Ok(new { incidentId, reservations, purchaseOrders = orders });
    }

    [Function(nameof(ReserveStock))]
    public async Task<string> ReserveStock(
        [McpToolTrigger("reserve-stock", "Reserves a part or a piece of equipment for an incident. Safe to repeat: the same incident and SKU is only reserved once. Fails with status 'insufficient_stock' (and the shortfall) when not enough is available. 'belowReorderLevel' tells whether the remaining stock needs replenishing.")]
        ToolInvocationContext context,
        [McpToolProperty("incidentId", "Incident id, for example INC-2026-1114-0001", isRequired: true)] string incidentId,
        [McpToolProperty("sku", "SKU to reserve, for example CLP-CI-300", isRequired: true)] string sku,
        [McpToolProperty("quantity", "Number of units. Equipment is always 1.", isRequired: true)] int? quantity,
        [McpToolProperty("atUtc", "Moment the item is needed, ISO 8601 UTC. Equipment that is not back by then cannot be reserved. Default now.", isRequired: false)] string? atUtc)
    {
        if (string.IsNullOrWhiteSpace(incidentId)) return ToolJson.Error("'incidentId' is required.");
        if (quantity is null or < 1) return ToolJson.Error("'quantity' must be 1 or more.");
        var at = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(atUtc) && !ToolJson.TryTime(atUtc, "atUtc", out at, out var error)) return error;

        var item = await store.GetInventoryItemAsync(sku.Trim());
        if (item is null) return ToolJson.Ok(new { status = "not_found", sku, message = "Unknown SKU. Use get-inventory to list valid SKUs." });

        var existing = await store.GetReservationAsync(incidentId, item.Id);
        if (existing is not null)
            return ToolJson.Ok(new { status = "already_reserved", reservation = existing, message = "This incident already reserved this SKU. Nothing changed." });

        var available = StockViews.Available(item, at);
        if (item.Kind == "equipment" && item.AvailableFromUtc is { } backAt && backAt > at)
            return ToolJson.Ok(new { status = "equipment_unavailable", sku = item.Id, availableFromUtc = backAt, message = $"{item.Name} is not available before {backAt:u}." });
        if (available < quantity)
            return ToolJson.Ok(new { status = "insufficient_stock", sku = item.Id, requested = quantity, available, shortfall = quantity - available, supplierId = item.SupplierId, message = "Not enough stock. Consider create-purchase-order." });

        // Reservation record first (its id is unique per incident and SKU), then the atomic stock decrement.
        var reservation = new StockReservation($"{incidentId}-{item.Id}", incidentId, item.Id, item.Name, item.Kind, quantity.Value, item.WarehouseId, LeakOpsTime.Utc(at));
        if (!await store.TryCreateReservationAsync(reservation))
            return ToolJson.Ok(new { status = "already_reserved", reservation = await store.GetReservationAsync(incidentId, item.Id), message = "This incident already reserved this SKU. Nothing changed." });

        var updated = await store.TryReserveStockAsync(item, quantity.Value);
        if (updated is null)
        {
            await store.DeleteReservationAsync(reservation);   // someone else took the last units in the meantime
            var now = await store.GetInventoryItemAsync(item.Id);
            return ToolJson.Ok(new { status = "insufficient_stock", sku = item.Id, requested = quantity, available = now is null ? 0 : StockViews.Available(now, at), message = "Stock changed while reserving. Check get-inventory and try again." });
        }

        var remaining = StockViews.Available(updated, at);
        logger.LogInformation("reserve-stock {IncidentId} {Sku} x{Quantity} -> remaining {Remaining}", incidentId, item.Id, quantity, remaining);
        return ToolJson.Ok(new
        {
            status = "reserved",
            reservation,
            remainingAvailable = remaining,
            reorderLevel = updated.ReorderLevel,
            belowReorderLevel = updated.Kind == "part" && remaining < updated.ReorderLevel,
        });
    }

    [Function(nameof(CreatePurchaseOrder))]
    public async Task<string> CreatePurchaseOrder(
        [McpToolTrigger("create-purchase-order", "Creates a purchase order at the SKU's supplier. Express ordering costs a surcharge but delivers faster. Orders above the approval threshold get status 'pending_approval'. Safe to repeat: one order per incident and SKU (a repeat replaces it).")]
        ToolInvocationContext context,
        [McpToolProperty("incidentId", "Incident id, for example INC-2026-1114-0001", isRequired: true)] string incidentId,
        [McpToolProperty("sku", "SKU to order, for example VLV-GATE-300", isRequired: true)] string sku,
        [McpToolProperty("quantity", "Number of units to order", isRequired: true)] int? quantity,
        [McpToolProperty("reason", "Why the order is needed, one sentence", isRequired: true)] string reason,
        [McpToolProperty("express", "True for express delivery (surcharge, shorter lead time). Default false.", isRequired: false)] bool? express,
        [McpToolProperty("orderedAtUtc", "Order moment, ISO 8601 UTC. Default now.", isRequired: false)] string? orderedAtUtc,
        [McpToolProperty("purpose", "'urgent' (default) for parts this incident is short of, or 'replenishment' to restock afterwards. Each purpose has its own order per incident and SKU.", isRequired: false)] string? purpose)
    {
        if (string.IsNullOrWhiteSpace(incidentId)) return ToolJson.Error("'incidentId' is required.");
        if (quantity is null or < 1) return ToolJson.Error("'quantity' must be 1 or more.");
        if (purpose is not null && purpose.Trim().ToLowerInvariant() is not ("urgent" or "replenishment" or ""))
            return ToolJson.Error("'purpose' must be 'urgent' or 'replenishment'.");
        var suffix = purpose?.Trim().Equals("replenishment", StringComparison.OrdinalIgnoreCase) == true ? Procurement.ReplenishmentSuffix : "";
        var at = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(orderedAtUtc) && !ToolJson.TryTime(orderedAtUtc, "orderedAtUtc", out at, out var error)) return error;

        var item = await store.GetInventoryItemAsync(sku.Trim());
        if (item is null) return ToolJson.Error($"Unknown SKU '{sku}'. Use get-inventory to list valid SKUs.");
        if (item.Kind != "part") return ToolJson.Error($"{item.Id} is equipment and cannot be purchased with this tool.");

        var supplier = (await store.GetSuppliersAsync())[item.SupplierId];
        var order = Procurement.BuildOrder(item, supplier, incidentId, quantity.Value, express == true, reason, at, suffix);
        await store.UpsertPurchaseOrderAsync(order);
        logger.LogInformation("create-purchase-order {OrderId} total {Total} status {Status}", order.Id, order.TotalEur, order.Status);

        return ToolJson.Ok(new
        {
            order,
            approvalThresholdEur = Procurement.ApprovalThresholdEur,
            message = order.ApprovalRequired
                ? $"Total {order.TotalEur:F2} EUR is above {Procurement.ApprovalThresholdEur:F0} EUR: waiting for manager approval."
                : "Order placed.",
        });
    }
}
