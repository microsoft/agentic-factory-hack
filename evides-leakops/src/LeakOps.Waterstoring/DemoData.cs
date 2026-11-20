using LeakOps.Contracts;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace LeakOps.Waterstoring;

/// <summary>
/// What the technicians page and the "Reset demo data" button need besides the incident list: the roster, the work orders that
/// assign technicians, and a way to put the whole demo back to its starting state.
/// </summary>
public interface IDemoData
{
    Task<IReadOnlyList<Technician>> TechniciansAsync();
    Task<IReadOnlyList<WorkOrder>> WorkOrdersAsync();
    Task<IReadOnlyList<InventoryItem>> InventoryAsync();
    Task<IReadOnlyList<Warehouse>> WarehousesAsync();
    Task<IReadOnlyList<Supplier>> SuppliersAsync();
    Task<IReadOnlyList<StockReservation>> ReservationsAsync();
    Task<IReadOnlyList<PurchaseOrder>> PurchaseOrdersAsync();

    /// <summary>Deletes incidents, work orders, purchase orders and stock reservations of the workflow, and restores the stock levels.</summary>
    Task ResetAsync();
}

/// <summary>Reads the shipped data/*.json files. Used on a laptop and in tests: the roster and stock are the seed data and nothing is reserved, ordered or assigned.</summary>
public class FileDemoData(string dataDirectory) : IDemoData
{
    private Task<IReadOnlyList<T>> Seed<T>(string file) => Task.FromResult<IReadOnlyList<T>>(DemoJson.ReadList<T>(dataDirectory, file));

    public Task<IReadOnlyList<Technician>> TechniciansAsync() => Seed<Technician>("technicians.json");
    public Task<IReadOnlyList<WorkOrder>> WorkOrdersAsync() => Task.FromResult<IReadOnlyList<WorkOrder>>([]);
    public Task<IReadOnlyList<InventoryItem>> InventoryAsync() => Seed<InventoryItem>("inventory.json");
    public Task<IReadOnlyList<Warehouse>> WarehousesAsync() => Seed<Warehouse>("warehouses.json");
    public Task<IReadOnlyList<Supplier>> SuppliersAsync() => Seed<Supplier>("suppliers.json");
    public Task<IReadOnlyList<StockReservation>> ReservationsAsync() => Task.FromResult<IReadOnlyList<StockReservation>>([]);
    public Task<IReadOnlyList<PurchaseOrder>> PurchaseOrdersAsync() => Task.FromResult<IReadOnlyList<PurchaseOrder>>([]);
    public Task ResetAsync() => Task.CompletedTask;
}

/// <summary>
/// Reads the roster, stock, reservations, orders and work orders from the LeakOps Cosmos database, the same data the agents' tools use,
/// so these pages and the agents always agree. The reset writes: the web app's managed identity has data access to the whole account.
/// </summary>
public class CosmosDemoData(CosmosClient client, string database, string dataDirectory) : IDemoData
{
    private Database Db => client.GetDatabase(database);

    public async Task<IReadOnlyList<Technician>> TechniciansAsync() => await QueryAsync<Technician>(LeakOpsContainers.Technicians);
    public async Task<IReadOnlyList<WorkOrder>> WorkOrdersAsync() => await QueryAsync<WorkOrder>(LeakOpsContainers.WorkOrders);
    public async Task<IReadOnlyList<InventoryItem>> InventoryAsync() => await QueryAsync<InventoryItem>(LeakOpsContainers.Inventory);
    public async Task<IReadOnlyList<Warehouse>> WarehousesAsync() => await QueryAsync<Warehouse>(LeakOpsContainers.Warehouses);
    public async Task<IReadOnlyList<Supplier>> SuppliersAsync() => await QueryAsync<Supplier>(LeakOpsContainers.Suppliers);
    public async Task<IReadOnlyList<StockReservation>> ReservationsAsync() => await QueryAsync<StockReservation>(LeakOpsContainers.StockReservations);
    public async Task<IReadOnlyList<PurchaseOrder>> PurchaseOrdersAsync() => await QueryAsync<PurchaseOrder>(LeakOpsContainers.PurchaseOrders);

    public async Task ResetAsync()
    {
        foreach (var name in new[] { LeakOpsContainers.Incidents, LeakOpsContainers.WorkOrders, LeakOpsContainers.PurchaseOrders, LeakOpsContainers.StockReservations })
            await ClearAsync(name);

        // The seed file holds the starting stock; upserting it puts quantities and reservations back.
        var inventory = Db.GetContainer(LeakOpsContainers.Inventory);
        foreach (var item in DemoJson.ReadList<InventoryItem>(dataDirectory, "inventory.json"))
            await inventory.UpsertItemAsync(item, new PartitionKey(item.Category));
    }

    private async Task<List<T>> QueryAsync<T>(string container)
    {
        var found = new List<T>();
        using var feed = Db.GetContainer(container).GetItemQueryIterator<T>("SELECT * FROM c");
        while (feed.HasMoreResults) found.AddRange(await feed.ReadNextAsync());
        return found;
    }

    private async Task ClearAsync(string name)
    {
        var container = Db.GetContainer(name);
        var partitionKeyProperty = LeakOpsContainers.PartitionKeyPaths[name].TrimStart('/');

        var documents = new List<(string Id, string PartitionKey)>();
        using var feed = container.GetItemQueryIterator<JObject>("SELECT * FROM c");
        while (feed.HasMoreResults)
            foreach (var document in await feed.ReadNextAsync())
                documents.Add((document.Value<string>("id")!, document.Value<string>(partitionKeyProperty)!));

        await Task.WhenAll(documents.Select(d => container.DeleteItemAsync<object>(d.Id, new PartitionKey(d.PartitionKey))));
    }
}

public static class DemoJson
{
    // Same settings as the seeder: camelCase names, and "Z" timestamps stay UTC offsets.
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        DateParseHandling = DateParseHandling.DateTimeOffset,
    };

    public static List<T> ReadList<T>(string directory, string file) =>
        JsonConvert.DeserializeObject<List<T>>(File.ReadAllText(Path.Combine(directory, file)), Settings)
        ?? throw new InvalidDataException($"{file} is empty or invalid.");
}
