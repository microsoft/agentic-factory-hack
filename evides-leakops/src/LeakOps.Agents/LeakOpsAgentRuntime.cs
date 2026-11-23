using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using LeakOps.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace LeakOps.Agents;

/// <summary>Everything that defines one agent: its name, its instructions, the MCP tools it may use (and nothing else), optionally its knowledge base and the Foundry skills it applies.</summary>
public record AgentSpec(string Name, string Instructions, IReadOnlyCollection<string> AllowedTools, KnowledgeSpec? Knowledge = null, IReadOnlyCollection<string>? Skills = null);

/// <summary>
/// The knowledge base an agent may ask (Foundry IQ on Azure AI Search). It is one more MCP tool, next to the data tools, so the same
/// least-privilege idea applies: every agent only gets the knowledge it needs. Connection is the Foundry project connection that holds
/// the endpoint and the sign-in. Instructions are added to the agent's instructions only when the registry switches the knowledge layer on,
/// so the demo also works without it.
/// </summary>
public record KnowledgeSpec(string Connection, string KnowledgeBase, string Instructions);

public static class KnowledgeBases
{
    public const string ToolName = "knowledge_base_retrieve";
    public const string ServerLabel = "leakops_kb";

    // Operational documents (SOPs, playbook, policy, past incident reviews) and, separately, the public communication guidelines.
    public const string OpsConnection = "leakops-kb-ops";
    public const string OpsKnowledgeBase = "leakops-ops-kb";
    public const string CommsConnection = "leakops-kb-comms";
    public const string CommsKnowledgeBase = "leakops-comms-kb";
}

/// <summary>
/// The Foundry skills (preview): versioned how-to's stored in the Foundry project (the folder evides-leakops/skills holds the source).
/// The registry publishes them and injects the default version into the instructions of the agents that list them.
/// </summary>
public static class SkillNames
{
    public const string NoticeStyle = "evides-notice-style";
    public const string IsolationPlaybook = "isolation-playbook";
    public const string EvidenceCitation = "evidence-citation";
}

/// <summary>
/// Shared plumbing for all six agents. It calls the agent registered in the Foundry project, returns a typed answer together with
/// a trace of what the agent did, and offers direct MCP tool calls for the facts the rules check the answers against.
/// </summary>
public class LeakOpsAgentRuntime(AIProjectClient project, AgentSettings settings, ILogger<LeakOpsAgentRuntime> logger)
{
    // Web defaults = camelCase, matching the JSON schema the model is asked to follow.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Without a cap, Foundry reserves a very large output budget for every model call against the deployment's tokens-per-minute
    // limit, and runs one after another get HTTP 429. The answers are small JSON documents, so a cap well above them is plenty.
    private static readonly ChatClientAgentRunOptions RunOptions = new(new ChatOptions { MaxOutputTokens = 8000 });

    public async Task<AgentResult<T>> RunAsync<T>(AgentSpec spec, string input, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        // The agent lives in the Foundry project (created by LeakOps.AgentRegistry): name, instructions, model, answer schema and
        // MCP tools are all part of its definition and visible in the portal. Foundry calls the MCP tools itself, server-side.
        // Here we only refer to the agent by name; the latest version is used.
        AIAgent agent = project
            .AsAIAgent(new AgentReference(spec.Name))
            .AsBuilder()
            .UseOpenTelemetry(sourceName: spec.Name)
            .Build();

        AgentResponse<T> response;
        try
        {
            response = await agent.RunAsync<T>(input, serializerOptions: JsonOptions, options: RunOptions, cancellationToken: cancellationToken);
        }
        catch (ClientResultException e) when (e.Status == 404)
        {
            throw new InvalidOperationException($"Agent '{spec.Name}' does not exist in the Foundry project {settings.ProjectEndpoint}. Register it with deploy/deploy.ps1 or src/LeakOps.AgentRegistry.", e);
        }

        // Tool calls made by Foundry on the MCP server show up in the response as MCP content (plain function calls would too).
        var calls = response.Messages
            .SelectMany(m => m.Contents)
            .Select(c => c switch
            {
                McpServerToolCallContent mcp => new ToolCallTrace(mcp.Name, JsonSerializer.Serialize(mcp.Arguments, JsonOptions)),
                FunctionCallContent function => new ToolCallTrace(function.Name, JsonSerializer.Serialize(function.Arguments, JsonOptions)),
                _ => null,
            })
            .OfType<ToolCallTrace>()
            .ToList();

        var trace = new AgentTrace(spec.Name, settings.ModelDeployment, stopwatch.ElapsedMilliseconds,
            response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount, calls);
        logger.LogInformation("{Agent} finished in {Ms} ms with {Calls} tool call(s): {Tools}",
            spec.Name, trace.DurationMs, calls.Count, string.Join(", ", calls.Select(c => c.Name)));

        return new AgentResult<T>(response.Result, trace);
    }

    /// <summary>
    /// Calls one MCP tool straight from code, without the model, and returns its JSON result. Used to fetch the authoritative
    /// facts (GIS data, procedures) that the rules check the model's answer against. Throws when the tool reports an error.
    /// </summary>
    public async Task<JsonElement> CallToolAsync(string tool, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        await using var mcp = await ConnectMcpAsync(cancellationToken);
        var result = await mcp.CallToolAsync(tool, arguments, cancellationToken: cancellationToken);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        var json = JsonDocument.Parse(text).RootElement.Clone();

        if (result.IsError == true || (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out _)))
            throw new InvalidOperationException($"MCP tool '{tool}' failed: {text}");
        return json;
    }

    private async Task<McpClient> ConnectMcpAsync(CancellationToken cancellationToken)
    {
        var options = new HttpClientTransportOptions { Endpoint = settings.McpServerUrl };
        if (!string.IsNullOrWhiteSpace(settings.McpAuthKey))
            options.AdditionalHeaders = new Dictionary<string, string> { ["x-functions-key"] = settings.McpAuthKey };

        return await McpClient.CreateAsync(new HttpClientTransport(options), cancellationToken: cancellationToken);
    }
}
