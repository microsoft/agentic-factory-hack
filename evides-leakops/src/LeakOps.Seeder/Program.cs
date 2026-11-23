using LeakOps.Contracts;
using LeakOps.Seeder;
using Microsoft.Azure.Cosmos;

// Usage: dotnet run --project src/LeakOps.Seeder -- [--dry-run] [--reset] [--reset-runtime] [--data <folder>] [--database <name>]
//   --dry-run         load and generate the data, print counts, do not touch Cosmos DB
//   --reset           delete the database first (only allowed for a database named like LeakOps*)
//   --reset-runtime   after seeding, also empty the run-time containers (incidents, work orders, purchase orders, reservations).
//                     Seeding itself already puts the stock reservations back to zero, so this gives a clean demo state.
// Connection settings come from COSMOS_ENDPOINT and COSMOS_KEY (environment variables, which win, or the repo-root .env file).
// C# note: this file uses "top-level statements" - no Main method or class needed (like a Python script).

var dryRun = args.Contains("--dry-run");
var reset = args.Contains("--reset");
var resetRuntime = args.Contains("--reset-runtime");
var dataPath = ArgValue("--data");
var database = ArgValue("--database") ?? LeakOpsContainers.DefaultDatabase;

// The repo-root .env points COSMOS_DATABASE_NAME at the tire-factory database, so it is deliberately NOT used here.
if (!database.StartsWith("LeakOps", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"Refusing to use database '{database}': the seeder only writes to databases named LeakOps*.");
    return 1;
}

var dataDirectory = SeedDataLoader.FindDataDirectory(dataPath);
var data = SeedDataLoader.Load(dataDirectory);

var sets = new (string Container, int Count)[]
{
    (LeakOpsContainers.Sensors, data.Sensors.Count),
    (LeakOpsContainers.SensorReadings, data.SensorReadings.Count),
    (LeakOpsContainers.PipeSegments, data.PipeSegments.Count),
    (LeakOpsContainers.Valves, data.Valves.Count),
    (LeakOpsContainers.PlannedWorks, data.PlannedWorks.Count),
    (LeakOpsContainers.WeatherObservations, data.WeatherObservations.Count),
    (LeakOpsContainers.UsageProfiles, data.UsageProfiles.Count),
    (LeakOpsContainers.UsageEvents, data.UsageEvents.Count),
    (LeakOpsContainers.Technicians, data.Technicians.Count),
    (LeakOpsContainers.Inventory, data.Inventory.Count),
    (LeakOpsContainers.Warehouses, data.Warehouses.Count),
    (LeakOpsContainers.Suppliers, data.Suppliers.Count),
    (LeakOpsContainers.RepairProcedures, data.RepairProcedures.Count),
    (LeakOpsContainers.Scenarios, data.Scenarios.Count),
};

Console.WriteLine($"Data folder: {dataDirectory}");
foreach (var (container, count) in sets) Console.WriteLine($"  {container,-22} {count,5} items");
if (dryRun)
{
    Console.WriteLine("Dry run: nothing written.");
    return 0;
}

// NoClobber: variables that are already set (for example by deploy.ps1 for a new deployment) win over the repo-root .env.
try { DotNetEnv.Env.NoClobber().TraversePath().Load(); } catch (FileNotFoundException) { /* no .env file: rely on real environment variables */ }
var endpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT");
var key = Environment.GetEnvironmentVariable("COSMOS_KEY");
if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
{
    Console.Error.WriteLine("COSMOS_ENDPOINT and COSMOS_KEY are required (environment or .env).");
    return 1;
}

// using = Python's "with": the client is disposed when the program ends.
using var client = new CosmosClient(endpoint, key, new CosmosClientOptions
{
    AllowBulkExecution = true,   // lets the SDK batch the many parallel upserts below
    SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase },
});

if (reset)
{
    Console.WriteLine($"Deleting database {database} ...");
    try { await client.GetDatabase(database).DeleteAsync(); }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { /* nothing to delete */ }
}

var db = (await client.CreateDatabaseIfNotExistsAsync(database)).Database;
foreach (var (name, path) in LeakOpsContainers.PartitionKeyPaths)
    await db.CreateContainerIfNotExistsAsync(new ContainerProperties(name, path));
Console.WriteLine($"Database {database}: {LeakOpsContainers.PartitionKeyPaths.Count} containers ready.");

await Upsert(LeakOpsContainers.Sensors, data.Sensors, s => s.DistrictId);
await Upsert(LeakOpsContainers.SensorReadings, data.SensorReadings, r => r.SensorId);
await Upsert(LeakOpsContainers.PipeSegments, data.PipeSegments, p => p.DistrictId);
await Upsert(LeakOpsContainers.Valves, data.Valves, v => v.DistrictId);
await Upsert(LeakOpsContainers.PlannedWorks, data.PlannedWorks, p => p.DistrictId);
await Upsert(LeakOpsContainers.WeatherObservations, data.WeatherObservations, w => w.DistrictId);
await Upsert(LeakOpsContainers.UsageProfiles, data.UsageProfiles, u => u.DistrictId);
await Upsert(LeakOpsContainers.UsageEvents, data.UsageEvents, u => u.DistrictId);
await Upsert(LeakOpsContainers.Technicians, data.Technicians, t => t.Region);
await Upsert(LeakOpsContainers.Inventory, data.Inventory, i => i.Category);
await Upsert(LeakOpsContainers.Warehouses, data.Warehouses, w => w.Id);
await Upsert(LeakOpsContainers.Suppliers, data.Suppliers, s => s.Id);
await Upsert(LeakOpsContainers.RepairProcedures, data.RepairProcedures, r => r.Material);
await Upsert(LeakOpsContainers.Scenarios, data.Scenarios, s => s.Id);

if (resetRuntime)
{
    foreach (var name in new[] { LeakOpsContainers.Incidents, LeakOpsContainers.WorkOrders, LeakOpsContainers.PurchaseOrders, LeakOpsContainers.StockReservations, LeakOpsContainers.WaterstoringIncidents })
        await ClearContainer(name);
}

Console.WriteLine("Seeding finished.");
return 0;

async Task ClearContainer(string containerName)
{
    var container = db.GetContainer(containerName);
    var partitionKeyProperty = LeakOpsContainers.PartitionKeyPaths[containerName].TrimStart('/');

    var documents = new List<(string Id, string PartitionKey)>();
    using var iterator = container.GetItemQueryIterator<Newtonsoft.Json.Linq.JObject>("SELECT * FROM c");
    while (iterator.HasMoreResults)
        foreach (var document in await iterator.ReadNextAsync())
            documents.Add((document.Value<string>("id")!, document.Value<string>(partitionKeyProperty)!));

    await Task.WhenAll(documents.Select(d => container.DeleteItemAsync<object>(d.Id, new PartitionKey(d.PartitionKey))));
    Console.WriteLine($"  {containerName,-22} {documents.Count,5} deleted");
}

async Task Upsert<T>(string containerName, IReadOnlyList<T> items, Func<T, string> partitionKey)
{
    var container = db.GetContainer(containerName);
    // Upsert = insert or replace, so the seeder can be run again safely.
    await Task.WhenAll(items.Select(item => container.UpsertItemAsync(item, new PartitionKey(partitionKey(item)))));
    Console.WriteLine($"  {containerName,-22} {items.Count,5} upserted");
}

string? ArgValue(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
