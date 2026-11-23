namespace LeakOps.Contracts;

/// <summary>Cosmos DB container names and partition key paths shared by the seeder, the MCP tools and the agents.</summary>
public static class LeakOpsContainers
{
    public const string DefaultDatabase = "LeakOpsDB";

    public const string Sensors = "Sensors";
    public const string SensorReadings = "SensorReadings";
    public const string PipeSegments = "PipeSegments";
    public const string Valves = "Valves";
    public const string PlannedWorks = "PlannedWorks";
    public const string WeatherObservations = "WeatherObservations";
    public const string UsageProfiles = "UsageProfiles";
    public const string UsageEvents = "UsageEvents";
    public const string Technicians = "Technicians";
    public const string Inventory = "Inventory";
    public const string Warehouses = "Warehouses";
    public const string Suppliers = "Suppliers";
    public const string RepairProcedures = "RepairProcedures";
    public const string Scenarios = "Scenarios";

    // Written by the agents at run time (created empty by the seeder).
    public const string Incidents = "Incidents";
    public const string WorkOrders = "WorkOrders";
    public const string PurchaseOrders = "PurchaseOrders";
    public const string StockReservations = "StockReservations";

    // The public incidents of the (fake) Waterstoring site when it runs in Azure.
    public const string WaterstoringIncidents = "WaterstoringIncidents";

    public static readonly IReadOnlyDictionary<string, string> PartitionKeyPaths = new Dictionary<string, string>
    {
        [Sensors] = "/districtId",
        [SensorReadings] = "/sensorId",
        [PipeSegments] = "/districtId",
        [Valves] = "/districtId",
        [PlannedWorks] = "/districtId",
        [WeatherObservations] = "/districtId",
        [UsageProfiles] = "/districtId",
        [UsageEvents] = "/districtId",
        [Technicians] = "/region",
        [Inventory] = "/category",
        [Warehouses] = "/id",
        [Suppliers] = "/id",
        [RepairProcedures] = "/material",
        [Scenarios] = "/id",
        [Incidents] = "/districtId",
        [WorkOrders] = "/incidentId",
        [PurchaseOrders] = "/incidentId",
        [StockReservations] = "/incidentId",
        [WaterstoringIncidents] = "/id",
    };
}
