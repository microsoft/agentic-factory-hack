using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using LeakOps.Contracts;
using LeakOps.Contracts.Logic;
using LeakOps.Workflow;

// Workflow host: runs the six agents in order for one alarm and streams the progress to the operator page as server-sent events.
// The agents are the HTTP functions of LeakOps.Agents; this host only orchestrates and never touches data itself.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(WorkflowSettings.From(builder.Configuration));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ScenarioCatalog>();
builder.Services.AddSingleton<RunLog>();
builder.Services.AddSingleton<RunGuard>();
builder.Services.AddSingleton<RunControl>();
// The agents take a while (a model call each), so the HTTP timeout is generous.
builder.Services.AddHttpClient<IAgentGateway, AgentGateway>(client => client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient<IDemoReset, WaterstoringDemoReset>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddTransient<WorkflowRunner>();

// Telemetry goes to Application Insights when the app runs in Azure (the connection string is an app setting there).
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

var app = builder.Build();
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

// Starting a workflow publishes to residents and books people and stock, so outside Development the API needs the operator key.
var operatorKey = app.Configuration["OPERATOR_KEY"];
if (string.IsNullOrWhiteSpace(operatorKey) && !app.Environment.IsDevelopment())
    throw new InvalidOperationException("OPERATOR_KEY is required outside Development.");

app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (!string.IsNullOrWhiteSpace(operatorKey) && path.StartsWithSegments("/api") && !path.StartsWithSegments("/api/config"))
    {
        var supplied = context.Request.Headers["X-Operator-Key"].ToString();
        // FixedTimeEquals compares without leaking, through timing, how many characters matched.
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(operatorKey)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid X-Operator-Key header." });
            return;
        }
    }
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "workflow" }));
app.MapGet("/api/config", (WorkflowSettings settings) => Results.Ok(new { waterstoringUrl = settings.WaterstoringPublicUrl, keyRequired = !string.IsNullOrWhiteSpace(operatorKey) }));
app.MapGet("/api/scenarios", (ScenarioCatalog catalog) => Results.Ok(catalog.Scenarios));
app.MapGet("/api/runs", (RunLog log) => Results.Ok(log.Recent()));
app.MapGet("/api/runs/{runId}", (string runId, RunLog log) => log.Find(runId) is { } run ? Results.Ok(run) : Results.NotFound());

