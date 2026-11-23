using Microsoft.Extensions.Configuration;

namespace LeakOps.Agents;

/// <summary>Configuration shared by all agents. Environment variable names follow .github/copilot-instructions.md.</summary>
public record AgentSettings(
    string ProjectEndpoint,
    string ModelDeployment,
    Uri McpServerUrl,
    string? McpAuthKey,
    string? TenantId,
    Uri WaterstoringBaseUrl,
    string? WaterstoringApiKey)
{
    public static AgentSettings From(IConfiguration config)
    {
        var endpoint = config["AZURE_AI_PROJECT_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT is required (the Foundry project endpoint).");

        // "is { Length: > 0 } configured" is a pattern match: true when the value is not null and not empty.
        var model = config["MODEL_DEPLOYMENT_NAME"] is { Length: > 0 } configured ? configured : "gpt-5.4";
        var mcp = config["MCP_SERVER_URL"] ?? "http://localhost:7071/runtime/webhooks/mcp";
        var waterstoring = config["WATERSTORING_BASE_URL"] ?? "http://localhost:5080";

        return new AgentSettings(
            ProjectEndpoint: endpoint,
            ModelDeployment: model,
            McpServerUrl: new Uri(mcp),
            McpAuthKey: config["MCP_AUTH_KEY"],
            TenantId: config["AZURE_TENANT_ID"],   // needed when the CLI's default account is in another tenant than the Foundry project
            WaterstoringBaseUrl: new Uri(waterstoring),
            WaterstoringApiKey: config["WATERSTORING_API_KEY"]);
    }
}
