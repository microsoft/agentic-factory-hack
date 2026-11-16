using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using LeakOps.Contracts;
using LeakOps.Waterstoring;
using Microsoft.Azure.Cosmos;

// Fake Waterstoring.nl: a public page where residents look up disruptions, plus an API-key protected ingest API
// that the Communications and Scheduling agents call. This is a demo, not the real waterstoring.nl.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => CreateCosmosClient(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<IIncidentRepository>(sp => CreateRepository(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<CosmosHolder>().Client));
builder.Services.AddSingleton<IDemoData>(sp => CreateDemoData(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<CosmosHolder>().Client));
builder.Services.AddSingleton<IncidentStore>();

// Telemetry goes to Application Insights when the app runs in Azure (the connection string is an app setting there).
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

var app = builder.Build();

// The key must be configured outside Development; Development falls back to a well-known demo key.
var apiKey = app.Configuration["WATERSTORING_API_KEY"] ?? (app.Environment.IsDevelopment() ? "dev-waterstoring-key" : null);
if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("WATERSTORING_API_KEY is required outside Development.");

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "waterstoring" }));
app.MapGet("/evides", (IWebHostEnvironment env) => Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html"));
app.MapGet("/technicians", (IWebHostEnvironment env) => Results.File(Path.Combine(env.WebRootPath, "technicians.html"), "text/html"));
app.MapGet("/warehouse", (IWebHostEnvironment env) => Results.File(Path.Combine(env.WebRootPath, "warehouse.html"), "text/html"));

// ---- Public, read-only ------------------------------------------------------------------------------------------

app.MapGet("/api/incidents", async (IncidentStore store, bool? active) => Results.Ok(await store.ListAsync(active ?? false)));

app.MapGet("/api/incidents/{id}", async (string id, IncidentStore store) =>
    await store.GetAsync(id) is { } incident ? Results.Ok(incident) : Results.NotFound());

app.MapGet("/api/search", async (IncidentStore store, string? q) =>
{
    var incidents = await store.SearchAsync(q);
    return Results.Ok(new { query = q, hasIncident = incidents.Count > 0, incidents });
});

// The simulated roster of the Evides field technicians and who is assigned to which job (demo data, no real people).
// "at" overrides the moment the state is shown for; by default it is the simulation clock, the time of the demo alarm.
app.MapGet("/api/technicians", async (IDemoData data, IConfiguration config, DateTimeOffset? at) =>
    Results.Ok(TechnicianBoard.Build(await data.TechniciansAsync(), await data.WorkOrdersAsync(), SimulationClock(config, at))));

// The simulated stock of the two Evides depots: what is on hand, reserved for an incident and on order (demo data).
app.MapGet("/api/warehouse", async (IDemoData data, IConfiguration config, DateTimeOffset? at) =>
    Results.Ok(WarehouseBoard.Build(await data.InventoryAsync(), await data.WarehousesAsync(), await data.SuppliersAsync(),
        await data.ReservationsAsync(), await data.PurchaseOrdersAsync(), SimulationClock(config, at))));

static DateTimeOffset SimulationClock(IConfiguration config, DateTimeOffset? at) =>
    at ?? (DateTimeOffset.TryParse(config["DEMO_CLOCK_UTC"], out var configured) ? configured : DateTimeOffset.Parse("2026-11-14T03:10:00Z"));

// ---- Protected: header "X-Api-Key" ------------------------------------------------------------------------------

app.MapPost("/api/incidents", async (WaterstoringIncidentRequest request, IncidentStore store, ILogger<Program> logger) =>
{
    var errors = Validate(request);
    if (errors.Count > 0) return Results.ValidationProblem(errors);

    var (incident, created) = await store.UpsertAsync(request);
    logger.LogInformation("Incident {IncidentId} {Action} with status {Status}", incident.IncidentId, created ? "created" : "replaced", incident.Status);
    return created ? Results.Created($"/api/incidents/{incident.IncidentId}", incident) : Results.Ok(incident);
}).AddEndpointFilter(RequireApiKey);

app.MapPatch("/api/incidents/{id}", async (string id, WaterstoringIncidentUpdate update, IncidentStore store, ILogger<Program> logger) =>
{
    if (update.Status is not null && !WaterstoringStatus.IsValid(update.Status))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = [$"Must be one of: {string.Join(", ", WaterstoringStatus.All)}"] });

    var incident = await store.UpdateAsync(id, update);
    if (incident is null) return Results.NotFound();

    logger.LogInformation("Incident {IncidentId} updated: status {Status}, expected resolution {Eta}", id, incident.Status, incident.ExpectedResolutionUtc);
    return Results.Ok(incident);
}).AddEndpointFilter(RequireApiKey);

// The "Clear alerts" button on the page calls this with the operator key (the same key as the operator console), so the demo
// can be reset without knowing the ingest key. The ingest key keeps working for scripts.
app.MapPost("/api/admin/reset", async (IncidentStore store, ILogger<Program> logger) =>
{
    await store.ClearAsync();
    logger.LogInformation("All incidents cleared");
    return Results.NoContent();
}).AddEndpointFilter(RequireApiKeyOrOperatorKey);

