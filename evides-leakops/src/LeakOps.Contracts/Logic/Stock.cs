namespace LeakOps.Contracts.Logic;

public record StockView(
    string Sku,
    string Name,
    string Category,
    string Kind,
    string? EquipmentType,
    double? CapacityTonnes,
    int? DiameterMm,
    int QuantityOnHand,
    int QuantityReserved,
    int QuantityAvailable,           // 0 when equipment is not back yet at the requested time
    DateTimeOffset? AvailableFromUtc,
    int ReorderLevel,
    int ReorderQuantity,
    bool BelowReorderLevel,          // available quantity is below the reorder level
    double UnitPriceEur,
    string Warehouse,
    GeoPoint WarehouseLocation,
    string Supplier,
    int SupplierStandardLeadTimeDays,
    int SupplierExpressLeadHours,
    double SupplierExpressSurchargePercent,
    string? Notes);

public static class StockViews
{
    public static int Available(InventoryItem item, DateTimeOffset atUtc) =>
        item.AvailableFromUtc is { } from && from > atUtc ? 0 : Math.Max(0, item.QuantityOnHand - item.QuantityReserved);

    public static StockView Create(
        InventoryItem item, DateTimeOffset atUtc, IReadOnlyDictionary<string, Warehouse> warehouses, IReadOnlyDictionary<string, Supplier> suppliers)
    {
        var warehouse = warehouses[item.WarehouseId];
        var supplier = suppliers[item.SupplierId];
        var available = Available(item, atUtc);

        return new StockView(
            item.Id, item.Name, item.Category, item.Kind, item.EquipmentType, item.CapacityTonnes, item.DiameterMm,
            item.QuantityOnHand, item.QuantityReserved, available, item.AvailableFromUtc,
            item.ReorderLevel, item.ReorderQuantity, item.Kind == "part" && available < item.ReorderLevel, item.UnitPriceEur,
            warehouse.Name, warehouse.Location, supplier.Name,
            supplier.StandardLeadTimeDays, supplier.ExpressLeadHours, supplier.ExpressSurchargePercent, item.Notes);
    }
}

public static class Procurement
{
    /// <summary>Orders above this total need a manager's approval (simulated rule).</summary>
    public const double ApprovalThresholdEur = 2500;

    public const string ReplenishmentSuffix = "-REPL";

    /// <summary>
    /// A purchase order for one SKU of one incident. An urgent order (parts this repair is short of) and a replenishment order
    /// (restocking after the repair used the stock) are different orders, so the replenishment gets its own id suffix.
    /// </summary>
    public static PurchaseOrder BuildOrder(
        InventoryItem item, Supplier supplier, string incidentId, int quantity, bool express, string reason, DateTimeOffset orderedAtUtc, string idSuffix = "")
    {
        var ordered = LeakOpsTime.Utc(orderedAtUtc);
        var surcharge = express ? 1 + supplier.ExpressSurchargePercent / 100.0 : 1.0;
        var total = Math.Round(item.UnitPriceEur * quantity * surcharge, 2);
        var delivery = express ? ordered.AddHours(supplier.ExpressLeadHours) : ordered.AddDays(supplier.StandardLeadTimeDays);
        var needsApproval = total > ApprovalThresholdEur;

        return new PurchaseOrder(
            Id: $"PO-{incidentId}-{item.Id}{idSuffix}",
            IncidentId: incidentId,
            Sku: item.Id,
            Name: item.Name,
            Quantity: quantity,
            UnitPriceEur: item.UnitPriceEur,
            SupplierId: supplier.Id,
            SupplierName: supplier.Name,
            Express: express,
            TotalEur: total,
            OrderedAtUtc: ordered,
            ExpectedDeliveryUtc: delivery,
            Status: needsApproval ? "pending_approval" : "ordered",
            ApprovalRequired: needsApproval,
            Reason: reason);
    }
}
