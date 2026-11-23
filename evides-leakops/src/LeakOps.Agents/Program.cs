using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using LeakOps.Agents;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// The Agents function app: one HTTP function per agent (Triage now; the other five are added in steps 5-9), all running on
// the same shared runtime. The workflow host calls them in order.
var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

builder.Services.AddSingleton(sp => AgentSettings.From(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(sp =>
{
    var settings = sp.GetRequiredService<AgentSettings>();

    // Explicit, predictable credentials (DefaultAzureCredential may try a Visual Studio or VS Code account of another tenant first).
    // In Azure (WEBSITE_SITE_NAME is set there): the function app's managed identity.
    // Locally: the Azure CLI sign-in, pinned to the tenant of the Foundry project via AZURE_TENANT_ID.
    TokenCredential credential = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") is not null
        ? new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)
        : new AzureCliCredential(new AzureCliCredentialOptions { TenantId = settings.TenantId });

    return new AIProjectClient(new Uri(settings.ProjectEndpoint), credential);
});
builder.Services.AddHttpClient<WaterstoringClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<LeakOpsAgentRuntime>();
builder.Services.AddSingleton<TriageAgent>();
builder.Services.AddSingleton<DiagnosticAgent>();
builder.Services.AddSingleton<CommunicationsAgent>();
builder.Services.AddSingleton<ResourceMatchmakerAgent>();
builder.Services.AddSingleton<ProcurementAgent>();
builder.Services.AddSingleton<SchedulingAgent>();

builder.Build().Run();
