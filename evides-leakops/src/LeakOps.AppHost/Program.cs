using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

// Local orchestration of the whole LeakOps solution with one command: `dotnet run --project src\LeakOps.AppHost`.
//
//   azurite        storage emulator the Azure Functions host needs (npx azurite)
//   mcp            Azure Functions app with the 19 MCP tools        :7071 (func start)
//   agents         Azure Functions app with the six agents          :7072 (func start)
//   waterstoring   the fake Waterstoring.nl                          :5080
//   workflow       workflow host and operator console                :5090
//
// Secrets and settings: taken from the environment, or from the repo-root .env (COSMOS_ENDPOINT, COSMOS_KEY, AZURE_AI_PROJECT_ENDPOINT).
// Nothing is written to a file. The Foundry project may live in another tenant than your default az login: set AZURE_TENANT_ID and
// AZURE_CONFIG_DIR (a separate `az login` profile) before you start, see the README.
var builder = DistributedApplication.CreateBuilder(args);
var root = FindRepoRoot();
var dotEnv = ReadDotEnv(Path.Combine(root, ".env"));
string Setting(string name, string fallback = "") => Environment.GetEnvironmentVariable(name) ?? dotEnv.GetValueOrDefault(name) ?? fallback;

// C# note: "secret: true" parameters are masked in the Aspire dashboard.
var cosmosEndpoint = builder.AddParameter("cosmos-endpoint", () => Setting("COSMOS_ENDPOINT"));
var cosmosKey = builder.AddParameter("cosmos-key", () => Setting("COSMOS_KEY"), secret: true);
var foundryEndpoint = builder.AddParameter("foundry-project-endpoint", () => Setting("AZURE_AI_PROJECT_ENDPOINT"));

var src = Path.Combine(root, "evides-leakops", "src");

// The Functions host fails at startup when the storage emulator is not accepting connections yet, so "azurite is running" is not enough:
// the Functions apps wait until Azurite really listens on its blob, queue and table ports.
builder.Services.AddHealthChecks().AddCheck("azurite-ports", () =>
    new[] { 10000, 10001, 10002 }.All(IsListening) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Azurite is not listening yet."));

var azurite = builder.AddExecutable("azurite", "npx", src, "azurite", "--silent", "--location", Path.Combine(Path.GetTempPath(), "leakops-azurite"),
        "--blobHost", "127.0.0.1", "--queueHost", "127.0.0.1", "--tableHost", "127.0.0.1")
    .WithHealthCheck("azurite-ports");

var waterstoring = builder.AddProject<Projects.LeakOps_Waterstoring>("waterstoring");

var mcp = builder.AddExecutable("mcp", "func", Path.Combine(src, "LeakOps.Mcp"), "start", "--port", "7071")
    .WithHttpEndpoint(port: 7071, name: "http", isProxied: false)
    .WithEnvironment("COSMOS_ENDPOINT", cosmosEndpoint)
    .WithEnvironment("COSMOS_KEY", cosmosKey)
    .WithEnvironment("COSMOS_DATABASE_NAME", "LeakOpsDB")
    .WaitFor(azurite);

var agents = builder.AddExecutable("agents", "func", Path.Combine(src, "LeakOps.Agents"), "start", "--port", "7072")
    .WithHttpEndpoint(port: 7072, name: "http", isProxied: false)
    .WithEnvironment("AZURE_AI_PROJECT_ENDPOINT", foundryEndpoint)
    .WithEnvironment("MODEL_DEPLOYMENT_NAME", Setting("MODEL_DEPLOYMENT_NAME", "gpt-5.4"))
    .WithEnvironment("MCP_SERVER_URL", ReferenceExpression.Create($"{mcp.GetEndpoint("http")}/runtime/webhooks/mcp"))
    .WithEnvironment("WATERSTORING_BASE_URL", waterstoring.GetEndpoint("http"))
    .WaitFor(azurite).WaitFor(mcp).WaitFor(waterstoring);
// Sign-in for the model: pass the tenant and the separate az profile through when they are set (see the README).
if (Setting("AZURE_TENANT_ID") is { Length: > 0 } tenant) agents.WithEnvironment("AZURE_TENANT_ID", tenant);
if (Setting("AZURE_CONFIG_DIR") is { Length: > 0 } azureConfig) agents.WithEnvironment("AZURE_CONFIG_DIR", azureConfig);

builder.AddProject<Projects.LeakOps_Workflow>("workflow")
    .WithEnvironment("AGENTS_BASE_URL", agents.GetEndpoint("http"))
    .WithEnvironment("WATERSTORING_PUBLIC_URL", ReferenceExpression.Create($"{waterstoring.GetEndpoint("http")}/evides"))
    .WaitFor(agents);

builder.Build().Run();

static bool IsListening(int port)
{
    try
    {
        using var client = new System.Net.Sockets.TcpClient();
        return client.ConnectAsync("127.0.0.1", port).Wait(TimeSpan.FromMilliseconds(500));
    }
    catch
    {
        return false;
    }
}

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (Directory.Exists(Path.Combine(dir.FullName, "evides-leakops", "src"))) return dir.FullName;
    throw new InvalidOperationException("Cannot find the repository root (the folder that holds evides-leakops/src).");
}

static Dictionary<string, string> ReadDotEnv(string path) =>
    File.Exists(path)
        ? File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#') && line.Contains('='))
            .Select(line => line.Split('=', 2))
            .GroupBy(parts => parts[0].Trim())
            .ToDictionary(g => g.Key, g => g.Last()[1].Trim().Trim('"'))
        : [];
