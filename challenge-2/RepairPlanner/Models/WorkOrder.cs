using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>Work order stored in the Cosmos DB "WorkOrders" container (partition key: status).</summary>
public sealed class WorkOrder
{
    [JsonPropertyName("id")]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("workOrderNumber")]
    [JsonProperty("workOrderNumber")]
    public string WorkOrderNumber { get; set; } = string.Empty;

    [JsonPropertyName("machineId")]
    [JsonProperty("machineId")]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    // "corrective" | "preventive" | "emergency"
    [JsonPropertyName("type")]
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    // "critical" | "high" | "medium" | "low"
    [JsonPropertyName("priority")]
    [JsonProperty("priority")]
    public string Priority { get; set; } = string.Empty;

    // Cosmos DB partition key
    [JsonPropertyName("status")]
    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    // technician id, or null when nobody is assigned
    [JsonPropertyName("assignedTo")]
    [JsonProperty("assignedTo")]
    public string? AssignedTo { get; set; }

    [JsonPropertyName("createdDate")]
    [JsonProperty("createdDate")]
    public DateTimeOffset CreatedDate { get; set; }

    [JsonPropertyName("scheduledDate")]
    [JsonProperty("scheduledDate")]
    public DateTimeOffset? ScheduledDate { get; set; }

    // minutes
    [JsonPropertyName("estimatedDuration")]
    [JsonProperty("estimatedDuration")]
    public int EstimatedDuration { get; set; }

    [JsonPropertyName("partsUsed")]
    [JsonProperty("partsUsed")]
    public List<WorkOrderPartUsage> PartsUsed { get; set; } = [];

    [JsonPropertyName("tasks")]
    [JsonProperty("tasks")]
    public List<RepairTask> Tasks { get; set; } = [];

    [JsonPropertyName("notes")]
    [JsonProperty("notes")]
    public string Notes { get; set; } = string.Empty;
}
