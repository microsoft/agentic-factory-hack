namespace RepairPlanner.Services;

/// <summary>Cosmos DB connection settings (read from environment variables in Program.cs).</summary>
public sealed class CosmosDbOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
}
