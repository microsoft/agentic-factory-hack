using LeakOps.Contracts;

namespace LeakOps.Agents;

/// <summary>One agent as Foundry will know it: the spec (name, instructions, allowed tools) and the C# type of its JSON answer.</summary>
public record CatalogEntry(AgentSpec Spec, Type OutputType, string Description);

/// <summary>
/// The six agents in pipeline order. The registry tool reads this list to create the agents in the Foundry project, so the
/// instructions and tool allow-lists live in one place only: the agent classes.
/// </summary>
public static class AgentCatalog
{
    // typeof(T) is C#'s way to pass a type as a value, like passing a class in Python.
    public static readonly IReadOnlyList<CatalogEntry> All =
    [
        new(TriageAgent.Spec, typeof(TriageVerdict), "Decides whether a SCADA alarm is a genuine leak, a false alarm or unclear. Read-only."),
        new(DiagnosticAgent.Spec, typeof(DiagnosisJudgement), "Locates the breach on the pipe segment and chooses the isolation valves."),
        new(CommunicationsAgent.Spec, typeof(PublicNotice), "Writes the public notice for residents in Dutch and English. No data tools; only the public communication guidelines."),
        new(ResourceMatchmakerAgent.Spec, typeof(MatchJudgement), "Chooses the primary and backup technician for the repair."),
        new(ProcurementAgent.Spec, typeof(ProcurementJudgement), "Decides which parts and equipment to reserve or order."),
        new(SchedulingAgent.Spec, typeof(SchedulingJudgement), "Plans the work order timing and the message to the technician. No tools."),
    ];
}
