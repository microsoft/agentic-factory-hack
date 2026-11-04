using System.Text.Json;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RepairPlanner;
using RepairPlanner.Models;
using RepairPlanner.Services;

// Read configuration from environment variables (load them first: export $(cat ../.env | xargs))
static string Env(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Missing environment variable: {name}");

var projectEndpoint = Env("AZURE_AI_PROJECT_ENDPOINT");
var modelDeploymentName = Env("MODEL_DEPLOYMENT_NAME");
var cosmosOptions = new CosmosDbOptions
{
    Endpoint = Env("COSMOS_ENDPOINT"),
    Key = Env("COSMOS_KEY"),
    DatabaseName = Env("COSMOS_DATABASE_NAME"),
};

// Dependency injection container: registers the services once and builds the agent with them
var services = new ServiceCollection();
services.AddLogging(b => b.AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss ").SetMinimumLevel(LogLevel.Information));
services.AddSingleton(cosmosOptions);
services.AddSingleton<CosmosDbService>();
services.AddSingleton<IFaultMappingService, FaultMappingService>();
services.AddSingleton(new AIProjectClient(new Uri(projectEndpoint), new DefaultAzureCredential()));
services.AddSingleton(sp => new RepairPlannerAgent(
    sp.GetRequiredService<AIProjectClient>(),
    sp.GetRequiredService<CosmosDbService>(),
    sp.GetRequiredService<IFaultMappingService>(),
    modelDeploymentName,
    sp.GetRequiredService<ILogger<RepairPlannerAgent>>()));

// await using disposes the provider (and CosmosDbService) at the end, like Python's "async with"
await using var provider = services.BuildServiceProvider();
var logger = provider.GetRequiredService<ILogger<Program>>();

// Sample diagnosed fault, shaped like the output of the Fault Diagnosis Agent (Challenge 1)
var fault = new DiagnosedFault
{
    MachineId = "machine-001",
    FaultType = "curing_temperature_excessive",
    RootCause = "Heating element malfunction",
    Severity = "High",
    DetectedAt = DateTimeOffset.UtcNow,
    Metadata = new Dictionary<string, object?>
    {
        ["observedCuringTemperatureC"] = 179.2,
        ["warningThresholdC"] = 178,
        ["machineType"] = "tire_curing_press",
    },
};

try
{
    var agent = provider.GetRequiredService<RepairPlannerAgent>();
    await agent.EnsureAgentVersionAsync();

    var workOrder = await agent.PlanAndCreateWorkOrderAsync(fault);

    logger.LogInformation("Saved work order {Number} (id={Id}, status={Status}, assignedTo={Assignee})",
        workOrder.WorkOrderNumber, workOrder.Id, workOrder.Status, workOrder.AssignedTo ?? "unassigned");

    Console.WriteLine();
    Console.WriteLine(JsonSerializer.Serialize(workOrder, new JsonSerializerOptions { WriteIndented = true }));
}
catch (Exception ex)
{
    logger.LogError(ex, "Repair planning failed");
    return 1;
}

return 0;
