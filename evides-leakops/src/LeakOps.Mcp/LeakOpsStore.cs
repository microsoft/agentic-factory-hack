using System.Net;
using LeakOps.Contracts;
using LeakOps.Contracts.Logic;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace LeakOps.Mcp;

/// <summary>All Cosmos DB access for the MCP tools. Keeps the queries in one place.</summary>
public class LeakOpsStore
{
    private readonly Database _db;

    public LeakOpsStore(CosmosClient client, IConfiguration config)
    {
        var name = config["COSMOS_DATABASE_NAME"] ?? LeakOpsContainers.DefaultDatabase;

        // Same guard as the seeder: never touch the tire-factory database by accident.
        if (!name.StartsWith("LeakOps", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"COSMOS_DATABASE_NAME is '{name}', but the LeakOps tools only use databases named LeakOps*.");

        _db = client.GetDatabase(name);
    }

    private Container Container(string name) => _db.GetContainer(name);

    // ---- Network and SCADA ----------------------------------------------------------------------------------------

    public async Task<Sensor?> GetSensorAsync(string id) =>
        (await QueryAsync<Sensor>(LeakOpsContainers.Sensors, new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter("@id", id))).FirstOrDefault();

    public Task<List<Sensor>> ListSensorsAsync(string districtId) =>
        QueryAsync<Sensor>(LeakOpsContainers.Sensors, new QueryDefinition("SELECT * FROM c ORDER BY c.id"), districtId);

    public Task<List<Sensor>> ListSensorsOnSegmentAsync(string districtId, string segmentId) =>
        QueryAsync<Sensor>(LeakOpsContainers.Sensors,
            new QueryDefinition("SELECT * FROM c WHERE c.segmentId = @s ORDER BY c.chainageM").WithParameter("@s", segmentId), districtId);

    public Task<List<SensorReading>> GetReadingsAsync(string sensorId, DateTimeOffset from, DateTimeOffset to) =>
        QueryAsync<SensorReading>(LeakOpsContainers.SensorReadings,
            new QueryDefinition("SELECT * FROM c WHERE c.timestampUtc >= @from AND c.timestampUtc <= @to ORDER BY c.timestampUtc")
                .WithParameter("@from", LeakOpsTime.Utc(from)).WithParameter("@to", LeakOpsTime.Utc(to)),
            sensorId);

    public async Task<PipeSegment?> GetSegmentAsync(string id) =>
        (await QueryAsync<PipeSegment>(LeakOpsContainers.PipeSegments, new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter("@id", id))).FirstOrDefault();

    public Task<List<PipeSegment>> ListSegmentsAsync(string districtId) =>
        QueryAsync<PipeSegment>(LeakOpsContainers.PipeSegments, new QueryDefinition("SELECT * FROM c ORDER BY c.id"), districtId);

    public Task<List<Valve>> ListValvesAsync(string districtId, string segmentId) =>
        QueryAsync<Valve>(LeakOpsContainers.Valves,
            new QueryDefinition("SELECT * FROM c WHERE c.segmentId = @s ORDER BY c.chainageM").WithParameter("@s", segmentId), districtId);

    // The network is small, so reading every valve (across partitions) is cheap and keeps the radius search simple.
    public Task<List<Valve>> ListAllValvesAsync() =>
        QueryAsync<Valve>(LeakOpsContainers.Valves, new QueryDefinition("SELECT * FROM c"));

    // ---- Context for triage ---------------------------------------------------------------------------------------

    public Task<List<PlannedWork>> GetPlannedWorksAsync(string districtId, DateTimeOffset from, DateTimeOffset to) =>
        QueryAsync<PlannedWork>(LeakOpsContainers.PlannedWorks,
            new QueryDefinition("SELECT * FROM c WHERE c.startUtc <= @to AND c.endUtc >= @from ORDER BY c.startUtc")
                .WithParameter("@from", LeakOpsTime.Utc(from)).WithParameter("@to", LeakOpsTime.Utc(to)),
            districtId);

    public Task<List<WeatherObservation>> GetWeatherAsync(string districtId, DateTimeOffset from, DateTimeOffset to) =>
        QueryAsync<WeatherObservation>(LeakOpsContainers.WeatherObservations,
            new QueryDefinition("SELECT * FROM c WHERE c.timestampUtc >= @from AND c.timestampUtc <= @to ORDER BY c.timestampUtc")
                .WithParameter("@from", LeakOpsTime.Utc(from)).WithParameter("@to", LeakOpsTime.Utc(to)),
            districtId);

    public Task<List<UsageProfile>> GetUsageProfilesAsync(string districtId, string dayType) =>
        QueryAsync<UsageProfile>(LeakOpsContainers.UsageProfiles,
            new QueryDefinition("SELECT * FROM c WHERE c.dayType = @d ORDER BY c.localHour").WithParameter("@d", dayType), districtId);

    public Task<List<UsageEvent>> GetUsageEventsAsync(string districtId) =>
        QueryAsync<UsageEvent>(LeakOpsContainers.UsageEvents, new QueryDefinition("SELECT * FROM c ORDER BY c.startUtc"), districtId);

    // ---- Repair knowledge, workforce, stock -----------------------------------------------------------------------

    public async Task<RepairProcedure?> FindProcedureAsync(string material, int diameterMm, string breachType) =>
        (await QueryAsync<RepairProcedure>(LeakOpsContainers.RepairProcedures,
            new QueryDefinition("SELECT * FROM c WHERE c.minDiameterMm <= @d AND c.maxDiameterMm >= @d AND c.breachType = @b")
                .WithParameter("@d", diameterMm).WithParameter("@b", breachType),
            material)).FirstOrDefault();

    public Task<List<RepairProcedure>> ListProceduresAsync(string material) =>
        QueryAsync<RepairProcedure>(LeakOpsContainers.RepairProcedures, new QueryDefinition("SELECT * FROM c ORDER BY c.minDiameterMm"), material);

    public Task<List<Technician>> ListTechniciansAsync() =>
        QueryAsync<Technician>(LeakOpsContainers.Technicians, new QueryDefinition("SELECT * FROM c ORDER BY c.id"));

    public async Task<Technician?> GetTechnicianAsync(string id) =>
        (await QueryAsync<Technician>(LeakOpsContainers.Technicians, new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter("@id", id))).FirstOrDefault();

    public Task<List<InventoryItem>> ListInventoryAsync() =>
        QueryAsync<InventoryItem>(LeakOpsContainers.Inventory, new QueryDefinition("SELECT * FROM c ORDER BY c.id"));

    public async Task<InventoryItem?> GetInventoryItemAsync(string sku) =>
        (await QueryAsync<InventoryItem>(LeakOpsContainers.Inventory, new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter("@id", sku))).FirstOrDefault();

    public async Task<Dictionary<string, Warehouse>> GetWarehousesAsync() =>
        (await QueryAsync<Warehouse>(LeakOpsContainers.Warehouses, new QueryDefinition("SELECT * FROM c"))).ToDictionary(w => w.Id);

    public async Task<Dictionary<string, Supplier>> GetSuppliersAsync() =>
        (await QueryAsync<Supplier>(LeakOpsContainers.Suppliers, new QueryDefinition("SELECT * FROM c"))).ToDictionary(s => s.Id);

    // ---- Writes: reservations, purchase orders, work orders -------------------------------------------------------

    public Task<StockReservation?> GetReservationAsync(string incidentId, string sku) =>
        ReadAsync<StockReservation>(LeakOpsContainers.StockReservations, $"{incidentId}-{sku}", incidentId);

    public Task<List<StockReservation>> ListReservationsAsync(string incidentId) =>
        QueryAsync<StockReservation>(LeakOpsContainers.StockReservations, new QueryDefinition("SELECT * FROM c ORDER BY c.sku"), incidentId);

    /// <summary>Creates the reservation record. Returns false when this incident already reserved this SKU.</summary>
    public async Task<bool> TryCreateReservationAsync(StockReservation reservation)
    {
        try
        {
            await Container(LeakOpsContainers.StockReservations).CreateItemAsync(reservation, new PartitionKey(reservation.IncidentId));
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return false;
        }
    }

    public Task DeleteReservationAsync(StockReservation reservation) =>
        Container(LeakOpsContainers.StockReservations).DeleteItemAsync<StockReservation>(reservation.Id, new PartitionKey(reservation.IncidentId));

    /// <summary>
    /// Adds to the reserved quantity in one atomic step. The filter predicate makes Cosmos DB refuse the change (HTTP 412)
    /// when not enough stock is left, so two agents can never reserve the last clamp at the same time.
    /// </summary>
    public async Task<InventoryItem?> TryReserveStockAsync(InventoryItem item, int quantity)
    {
        try
        {
            var response = await Container(LeakOpsContainers.Inventory).PatchItemAsync<InventoryItem>(
                item.Id,
                new PartitionKey(item.Category),
                [PatchOperation.Increment("/quantityReserved", quantity)],
                new PatchItemRequestOptions { FilterPredicate = $"from c where (c.quantityOnHand - c.quantityReserved) >= {quantity}" });
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            return null;
        }
    }

    public Task<PurchaseOrder?> GetPurchaseOrderAsync(string incidentId, string sku) =>
        ReadAsync<PurchaseOrder>(LeakOpsContainers.PurchaseOrders, $"PO-{incidentId}-{sku}", incidentId);

    public Task<List<PurchaseOrder>> ListPurchaseOrdersAsync(string incidentId) =>
        QueryAsync<PurchaseOrder>(LeakOpsContainers.PurchaseOrders, new QueryDefinition("SELECT * FROM c ORDER BY c.sku"), incidentId);

    public Task UpsertPurchaseOrderAsync(PurchaseOrder order) =>
        Container(LeakOpsContainers.PurchaseOrders).UpsertItemAsync(order, new PartitionKey(order.IncidentId));

    public Task UpsertWorkOrderAsync(WorkOrder order) =>
        Container(LeakOpsContainers.WorkOrders).UpsertItemAsync(order, new PartitionKey(order.IncidentId));

    /// <summary>All work orders (across incidents), to see who is already committed.</summary>
    public Task<List<WorkOrder>> ListWorkOrdersAsync() =>
        QueryAsync<WorkOrder>(LeakOpsContainers.WorkOrders, new QueryDefinition("SELECT * FROM c"));

    // ---- Helpers --------------------------------------------------------------------------------------------------

    private async Task<List<T>> QueryAsync<T>(string container, QueryDefinition query, string? partitionKey = null)
    {
        // With a partition key the query only touches one partition; without it Cosmos DB fans out across all of them.
        var options = partitionKey is null ? new QueryRequestOptions() : new QueryRequestOptions { PartitionKey = new PartitionKey(partitionKey) };
        var results = new List<T>();
        using var iterator = Container(container).GetItemQueryIterator<T>(query, requestOptions: options);
        while (iterator.HasMoreResults) results.AddRange(await iterator.ReadNextAsync());
        return results;
    }

    private async Task<T?> ReadAsync<T>(string container, string id, string partitionKey) where T : class
    {
        try
        {
            return (await Container(container).ReadItemAsync<T>(id, new PartitionKey(partitionKey))).Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