// Puts the whole demo back to the start: the public page, incidents, work orders (so nobody is assigned), purchase orders,
// stock reservations, and the stock levels. The operator console's "Reset demo data" button calls this.
app.MapPost("/api/admin/reset-demo", async (IncidentStore store, IDemoData data, ILogger<Program> logger) =>
{
    await store.ClearAsync();
    await data.ResetAsync();
    logger.LogInformation("Demo data reset");
    return Results.NoContent();
}).AddEndpointFilter(RequireApiKeyOrOperatorKey);

app.Run();

// C# note: local functions can be declared after the statements that use them.
async ValueTask<object?> RequireApiKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
{
    var supplied = context.HttpContext.Request.Headers["X-Api-Key"].ToString();
    return KeyMatches(supplied, apiKey) ? await next(context) : Results.Json(new { error = "Missing or invalid X-Api-Key header." }, statusCode: StatusCodes.Status401Unauthorized);
}

async ValueTask<object?> RequireApiKeyOrOperatorKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
{
    var headers = context.HttpContext.Request.Headers;
    var ok = KeyMatches(headers["X-Api-Key"].ToString(), apiKey) || KeyMatches(headers["X-Operator-Key"].ToString(), app.Configuration["OPERATOR_KEY"]);
    return ok ? await next(context) : Results.Json(new { error = "Missing or invalid X-Operator-Key (or X-Api-Key) header." }, statusCode: StatusCodes.Status401Unauthorized);
}

// FixedTimeEquals compares without leaking, through timing, how many characters matched. An unset key never matches.
static bool KeyMatches(string supplied, string? expected) =>
    !string.IsNullOrEmpty(expected) && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected));

// Incidents live in memory on a laptop. In Azure the app setting WATERSTORING_STORE=cosmos keeps them in the LeakOps Cosmos database,
// so a restart or a new deployment does not empty the page. In Azure the app's managed identity is used; locally a COSMOS_KEY works too.
static bool UsesCosmos(IConfiguration config) => string.Equals(config["WATERSTORING_STORE"], "cosmos", StringComparison.OrdinalIgnoreCase);

static string DatabaseName(IConfiguration config)
{
    var database = config["COSMOS_DATABASE_NAME"] ?? LeakOpsContainers.DefaultDatabase;
    if (!database.StartsWith("LeakOps", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"COSMOS_DATABASE_NAME is '{database}', but Waterstoring only uses databases named LeakOps*.");
    return database;
}

static IIncidentRepository CreateRepository(IConfiguration config, CosmosClient? client) =>
    client is null ? new InMemoryIncidentRepository() : new CosmosIncidentRepository(client.GetContainer(DatabaseName(config), LeakOpsContainers.WaterstoringIncidents));

// The roster and the seed stock ship with the app (data/*.json); locally they are found by walking up to evides-leakops/data.
static IDemoData CreateDemoData(IConfiguration config, CosmosClient? client)
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        var directory = Path.Combine(dir.FullName, "data");
        if (File.Exists(Path.Combine(directory, "technicians.json")))
            return client is null ? new FileDemoData(directory) : new CosmosDemoData(client, DatabaseName(config), directory);
    }
    throw new DirectoryNotFoundException("Could not find the data folder with technicians.json.");
}

static CosmosHolder CreateCosmosClient(IConfiguration config)
{
    if (!UsesCosmos(config)) return new CosmosHolder(null);

    var endpoint = config["COSMOS_ENDPOINT"] ?? throw new InvalidOperationException("COSMOS_ENDPOINT is required when WATERSTORING_STORE is 'cosmos'.");
    DatabaseName(config);

    var options = new CosmosClientOptions
    {
        ApplicationName = "leakops-waterstoring",
        SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase },
    };
    var key = config["COSMOS_KEY"];
    var client = string.IsNullOrWhiteSpace(key) ? new CosmosClient(endpoint, new DefaultAzureCredential(), options) : new CosmosClient(endpoint, key, options);
    return new CosmosHolder(client);
}

static Dictionary<string, string[]> Validate(WaterstoringIncidentRequest r)
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(r.IncidentId) || !Regex.IsMatch(r.IncidentId, "^[A-Za-z0-9._-]{1,64}$"))
        errors["incidentId"] = ["Required: 1-64 characters from A-Z a-z 0-9 . _ -"];
    if (string.IsNullOrWhiteSpace(r.Title)) errors["title"] = ["Required"];
    if (string.IsNullOrWhiteSpace(r.Message)) errors["message"] = ["Required"];
    if (!WaterstoringStatus.IsValid(r.Status)) errors["status"] = [$"Must be one of: {string.Join(", ", WaterstoringStatus.All)}"];
    if (string.IsNullOrWhiteSpace(r.Place)) errors["place"] = ["Required"];
    if ((r.AffectedStreets?.Count ?? 0) == 0 && (r.AffectedPostalCodes?.Count ?? 0) == 0)
        errors["affected"] = ["Give at least one affected street or postal code"];
    if (r.Location is null || r.Location.Lat is < -90 or > 90 || r.Location.Lon is < -180 or > 180)
        errors["location"] = ["Required: valid lat/lon"];
    if (r.CustomersAffected < 0) errors["customersAffected"] = ["Must be 0 or more"];
    return errors;
}

public partial class Program;   // makes the entry point visible to the integration tests

/// <summary>The one Cosmos client of the app (null when incidents are kept in memory). A holder because the DI container does not store nulls.</summary>
public record CosmosHolder(CosmosClient? Client);
