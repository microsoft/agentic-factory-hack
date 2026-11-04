using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>Fault diagnosis result coming from the previous agent (input of the Repair Planner).</summary>
public sealed class DiagnosedFault
{
    // Dual attributes: System.Text.Json (agent output) and Newtonsoft.Json (Cosmos DB SDK) use different attribute types.
    [JsonPropertyName("machineId")]
    [JsonProperty("machineId")]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyName("faultType")]
    [JsonProperty("faultType")]
    public string FaultType { get; set; } = string.Empty;

    [JsonPropertyName("rootCause")]
    [JsonProperty("rootCause")]
    public string RootCause { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    [JsonProperty("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonPropertyName("detectedAt")]
    [JsonProperty("detectedAt")]
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("metadata")]
    [JsonProperty("metadata")]
    public Dictionary<string, object?> Metadata { get; set; } = new();
}
