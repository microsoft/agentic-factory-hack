using Azure.Identity;
using LeakOps.Mcp;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// MCP server for the Evides LeakOps agents. Every [McpToolTrigger] function in this project becomes an MCP tool at
// <function-app>/runtime/webhooks/mcp. The tools read the simulated data in Cosmos DB and write reservations, purchase
// orders and work orders.
// C# note: the Functions "isolated worker" runs your code in its own process, started like any .NET host.
var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

// One CosmosClient for the whole process (it is thread-safe and expensive to create).
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var endpoint = config["COSMOS_ENDPOINT"] ?? throw new InvalidOperationException("COSMOS_ENDPOINT is required");
    var key = config["COSMOS_KEY"];
    var options = new CosmosClientOptions
    {
        ApplicationName = "leakops-mcp",
        SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase },
    };

    // Local development uses the key. In Azure, leave COSMOS_KEY empty and the function app's managed identity is used.
    // The "??" operator picks the right-hand side only when the left side is null.
    return string.IsNullOrWhiteSpace(key)
        ? new CosmosClient(endpoint, new DefaultAzureCredential(), options)
        : new CosmosClient(endpoint, key, options);
});
builder.Services.AddSingleton<LeakOpsStore>();

builder.Build().Run();
