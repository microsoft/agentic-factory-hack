namespace LeakOps.Workflow;

/// <summary>Settings of the workflow host, read from the environment (in Aspire they are filled in by the AppHost).</summary>
public record WorkflowSettings(Uri AgentsBaseUrl, string? AgentsFunctionKey, string WaterstoringPublicUrl)
{
    public static WorkflowSettings From(IConfiguration configuration)
    {
        var agents = configuration["AGENTS_BASE_URL"];
        var key = configuration["AGENTS_FUNCTION_KEY"];
        return new WorkflowSettings(
            new Uri(string.IsNullOrWhiteSpace(agents) ? "http://localhost:7072" : agents),
            string.IsNullOrWhiteSpace(key) ? null : key,
            configuration["WATERSTORING_PUBLIC_URL"] is { Length: > 0 } url ? url : "http://localhost:5080/evides");
    }
}
