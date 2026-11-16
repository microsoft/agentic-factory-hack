namespace LeakOps.Contracts;

/// <summary>The six agents in the order the workflow runs them. The name of a step is also the route of its function: /api/agents/{step}.</summary>
public static class WorkflowSteps
{
    public const string Triage = "triage";
    public const string Diagnostic = "diagnostic";
    public const string Communications = "communications";
    public const string Matchmaker = "matchmaker";
    public const string Procurement = "procurement";
    public const string Scheduling = "scheduling";

    public static readonly IReadOnlyList<string> All = [Triage, Diagnostic, Communications, Matchmaker, Procurement, Scheduling];
}

public static class WorkflowOutcomes
{
    public const string Completed = "completed";                   // all six agents ran
    public const string ClosedFalseAlarm = "closed_false_alarm";   // Triage found no leak: nothing else happens
    public const string NeedsReview = "needs_review";              // an agent says a person must decide before the workflow can go on
    public const string Failed = "failed";                         // an agent could not do its job (error, timeout, content filter)
    public const string Cancelled = "cancelled";                   // the operator paused the run and cancelled it (or never resumed it); nothing further was done
}

public static class WorkflowEventTypes
{
    public const string RunStarted = "run_started";
    public const string StepStarted = "step_started";
    public const string StepCompleted = "step_completed";
    public const string StepFailed = "step_failed";
    public const string RunPaused = "run_paused";       // the run waits before a step until the operator resumes or cancels
    public const string RunResumed = "run_resumed";
    public const string RunCompleted = "run_completed";
}

/// <summary>
/// An operator-requested pause before a step. <see cref="Wait"/> is given the run id and an "announce" callback that it must call once the
/// run is registered as paused (so a resume can never arrive before it is registered); it returns true to go on, false to cancel the run.
/// </summary>
public record PauseGate(string Step, Func<string, Func<ValueTask>, CancellationToken, Task<bool>> Wait);

/// <summary>One progress message of a run. Sent to the operator page as a server-sent event.</summary>
public record WorkflowEvent(
    string Type,
    string RunId,
    DateTimeOffset AtUtc,
    string? Step = null,
    int? Index = null,
    int? Total = null,
    long? DurationMs = null,
    string? Headline = null,         // one line for the operator
    object? Data = null,             // the agent's full answer and its trace
    string? Outcome = null,          // run_completed only
    IReadOnlyList<string>? Reasons = null,
    WorkflowSummary? Summary = null);

public record StepRecord(string Step, string Status, long DurationMs, string Headline);   // Status: completed | failed

public record WorkflowSummary(
    string RunId,
    string AlarmId,
    string Outcome,
    DateTimeOffset StartedUtc,
    long TotalMs,
    IReadOnlyList<StepRecord> Steps,
    string? StoppedAt,               // the step after which the workflow did not go on (needs_review, closed_false_alarm, failed)
    IReadOnlyList<string> Reasons,
    string? IncidentId,
    string? WorkOrderId,
    DateTimeOffset? ExpectedResolutionUtc);

/// <summary>How the workflow reaches an agent. The host implements it with HTTP calls to the Agents function app; tests use a fake.</summary>
public interface IAgentGateway
{
    Task<AgentResult<T>> CallAsync<T>(string step, object body, CancellationToken cancellationToken);
}

/// <summary>An agent answered with an error or could not be reached.</summary>
public class AgentCallException(string step, int statusCode, string reason, string message) : Exception(message)
{
    public string Step { get; } = step;
    public int StatusCode { get; } = statusCode;
    public string Reason { get; } = reason;
}
