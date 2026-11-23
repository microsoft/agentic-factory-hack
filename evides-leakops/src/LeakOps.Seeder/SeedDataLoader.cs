using LeakOps.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace LeakOps.Seeder;

/// <summary>Everything that gets written to Cosmos DB, loaded from data/*.json plus the generated parts.</summary>
public record SeedData(
    SimulationConfig Simulation,
    IReadOnlyList<Sensor> Sensors,
    IReadOnlyList<SensorReading> SensorReadings,
    IReadOnlyList<PipeSegment> PipeSegments,
    IReadOnlyList<Valve> Valves,
    IReadOnlyList<PlannedWork> PlannedWorks,
    IReadOnlyList<WeatherObservation> WeatherObservations,
    IReadOnlyList<UsageProfile> UsageProfiles,
    IReadOnlyList<UsageEvent> UsageEvents,
    IReadOnlyList<Technician> Technicians,
    IReadOnlyList<InventoryItem> Inventory,
    IReadOnlyList<Warehouse> Warehouses,
    IReadOnlyList<Supplier> Suppliers,
    IReadOnlyList<RepairProcedure> RepairProcedures,
    IReadOnlyList<Scenario> Scenarios);

public static class SeedDataLoader
{
    public static readonly JsonSerializerSettings JsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        DateParseHandling = DateParseHandling.DateTimeOffset,   // keep "Z" timestamps as UTC offsets
    };

    /// <summary>Finds evides-leakops/data by walking up from the app folder (works from bin/Debug and from tests).</summary>
    public static string FindDataDirectory(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath)) return Path.GetFullPath(explicitPath);

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "data");
            if (File.Exists(Path.Combine(candidate, "simulation.json")) && File.Exists(Path.Combine(candidate, "sensors.json")))
                return candidate;
        }
        throw new DirectoryNotFoundException("Could not find the evides-leakops/data folder. Pass --data <folder>.");
    }

    public static SeedData Load(string dataDirectory)
    {
        var simulation = ReadObject<SimulationConfig>(dataDirectory, "simulation.json");
        var sensors = ReadList<Sensor>(dataDirectory, "sensors.json");

        return new SeedData(
            Simulation: simulation,
            Sensors: sensors,
            SensorReadings: SimulatedDataGenerator.GenerateReadings(sensors, simulation),
            PipeSegments: ReadList<PipeSegment>(dataDirectory, "pipe-segments.json"),
            Valves: ReadList<Valve>(dataDirectory, "valves.json"),
            PlannedWorks: ReadList<PlannedWork>(dataDirectory, "planned-works.json"),
            WeatherObservations: SimulatedDataGenerator.GenerateWeather(simulation),
            UsageProfiles: SimulatedDataGenerator.GenerateUsageProfiles(sensors, simulation),
            UsageEvents: ReadList<UsageEvent>(dataDirectory, "usage-events.json"),
            Technicians: ReadList<Technician>(dataDirectory, "technicians.json"),
            Inventory: ReadList<InventoryItem>(dataDirectory, "inventory.json"),
            Warehouses: ReadList<Warehouse>(dataDirectory, "warehouses.json"),
            Suppliers: ReadList<Supplier>(dataDirectory, "suppliers.json"),
            RepairProcedures: ReadList<RepairProcedure>(dataDirectory, "repair-procedures.json"),
            Scenarios: ReadList<Scenario>(dataDirectory, "scenarios.json"));
    }

    private static List<T> ReadList<T>(string dir, string file) => ReadObject<List<T>>(dir, file);

    private static T ReadObject<T>(string dir, string file)
    {
        var path = Path.Combine(dir, file);
        var json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<T>(json, JsonSettings)
               ?? throw new InvalidDataException($"{file} is empty or invalid.");
    }
}
