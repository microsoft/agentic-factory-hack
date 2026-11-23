using LeakOps.Contracts;
using LeakOps.Contracts.Logic;

namespace LeakOps.Waterstoring;

public record WarehouseReservation(string IncidentId, int Quantity, DateTimeOffset ReservedAtUtc);

public record WarehouseOrder(
    string Id, string IncidentId, int Quantity, bool Express, string Status, bool ApprovalRequired,
    double TotalEur, string SupplierName, DateTimeOffset OrderedAtUtc, DateTimeOffset ExpectedDeliveryUtc, string Reason);

public record WarehouseItem(
    string Sku, string Name, string Category, string Kind, string? EquipmentType, string WarehouseId, string Warehouse,
    int OnHand, int Reserved, int Available, int ReorderLevel, int ReorderQuantity, string Level,
    DateTimeOffset? AvailableFromUtc, double UnitPriceEur, string Supplier, int SupplierLeadDays, int SupplierExpressHours, string? Notes,
    IReadOnlyList<WarehouseReservation> Reservations, IReadOnlyList<WarehouseOrder> Orders);

/// <summary>One line of the "Recent changes" view: something the Procurement agent wrote.</summary>
public record WarehouseChange(string Kind, string Sku, string Name, string IncidentId, int Quantity, string Detail, DateTimeOffset AtUtc);

public record WarehouseSummary(int Items, int Parts, int Equipment, int Low, int Out, int ReservedUnits, int OpenOrders, int PendingApproval);

public record WarehouseBoardResult(
    DateTimeOffset AtUtc, bool Simulated, WarehouseSummary Summary, IReadOnlyList<WarehouseItem> Items, IReadOnlyList<WarehouseChange> Changes);

/// <summary>
/// The internal warehouse as a table: the stock of both depots, what is reserved for an incident and what is on order. Available
/// quantity and the low-stock rule come from <see cref="StockViews"/>, the same code the Procurement agent's tools use.
/// </summary>
public static class WarehouseBoard
{
    public const string Ok = "ok";
    public const string Low = "low";              // a part below its reorder level
    public const string Out = "out";              // nothing available (a part that is gone, or equipment not back yet)

    public static WarehouseBoardResult Build(
        IEnumerable<InventoryItem> inventory, IEnumerable<Warehouse> warehouses, IEnumerable<Supplier> suppliers,
        IEnumerable<StockReservation> reservations, IEnumerable<PurchaseOrder> orders, DateTimeOffset atUtc)
    {
        var warehouseById = warehouses.ToDictionary(w => w.Id);
        var supplierById = suppliers.ToDictionary(s => s.Id);
        var reservationList = reservations.ToList();
        var orderList = orders.ToList();

        var items = inventory.OrderBy(i => i.Kind == "part" ? 0 : 1).ThenBy(i => i.Id, StringComparer.Ordinal).Select(i =>
        {
            var view = StockViews.Create(i, atUtc, warehouseById, supplierById);
            var level = view.QuantityAvailable == 0 ? Out : view.BelowReorderLevel ? Low : Ok;
            return new WarehouseItem(
                i.Id, i.Name, i.Category, i.Kind, i.EquipmentType, i.WarehouseId, view.Warehouse,
                i.QuantityOnHand, i.QuantityReserved, view.QuantityAvailable, i.ReorderLevel, i.ReorderQuantity, level,
                i.AvailableFromUtc, i.UnitPriceEur, view.Supplier, view.SupplierStandardLeadTimeDays, view.SupplierExpressLeadHours, i.Notes,
                reservationList.Where(r => r.Sku == i.Id).OrderByDescending(r => r.ReservedAtUtc)
                    .Select(r => new WarehouseReservation(r.IncidentId, r.Quantity, r.ReservedAtUtc)).ToList(),
                orderList.Where(o => o.Sku == i.Id).OrderByDescending(o => o.OrderedAtUtc)
                    .Select(o => new WarehouseOrder(o.Id, o.IncidentId, o.Quantity, o.Express, o.Status, o.ApprovalRequired, o.TotalEur,
                        o.SupplierName, o.OrderedAtUtc, o.ExpectedDeliveryUtc, o.Reason)).ToList());
        }).ToList();

        var changes = reservationList
            .Select(r => new WarehouseChange("reservation", r.Sku, r.Name, r.IncidentId, r.Quantity, r.WarehouseId, r.ReservedAtUtc))
            .Concat(orderList.Select(o => new WarehouseChange("order", o.Sku, o.Name, o.IncidentId, o.Quantity,
                $"{o.Id}|{o.Status}|{(o.Express ? "express" : "standard")}|{o.TotalEur:0.00}", o.OrderedAtUtc)))
            .OrderByDescending(c => c.AtUtc).ThenBy(c => c.Sku, StringComparer.Ordinal).ToList();

        var summary = new WarehouseSummary(items.Count, items.Count(i => i.Kind == "part"), items.Count(i => i.Kind == "equipment"),
            items.Count(i => i.Level == Low), items.Count(i => i.Level == Out), items.Sum(i => i.Reserved), orderList.Count,
            orderList.Count(o => o.Status == "pending_approval"));
        return new WarehouseBoardResult(atUtc, Simulated: true, summary, items, changes);
    }
}
