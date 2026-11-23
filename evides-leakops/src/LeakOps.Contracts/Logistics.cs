namespace LeakOps.Contracts;

public record Warehouse(string Id, string Name, string Address, GeoPoint Location);

public record Supplier(
    string Id,
    string Name,
    int StandardLeadTimeDays,
    int ExpressLeadHours,
    double ExpressSurchargePercent,
    string Contact);

/// <summary>A stock item: a spare part (Kind = "part") or a piece of heavy equipment (Kind = "equipment").</summary>
public record InventoryItem(
    string Id,                       // the SKU
    string Name,
    string Category,                 // repair_clamp | coupling | gate_valve | pipe_section | consumable | excavator | traffic_kit | pump
    string Kind,                     // part | equipment
    string? EquipmentType,           // excavator | traffic_kit | dewatering_pump (equipment only)
    double? CapacityTonnes,          // excavators only
    IReadOnlyList<string> CompatibleMaterials,
    int? DiameterMm,
    int QuantityOnHand,
    int QuantityReserved,
    int ReorderLevel,
    int ReorderQuantity,
    double UnitPriceEur,
    string WarehouseId,
    string SupplierId,
    DateTimeOffset? AvailableFromUtc, // equipment under maintenance becomes available again at this time
    string? Notes);

public record PartRequirement(string Sku, int Quantity, string Condition);

public record EquipmentRequirement(string EquipmentType, double? MinCapacityTonnes);

/// <summary>
/// Hardcoded repair knowledge: for a pipe material, diameter band and breach type, which certifications, parts and
/// equipment are needed and how long the repair takes. Same idea as the fault-to-parts mapping of the tire-factory hack.
/// </summary>
public record RepairProcedure(
    string Id,
    string Material,
    int MinDiameterMm,
    int MaxDiameterMm,
    string BreachType,               // burst | leak
    IReadOnlyList<string> RequiredCertifications,
    IReadOnlyList<PartRequirement> Parts,
    IReadOnlyList<EquipmentRequirement> Equipment,
    double EstimatedRepairHours,
    bool ExcavationRequired,
    bool TrafficManagementRequired,
    string Notes);
