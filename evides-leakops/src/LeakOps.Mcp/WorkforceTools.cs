using LeakOps.Contracts;
using LeakOps.Contracts.Logic;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace LeakOps.Mcp;

/// <summary>Tool for the Resource Matchmaker agent: live roster and certification matrix.</summary>
public class WorkforceTools(LeakOpsStore store, ILogger<WorkforceTools> logger)
{
    [Function(nameof(GetTechnicians))]
    public async Task<string> GetTechnicians(
        [McpToolTrigger("get-technicians", "Scans the live roster at a moment in time. For every technician: on shift or not, busy with another job, certifications and which required ones are missing, last known position, and straight-line distance and estimated travel time to the site. Sorted with available and fully certified technicians first, nearest first.")]
        ToolInvocationContext context,
        [McpToolProperty("atUtc", "The moment the technician is needed, ISO 8601 UTC. Normally the alarm time.", isRequired: true)] string atUtc,
        [McpToolProperty("requiredCertifications", "Comma separated certification codes from the repair procedure, for example CI-REPAIR-L3,EXCAVATOR-OP,TRAFFIC-MGMT", isRequired: false)] string? requiredCertifications,
        [McpToolProperty("siteLat", "Latitude of the repair site", isRequired: false)] double? siteLat,
        [McpToolProperty("siteLon", "Longitude of the repair site", isRequired: false)] double? siteLon,
        [McpToolProperty("availableOnly", "When true, leave out technicians who are not available at that moment. Default false.", isRequired: false)] bool? availableOnly,
        [McpToolProperty("excludeIncidentId", "The incident you are planning, for example INC-2026-1114-0001. Technicians already committed to OTHER incidents count as busy; this incident's own work order does not.", isRequired: false)] string? excludeIncidentId,
        [McpToolProperty("jobHours", "How many hours the technician will be committed from atUtc, normally the repair hours of the procedure. A technician who is busy at any time in that window counts as busy. Default 0: only the moment itself is checked.", isRequired: false)] double? jobHours)
    {
        if (!ToolJson.TryTime(atUtc, "atUtc", out var at, out var error)) return error;
        var required = ToolJson.SplitList(requiredCertifications);
        GeoPoint? site = siteLat is not null && siteLon is not null ? new GeoPoint(siteLat.Value, siteLon.Value) : null;
        var commitment = TimeSpan.FromHours(Math.Clamp(jobHours ?? 0, 0, 24));

        var workOrders = await store.ListWorkOrdersAsync();
        var statuses = (await store.ListTechniciansAsync())
            .Select(t => WorkforceAvailability.Evaluate(WorkforceAvailability.WithAssignments(t, workOrders, excludeIncidentId), at, required, site, commitment))
            .Where(s => availableOnly != true || s.Available)
            .OrderByDescending(s => s.Available && s.HasAllRequiredCertifications)
            .ThenBy(s => s.EstimatedTravelMinutes ?? int.MaxValue)
            .ThenBy(s => s.Id)
            .ToList();
        logger.LogInformation("get-technicians at {At} required [{Required}] -> {Count}", at, string.Join(",", required), statuses.Count);

        return ToolJson.Ok(new
        {
            atUtc = at,
            requiredCertifications = required,
            site,
            count = statuses.Count,
            qualifiedAndAvailable = statuses.Count(s => s.Available && s.HasAllRequiredCertifications),
            technicians = statuses,
        });
    }
}
