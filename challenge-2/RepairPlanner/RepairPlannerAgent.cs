using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;
using RepairPlanner.Services;

namespace RepairPlanner;

/// <summary>
/// Repair Planner Agent: turns a diagnosed fault into a work order.
/// Flow: mapping (skills/parts) -> Cosmos DB (technicians, parts) -> Foundry agent (plan) -> Cosmos DB (save).
/// </summary>
// Primary constructor: the parameters become fields usable in every method (like Python's __init__ storing self.x).
public sealed class RepairPlannerAgent(
    AIProjectClient projectClient,
    CosmosDbService cosmosDb,
    IFaultMappingService faultMapping,
    string modelDeploymentName,
    ILogger<RepairPlannerAgent> logger)
{
    private const string AgentName = "RepairPlannerAgent";

    private const string AgentInstructions = """
        You are a Repair Planner Agent for tire manufacturing equipment.
        Generate a repair plan with tasks, timeline, and resource allocation.
        Return the response as valid JSON matching the WorkOrder schema. Return ONLY the JSON object, no Markdown.

        Output JSON with these fields:
        - workOrderNumber, machineId, title, description
        - type: "corrective" | "preventive" | "emergency"
        - priority: "critical" | "high" | "medium" | "low"
        - status, assignedTo (technician id or null), notes
        - estimatedDuration: integer (minutes, e.g. 60 not "60 minutes")
        - partsUsed: [{ partId, partNumber, quantity }]
        - tasks: [{ sequence, title, description, estimatedDurationMinutes (integer), requiredSkills, safetyNotes }]

        IMPORTANT: All duration fields must be integers representing minutes (e.g. 90), not strings.

        Rules:
        - Assign the most qualified available technician
        - Include only relevant parts; empty array if none needed
        - Tasks must be ordered and actionable
        """;

    // LLMs sometimes return numbers as strings ("90"); AllowReadingFromString makes parsing tolerant.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private ProjectsAgentVersion? _agentVersion;

    /// <summary>Registers (or creates a new version of) the agent in the Foundry project.</summary>
    public async Task EnsureAgentVersionAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Creating agent '{Agent}' with model '{Model}'", AgentName, modelDeploymentName);

        var definition = new DeclarativeAgentDefinition(modelDeploymentName) { Instructions = AgentInstructions };
        var response = await projectClient.AgentAdministrationClient.CreateAgentVersionAsync(
            AgentName, new ProjectsAgentVersionCreationOptions(definition), cancellationToken: ct);

        _agentVersion = response.Value;
        logger.LogInformation("Agent version: {Version}", _agentVersion.Version);
    }

    public async Task<WorkOrder> PlanAndCreateWorkOrderAsync(DiagnosedFault fault, CancellationToken ct = default)
    {
        if (_agentVersion is null)
        {
            await EnsureAgentVersionAsync(ct);
        }

        logger.LogInformation("Planning repair for {Machine}, fault={Fault}", fault.MachineId, fault.FaultType);

        // 1. Required skills and parts from the static mapping
        var requiredSkills = faultMapping.GetRequiredSkills(fault.FaultType);
        var requiredPartNumbers = faultMapping.GetRequiredParts(fault.FaultType);

        // 2. Available technicians and parts inventory from Cosmos DB
        var technicians = await cosmosDb.GetAvailableTechniciansWithSkillsAsync(requiredSkills, ct);
        var parts = await cosmosDb.GetPartsInventoryAsync(requiredPartNumbers, ct);

        // 3. Ask the agent for a plan
        var prompt = BuildPrompt(fault, requiredSkills, technicians, parts);
        logger.LogInformation("Invoking agent '{Agent}'", AgentName);

        var agent = projectClient.AsAIAgent(_agentVersion!);
        var response = await agent.RunAsync(prompt, cancellationToken: ct);
        var text = response.Text ?? string.Empty;

        // 4. Parse the response and apply defaults
        var workOrder = ParseWorkOrder(text);
        ApplyDefaults(workOrder, fault, technicians, parts);

        // 5. Save to Cosmos DB
        return await cosmosDb.CreateWorkOrderAsync(workOrder, ct);
    }

    private static string BuildPrompt(
        DiagnosedFault fault,
        IReadOnlyList<string> requiredSkills,
        List<Technician> technicians,
        List<Part> parts)
    {
        var faultJson = JsonSerializer.Serialize(fault, JsonOptions);

        var technicianLines = technicians.Count == 0
            ? "(none available)"
            : string.Join("\n", technicians.Select(t =>
                $"- id={t.Id}, name={t.Name}, role={t.Role}, shift={t.ShiftSchedule}, skills=[{string.Join(", ", t.Skills)}], certifications=[{string.Join(", ", t.Certifications)}]"));

        var partLines = parts.Count == 0
            ? "(no parts required)"
            : string.Join("\n", parts.Select(p =>
                $"- partId={p.Id}, partNumber={p.PartNumber}, name={p.Name}, inStock={p.QuantityInStock}, leadTimeDays={p.LeadTimeDays}, location={p.Location}"));

        return $"""
            Create a repair work order for this diagnosed fault.

            Diagnosed fault:
            {faultJson}

            Required skills: {string.Join(", ", requiredSkills)}

            Available technicians (already filtered on availability and skills):
            {technicianLines}

            Parts inventory for this fault:
            {partLines}

            Respond with the work order JSON only.
            """;
    }

    private WorkOrder ParseWorkOrder(string text)
    {
        // Models sometimes wrap JSON in Markdown fences; keep only the outermost { ... }
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            logger.LogError("Agent response did not contain JSON: {Text}", text);
            throw new InvalidOperationException("Agent response did not contain a JSON object.");
        }

        try
        {
            return JsonSerializer.Deserialize<WorkOrder>(text[start..(end + 1)], JsonOptions)
                   ?? throw new InvalidOperationException("Agent response was empty JSON.");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Could not parse agent response as WorkOrder: {Text}", text);
            throw new InvalidOperationException("Agent response was not a valid work order.", ex);
        }
    }

    private static void ApplyDefaults(
        WorkOrder workOrder, DiagnosedFault fault, List<Technician> technicians, List<Part> parts)
    {
        // ??= means "assign if null/empty" (like Python's: x = x or default_value). Strings need a manual empty check.
        if (string.IsNullOrWhiteSpace(workOrder.Id))
        {
            workOrder.Id = $"wo-{Guid.NewGuid():N}"[..15];
        }
        if (string.IsNullOrWhiteSpace(workOrder.WorkOrderNumber))
        {
            workOrder.WorkOrderNumber = $"WO-{DateTime.UtcNow:yyyy}-{Random.Shared.Next(1000, 9999)}";
        }
        if (string.IsNullOrWhiteSpace(workOrder.MachineId))
        {
            workOrder.MachineId = fault.MachineId;
        }
        if (string.IsNullOrWhiteSpace(workOrder.Type))
        {
            workOrder.Type = "corrective";
        }
        if (string.IsNullOrWhiteSpace(workOrder.Priority))
        {
            workOrder.Priority = fault.Severity.ToLowerInvariant() switch
            {
                "critical" => "critical",
                "high" => "high",
                "low" => "low",
                _ => "medium",
            };
        }

        // New work orders always start as "new" (also the Cosmos DB partition key)
        workOrder.Status = "new";
        workOrder.CreatedDate = DateTimeOffset.UtcNow;

        // Never trust an assignee the model made up: it must be one of the available technicians
        if (workOrder.AssignedTo is not null && technicians.All(t => t.Id != workOrder.AssignedTo))
        {
            workOrder.AssignedTo = null;
        }

        // Resolve part ids from the inventory when the model only returned part numbers
        foreach (var usage in workOrder.PartsUsed)
        {
            var part = parts.FirstOrDefault(p =>
                string.Equals(p.PartNumber, usage.PartNumber, StringComparison.OrdinalIgnoreCase) || p.Id == usage.PartId);
            if (part is not null)
            {
                usage.PartId = part.Id;
                usage.PartNumber = part.PartNumber;
            }
        }

        if (workOrder.EstimatedDuration <= 0)
        {
            workOrder.EstimatedDuration = workOrder.Tasks.Sum(t => t.EstimatedDurationMinutes);
        }
    }
}
