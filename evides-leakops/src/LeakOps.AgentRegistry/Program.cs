using System.ClientModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Core;
using Azure.Identity;
using LeakOps.Agents;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

// Creates (or updates) the six LeakOps agents in the Foundry project, so they show up in the portal at https://ai.azure.com
// and can be tested, traced and evaluated there. Rerunnable: a new agent version is only created when the definition changed.
//
// Settings (environment variables):
//   AZURE_AI_PROJECT_ENDPOINT  required  the Foundry project endpoint
//   MCP_SERVER_URL             required  public URL of the MCP server (Foundry calls it server-side)
//   MODEL_DEPLOYMENT_NAME      optional  default gpt-5.4
//   KNOWLEDGE_SEARCH_ENDPOINT  optional  Azure AI Search endpoint of the Foundry IQ knowledge bases; switches knowledge on for the agents that have it
//   KNOWLEDGE_API_VERSION      optional  search API version of the knowledge base MCP endpoint, default 2026-08-01-preview
//   --no-knowledge             register the agents with data tools only, even when the search endpoint is set
//   SKILLS_DIR                 optional  folder with the skills (default: evides-leakops/skills, found by walking up from the app)
//   --no-skills                register the agents without the Foundry skills
//   MCP_CONNECTION_NAME        optional  project connection that holds the MCP key, default leakops-mcp
//   AZURE_TENANT_ID            optional  tenant for the Azure CLI sign-in
//   --list                     print the agents in the project and exit
//   --delete                   delete the six LeakOps agents and exit
static string Required(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new InvalidOperationException($"{name} is required.");

var endpoint = Required("AZURE_AI_PROJECT_ENDPOINT");
var model = Environment.GetEnvironmentVariable("MODEL_DEPLOYMENT_NAME") is { Length: > 0 } m ? m : "gpt-5.4";
var connection = Environment.GetEnvironmentVariable("MCP_CONNECTION_NAME") is { Length: > 0 } c ? c : "leakops-mcp";
var tenant = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");

TokenCredential credential = new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenant });
var admin = new AIProjectClient(new Uri(endpoint), credential).AgentAdministrationClient;

async Task<ProjectsAgentVersion?> LatestVersionAsync(string name)
{
    ProjectsAgentVersion? latest = null;
    try
    {
        await foreach (var version in admin.GetAgentVersionsAsync(name))
            if (latest is null || version.CreatedAt > latest.CreatedAt) latest = version;
    }
    catch (ClientResultException e) when (e.Status == 404) { }
    return latest;
}

if (args.Contains("--list"))
{
    await foreach (var agent in admin.GetAgentsAsync())
        Console.WriteLine($"{agent.Name}  latest version={(await LatestVersionAsync(agent.Name))?.Version}");
    return;
}

if (args.Contains("--delete"))
{
    foreach (var entry in AgentCatalog.All)
    {
        await admin.DeleteAgentAsync(entry.Spec.Name, force: true);
        Console.WriteLine($"deleted {entry.Spec.Name}");
    }
    return;
}

var mcpUrl = new Uri(Required("MCP_SERVER_URL"));

// Knowledge (Foundry IQ) is switched on when the search endpoint is known (deploy-knowledge.ps1 sets it up first). Without it the agents
// are registered with data tools only, exactly as before. --no-knowledge forces that, for comparing the two.
var searchEndpoint = args.Contains("--no-knowledge") ? null : Environment.GetEnvironmentVariable("KNOWLEDGE_SEARCH_ENDPOINT")?.TrimEnd('/');
var knowledgeApiVersion = Environment.GetEnvironmentVariable("KNOWLEDGE_API_VERSION") is { Length: > 0 } kv ? kv : "2026-08-01-preview";

var schemaOptions = new AIJsonSchemaCreateOptions
{
    // Strict structured output: every property required, no extra properties. Nullable fields become ["type","null"].
    TransformOptions = new AIJsonSchemaTransformOptions { DisallowAdditionalProperties = true, RequireAllProperties = true },
};

// Foundry skills (preview): published from evides-leakops/skills when their text changed, then injected into the instructions of the agents
// that list them (the "direct injection" mode of the docs). --no-skills registers the agents without them.
var skillStore = args.Contains("--no-skills") ? null
    : new SkillStore(new HttpClient(), new Uri(endpoint), credential, SkillStore.FindDirectory(Environment.GetEnvironmentVariable("SKILLS_DIR")));