// POST /api/runs  { "scenarioId": "..." } or { "alarm": { ... } }, optionally "pauseBefore": ["communications", "matchmaker"]
//   default       -> a text/event-stream with one event per step (run_started, step_started, step_completed, step_failed, run_completed)
//   ?wait=true    -> waits for the run and returns the summary as JSON (handy for scripts)
//   pauseBefore   -> the stream sends run_paused before each of those steps and waits for POST /api/runs/{runId}/resume or /cancel (streaming only)
app.MapPost("/api/runs", async (HttpContext context, RunRequest request, ScenarioCatalog catalog, WorkflowRunner runner, RunLog log, RunGuard guard,
    RunControl control, IHostApplicationLifetime lifetime, bool? wait) =>
{
    var alarm = request.Alarm ?? catalog.Scenarios.FirstOrDefault(s => s.Id == request.ScenarioId)?.Alarm;
    if (alarm is null) return Results.BadRequest(new { error = "Give a scenarioId of /api/scenarios, or an alarm." });
    var problem = ValidateAlarm(alarm);
    if (problem is not null) return Results.BadRequest(new { error = problem });
    var pauseSteps = (request.PauseBefore ?? []).Distinct().ToList();
    if (pauseSteps.Any(s => !PauseGate.Allowed.Contains(s)))
        return Results.BadRequest(new { error = $"pauseBefore can only contain: {string.Join(", ", PauseGate.Allowed)}." });
    if (pauseSteps.Count > 0 && wait == true)
        return Results.BadRequest(new { error = "pauseBefore needs the event stream; it cannot be combined with wait=true." });
    if (!guard.TryEnter(alarm.AlarmId))
        return Results.Conflict(new { error = $"A run for alarm {alarm.AlarmId} is already in progress. Wait for it to finish." });

    try
    {
        // The run is not tied to the browser connection: closing the page must not leave a half-booked incident behind.
        var token = lifetime.ApplicationStopping;

        if (wait == true)
        {
            var summary = await runner.RunAsync(alarm, _ => ValueTask.CompletedTask, token);
            log.Add(summary);
            return Results.Ok(summary);
        }

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        var clientGone = false;

        async ValueTask Write(string text)
        {
            if (clientGone) return;
            try
            {
                await context.Response.WriteAsync(text, token);
                await context.Response.Body.FlushAsync(token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
                clientGone = true;
            }
        }

        ValueTask Send(WorkflowEvent e) =>
            Write($"event: {e.Type}\ndata: {JsonSerializer.Serialize(e with { Data = e.Type == WorkflowEventTypes.RunPaused ? new { timeoutSeconds = (int)control.Timeout.TotalSeconds, deadlineUtc = DateTimeOffset.UtcNow + control.Timeout } : e.Data }, json)}\n\n");

        // While paused nothing is sent, and Azure closes an idle response after about 4 minutes, so a comment line goes out every 15 s.
        PauseGate? pause = pauseSteps.Count == 0 ? null : new PauseGate(pauseSteps, async (runId, announce, ct) =>
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task? keepAlive = null;
            try
            {
                return await control.WaitAsync(runId, async () =>
                {
                    await announce();
                    keepAlive = Task.Run(async () =>
                    {
                        try { while (true) { await Task.Delay(TimeSpan.FromSeconds(15), stop.Token); await Write(": keep-alive\n\n"); } }
                        catch (OperationCanceledException) { }
                    });
                }, ct, context.RequestAborted);
            }
            finally
            {
                stop.Cancel();
                if (keepAlive is not null) await keepAlive;
            }
        });

        log.Add(await runner.RunAsync(alarm, Send, token, pause));
        return Results.Empty;
    }
    finally
    {
        guard.Exit(alarm.AlarmId);
    }
});

// The operator page calls these while a run is paused (the operator-key check above covers them too).
app.MapPost("/api/runs/{runId}/resume", (string runId, RunControl control) =>
    control.Resume(runId) ? Results.Ok(new { runId, status = "resumed" }) : Results.NotFound(new { error = $"Run {runId} is not paused." }));
app.MapPost("/api/runs/{runId}/cancel", (string runId, RunControl control) =>
    control.Cancel(runId) ? Results.Ok(new { runId, status = "cancelled" }) : Results.NotFound(new { error = $"Run {runId} is not paused." }));

// Puts the whole demo back to the start (public page, incidents, work orders, purchase orders, reservations, stock) through Waterstoring.
// Refused while any run is active or paused, so a running demo is never pulled out from under itself.
app.MapPost("/api/admin/reset-demo", async (IDemoReset reset, RunGuard guard, RunLog log, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    if (!guard.TryBeginReset())
        return Results.Conflict(new { error = "A run is in progress (or paused). Finish or cancel it first, then reset." });
    try
    {
        await reset.ResetAsync(cancellationToken);
        log.Clear();
        logger.LogInformation("Demo data reset");
        return Results.Ok(new { status = "reset" });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
    finally
    {
        guard.EndReset();
    }
});

app.Run();

static string? ValidateAlarm(AnomalyAlarm alarm)
{
    if (string.IsNullOrWhiteSpace(alarm.AlarmId)) return "alarmId is required.";
    if (!IncidentIds.IsValidAlarmId(alarm.AlarmId)) return "alarmId must look like ALM-2026-1114-0001 (ALM- followed by letters, digits, '.', '_' or '-', at most 59 characters).";
    if (string.IsNullOrWhiteSpace(alarm.SensorId)) return "sensorId is required.";
    if (string.IsNullOrWhiteSpace(alarm.DistrictId)) return "districtId is required.";
    if (alarm.TriggeredAtUtc == default) return "triggeredAtUtc is required (ISO 8601 UTC).";
    return null;
}

record RunRequest(string? ScenarioId, AnomalyAlarm? Alarm, string[]? PauseBefore = null);

public partial class Program;   // makes the entry point visible to the integration tests
