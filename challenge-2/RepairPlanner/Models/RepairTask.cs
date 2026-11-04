using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>Single repair step of a work order.</summary>
public sealed class RepairTask
{
    [JsonPropertyName("sequence")]
    [JsonProperty("sequence")]
    public int Sequence { get; set; }

    [JsonPropertyName("title")]
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("estimatedDurationMinutes")]
    [JsonProperty("estimatedDurationMinutes")]
    public int EstimatedDurationMinutes { get; set; }

    [JsonPropertyName("requiredSkills")]
    [JsonProperty("requiredSkills")]
    public List<string> RequiredSkills { get; set; } = [];

    [JsonPropertyName("safetyNotes")]
    [JsonProperty("safetyNotes")]
    public string SafetyNotes { get; set; } = string.Empty;
}