var skillCache = new Dictionary<string, SkillVersion>();

foreach (var entry in AgentCatalog.All)
{
    var spec = entry.Spec;
    var knowledge = searchEndpoint is not null ? spec.Knowledge : null;

    var skills = new List<SkillVersion>();
    foreach (var skillName in skillStore is null ? [] : spec.Skills ?? [])
    {
        if (!skillCache.TryGetValue(skillName, out var skill))
        {
            skillCache[skillName] = skill = await skillStore!.EnsureAsync(skillName, CancellationToken.None);
            Console.WriteLine($"skill {skill.Name}: version {skill.Version}{(skill.Published ? " (published now)" : " (unchanged)")}");
        }
        skills.Add(skill);
    }

    // The answer format must be part of the agent definition: a schema passed at call time is ignored for registered agents.
    var schema = AIJsonUtilities.CreateJsonSchema(entry.OutputType, serializerOptions: new JsonSerializerOptions(JsonSerializerDefaults.Web), inferenceOptions: schemaOptions);
    var instructions = knowledge is null ? spec.Instructions : spec.Instructions + "\n\n" + knowledge.Instructions;
    if (skills.Count > 0)
        instructions += "\n\nSkills (versioned how-to's from Foundry; apply the ones that fit your task):\n\n" +
            string.Join("\n\n", skills.Select(s => $"### Skill {s.Name} (version {s.Version})\n{s.Body}"));
    var definition = new DeclarativeAgentDefinition(model)
    {
        Instructions = instructions,
        TextOptions = new ResponseTextOptions
        {
            TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(entry.OutputType.Name, BinaryData.FromString(schema.GetRawText()), jsonSchemaIsStrict: true),
        },
    };

    // Least privilege: the MCP tool only exposes the tools on this agent's allow-list. Agents without tools get none.
    if (spec.AllowedTools.Count > 0)
    {
        var filter = new McpToolFilter();
        foreach (var tool in spec.AllowedTools) filter.ToolNames.Add(tool);

        var mcp = ResponseTool.CreateMcpTool(
            serverLabel: "leakops_mcp",
            serverUri: mcpUrl,
            allowedTools: filter,
            toolCallApprovalPolicy: new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.NeverRequireApproval));
        mcp.ProjectConnectionId = connection;
        definition.Tools.Add(mcp);
    }

    // The knowledge base is a second MCP tool with a single allowed tool. Foundry signs in with the project's managed identity through
    // the project connection, so there is no key in the definition. Each agent gets its own knowledge base (ops or communications).
    var knowledgeUrl = knowledge is null ? null : $"{searchEndpoint}/knowledgebases/{knowledge.KnowledgeBase}/mcp?api-version={knowledgeApiVersion}";
    if (knowledge is not null)
    {
        var kbFilter = new McpToolFilter();
        kbFilter.ToolNames.Add(KnowledgeBases.ToolName);

        var kb = ResponseTool.CreateMcpTool(
            serverLabel: KnowledgeBases.ServerLabel,
            serverUri: new Uri(knowledgeUrl!),
            allowedTools: kbFilter,
            toolCallApprovalPolicy: new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.NeverRequireApproval));
        kb.ProjectConnectionId = knowledge.Connection;
        definition.Tools.Add(kb);
    }

    // The hash of everything that defines the agent decides whether a new version is needed.
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('\n', model, instructions, schema.GetRawText(), connection, mcpUrl, string.Join(',', spec.AllowedTools), knowledge?.Connection, knowledgeUrl)))).ToLowerInvariant()[..16];

    string? currentHash = null;
    (await LatestVersionAsync(spec.Name))?.Metadata.TryGetValue("definitionHash", out currentHash);

    if (currentHash == hash)
    {
        Console.WriteLine($"{spec.Name}: up to date");
        continue;
    }

    var options = new ProjectsAgentVersionCreationOptions(definition) { Description = entry.Description };
    options.Metadata["definitionHash"] = hash;
    options.Metadata["demo"] = "evides-leakops";
    var created = (await admin.CreateAgentVersionAsync(spec.Name, options)).Value;
    Console.WriteLine($"{spec.Name}: created version {created.Version} ({spec.AllowedTools.Count} tool(s){(knowledge is null ? "" : ", knowledge: " + knowledge.KnowledgeBase)}{(skills.Count == 0 ? "" : ", skills: " + string.Join(" ", skills.Select(s => $"{s.Name} v{s.Version}")))})");
}
