using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>Spare part stored in the Cosmos DB "PartsInventory" container (partition key: category).</summary>
public sealed class Part
{
    [JsonPropertyName("id")]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("partNumber")]
    [JsonProperty("partNumber")]
    public string PartNumber { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    [JsonProperty("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("compatibleMachines")]
    [JsonProperty("compatibleMachines")]
    public List<string> CompatibleMachines { get; set; } = [];

    [JsonPropertyName("manufacturer")]
    [JsonProperty("manufacturer")]
    public string Manufacturer { get; set; } = string.Empty;

    [JsonPropertyName("quantityInStock")]
    [JsonProperty("quantityInStock")]
    public int QuantityInStock { get; set; }

    [JsonPropertyName("reorderLevel")]
    [JsonProperty("reorderLevel")]
    public int ReorderLevel { get; set; }

    [JsonPropertyName("unitCost")]
    [JsonProperty("unitCost")]
    public double UnitCost { get; set; }

    [JsonPropertyName("leadTimeDays")]
    [JsonProperty("leadTimeDays")]
    public int LeadTimeDays { get; set; }

    [JsonPropertyName("location")]
    [JsonProperty("location")]
    public string Location { get; set; } = string.Empty;
}
