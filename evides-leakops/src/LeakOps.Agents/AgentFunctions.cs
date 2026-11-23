using System.Text.Json;
using LeakOps.Contracts;
using LeakOps.Contracts.Logic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace LeakOps.Agents;

/// <summary>HTTP entry points of the Agents function app. One function per agent; the workflow host calls them in order.</summary>
public class AgentFunctions(TriageAgent triage, DiagnosticAgent diagnostic, CommunicationsAgent communications, ResourceMatchmakerAgent matchmaker, ProcurementAgent procurement, SchedulingAgent scheduling, ILogger<AgentFunctions> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Function("Health")]
    public IActionResult Health([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest request) =>
        new OkObjectResult(new { status = "Healthy", service = "leakops-agents" });

    /// <summary>POST /api/agents/triage with an AnomalyAlarm; returns the TriageVerdict and a trace of the agent run.</summary>
    [Function("Triage")]
    public async Task<IActionResult> Triage(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "agents/triage")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var (alarm, bad) = await ReadBodyAsync<AnomalyAlarm>(request, cancellationToken);
        if (bad is not null) return bad;

        var problem = ValidateAlarm(alarm);
        if (problem is not null) return new BadRequestObjectResult(new { error = problem });

        return await RunAsync("Triage", alarm!.AlarmId, () => triage.TriageAsync(alarm, cancellationToken));
    }

    /// <summary>POST /api/agents/diagnostic with { alarm, triage }; only for a triage verdict that confirmed a leak.</summary>
    [Function("Diagnostic")]
    public async Task<IActionResult> Diagnostic(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "agents/diagnostic")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var (body, bad) = await ReadBodyAsync<DiagnosticRequest>(request, cancellationToken);
        if (bad is not null) return bad;

        var problem = body is null ? "Body is required." : ValidateAlarm(body.Alarm) ?? ValidateTriage(body);
        if (problem is not null)
            return problem.StartsWith("Nothing to diagnose")
                ? new UnprocessableEntityObjectResult(new { error = problem })
                : new BadRequestObjectResult(new { error = problem });

        return await RunAsync("Diagnostic", body!.Alarm.AlarmId, () => diagnostic.DiagnoseAsync(body, cancellationToken));
    }

    /// <summary>POST /api/agents/communications with { report }; publishes the public notice to Waterstoring. Not for a report that needs review.</summary>
    [Function("Communications")]
    public async Task<IActionResult> Communications(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "agents/communications")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var (body, bad) = await ReadBodyAsync<CommunicationsRequest>(request, cancellationToken);
        if (bad is not null) return bad;

        var report = body?.Report;
        if (report is null) return new BadRequestObjectResult(new { error = "report is required." });
        if (string.IsNullOrWhiteSpace(report.AlarmId) || string.IsNullOrWhiteSpace(report.Place) || report.ServedStreets is not { Count: > 0 })
            return new BadRequestObjectResult(new { error = "report needs alarmId, place and servedStreets." });
        if (report.NeedsReview)
            return new UnprocessableEntityObjectResult(new
            {
                error = "Nothing to publish: the diagnosis needs review by a person first.",
                reviewReasons = report.ReviewReasons,
            });

        return await RunAsync("Communications", report.AlarmId, () => communications.CommunicateAsync(body!, cancellationToken));
    }

    /// <summary>POST /api/agents/matchmaker with { report }; finds the closest available, certified technician. Not for a report that needs review.</summary>
    [Function("Matchmaker")]
    public async Task<IActionResult> Matchmaker(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "agents/matchmaker")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var (body, bad) = await ReadBodyAsync<MatchmakerRequest>(request, cancellationToken);
        if (bad is not null) return bad;

        var report = body?.Report;
        if (report is null) return new BadRequestObjectResult(new { error = "report is required." });
        if (string.IsNullOrWhiteSpace(report.AlarmId) || string.IsNullOrWhiteSpace(report.Material) || report.DiameterMm <= 0
            || string.IsNullOrWhiteSpace(report.BreachType) || report.TriggeredAtUtc == default)
            return new BadRequestObjectResult(new { error = "report needs alarmId, triggeredAtUtc, material, diameterMm and breachType." });
        if (report.NeedsReview)
            return new UnprocessableEntityObjectResult(new
            {
                error = "Nobody is dispatched: the diagnosis needs review by a person first.",
                reviewReasons = report.ReviewReasons,
            });

        return await RunAsync("Matchmaker", report.AlarmId, () => matchmaker.MatchAsync(body!, cancellationToken));
    }

    /// <summary>POST /api/agents/procurement with { report }; reserves parts and equipment and orders what is short. Not for a report that needs review.</summary>
    [Function("Procurement")]
    public async Task<IActionResult> Procurement(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "agents/procurement")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var (body, bad) = await ReadBodyAsync<ProcurementRequest>(request, cancellationToken);
        if (bad is not null) return bad;

        var report = body?.Report;
        if (report is null) return new BadRequestObjectResult(new { error = "report is required." });
        if (string.IsNullOrWhiteSpace(report.AlarmId) || string.IsNullOrWhiteSpace(report.Material) || report.DiameterMm <= 0
            || string.IsNullOrWhiteSpace(report.BreachType) || string.IsNullOrWhiteSpace(report.Severity) || report.TriggeredAtUtc == default)
            return new BadRequestObjectResult(new { error = "report needs alarmId, triggeredAtUtc, material, diameterMm, breachType and severity." });
        if (report.NeedsReview)
            return new UnprocessableEntityObjectResult(new
            {
                error = "Nothing is reserved: the diagnosis needs review by a person first.",
                reviewReasons = report.ReviewReasons,
            });

        return await RunAsync("Procurement", report.AlarmId, () => procurement.ProcureAsync(body!, cancellationToken));
    }

    /// <summary>POST /api/agents/scheduling with { report, match, package }; creates the work order and updates Waterstoring. Needs a technician and a package nobody has to review.</summary>
    [Function("Scheduling")]
    public async Task<IActionResult> Scheduling(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "agents/scheduling")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var (body, bad) = await ReadBodyAsync<SchedulingRequest>(request, cancellationToken);
        if (bad is not null) return bad;

        var report = body?.Report;
        if (report is null || body!.Match is null || body.Package is null)
            return new BadRequestObjectResult(new { error = "report, match and package are required." });
        if (string.IsNullOrWhiteSpace(report.AlarmId) || string.IsNullOrWhiteSpace(report.SegmentId) || string.IsNullOrWhiteSpace(report.Street)
            || string.IsNullOrWhiteSpace(report.Severity) || string.IsNullOrWhiteSpace(report.BreachType) || report.TriggeredAtUtc == default
            || report.IsolationPlan is null || report.Location is null)
            return new BadRequestObjectResult(new { error = "report needs alarmId, segmentId, street, severity, breachType, triggeredAtUtc, location and isolationPlan." });
        if (body.Match.AlarmId != report.AlarmId || body.Package.AlarmId != report.AlarmId)
            return new BadRequestObjectResult(new { error = "report, match and package belong to different alarms." });

        var blockers = new List<string>();
        if (report.NeedsReview) blockers.Add("the diagnosis needs review");
        if (body.Match.NeedsReview || body.Match.Primary is null) blockers.Add("no technician has been matched: " + string.Join(" ", body.Match.ReviewReasons));
        if (body.Package.NeedsReview) blockers.Add("the parts and equipment cannot be reserved: " + string.Join(" ", body.Package.ReviewReasons));
        if (blockers.Count > 0)
            return new UnprocessableEntityObjectResult(new { error = "Nothing is scheduled: a person has to decide first.", reasons = blockers });

        return await RunAsync("Scheduling", report.AlarmId, () => scheduling.ScheduleAsync(body, cancellationToken));
    }

    private async Task<IActionResult> RunAsync<T>(string agent, string alarmId, Func<Task<AgentResult<T>>> run)
    {
        try
        {
            return new OkObjectResult(await run());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The caller (workflow host) treats any non-200 as "this step failed" and routes the alarm to a person.
            // Azure's content safety can reject a prompt before the model sees it; report that distinctly.
            var blocked = ex.ToString().Contains("content_filter", StringComparison.OrdinalIgnoreCase);
            logger.LogError(ex, "{Agent} failed for alarm {AlarmId} ({Reason})", agent, alarmId, blocked ? "content filter" : "agent error");
            return new ObjectResult(new
            {
                error = $"The {agent.ToLowerInvariant()} agent failed.",
                reason = blocked ? "blocked_by_content_filter" : ex is HttpRequestException ? "publish_failed" : "agent_error",
                detail = ex.Message,
            }) { StatusCode = StatusCodes.Status502BadGateway };
        }
    }

    private static async Task<(T? Body, IActionResult? Error)> ReadBodyAsync<T>(HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return (await request.ReadFromJsonAsync<T>(Json, cancellationToken), null);
        }
        catch (JsonException ex)
        {
            return (default, new BadRequestObjectResult(new { error = $"Body is not valid: {ex.Message}" }));
        }
    }

    private static string? ValidateAlarm(AnomalyAlarm? alarm)
    {
        if (alarm is null) return "Body is required.";
        if (string.IsNullOrWhiteSpace(alarm.AlarmId)) return "alarmId is required.";
        if (!IncidentIds.IsValidAlarmId(alarm.AlarmId)) return "alarmId must look like ALM-2026-1114-0001 (ALM- followed by letters, digits, '.', '_' or '-', at most 59 characters).";
        if (string.IsNullOrWhiteSpace(alarm.SensorId)) return "sensorId is required.";
        if (string.IsNullOrWhiteSpace(alarm.DistrictId)) return "districtId is required.";
        if (alarm.TriggeredAtUtc == default) return "triggeredAtUtc is required (ISO 8601 UTC).";
        return null;
    }

    private static string? ValidateTriage(DiagnosticRequest request)
    {
        var triage = request.Triage;
        if (triage is null) return "triage is required.";
        if (triage.AlarmId != request.Alarm.AlarmId) return "triage.alarmId does not match alarm.alarmId.";
        if (triage.Verdict != "genuine_leak") return $"Nothing to diagnose: the triage verdict is '{triage.Verdict}', not 'genuine_leak'.";
        if (string.IsNullOrWhiteSpace(triage.SuspectedSegmentId)) return "Nothing to diagnose: the triage verdict names no suspected segment.";
        return null;
    }
}
