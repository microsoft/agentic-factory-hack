using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;

namespace RepairPlanner.Services;

/// <summary>Cosmos DB data access for technicians, parts and work orders.</summary>
// Primary constructor: the parameters (options, logger) become usable fields (like Python's __init__ storing self.x).
public sealed class CosmosDbService(CosmosDbOptions options, ILogger<CosmosDbService> logger) : IDisposable
{
    private readonly CosmosClient _client = new(options.Endpoint, options.Key);

    private Container Technicians => _client.GetContainer(options.DatabaseName, "Technicians");
    private Container Parts => _client.GetContainer(options.DatabaseName, "PartsInventory");
    private Container WorkOrders => _client.GetContainer(options.DatabaseName, "WorkOrders");

    /// <summary>Available technicians that have at least one of the required skills, best match first.</summary>
    public async Task<List<Technician>> GetAvailableTechniciansWithSkillsAsync(
        IReadOnlyList<string> requiredSkills, CancellationToken ct = default)
    {
        try
        {
            var query = new QueryDefinition(
                    "SELECT * FROM c WHERE c.available = true " +
                    "AND EXISTS (SELECT VALUE s FROM s IN c.skills WHERE ARRAY_CONTAINS(@skills, s))")
                .WithParameter("@skills", requiredSkills);

            var result = await ReadAllAsync<Technician>(Technicians, query, ct);

            // Rank by number of matching skills (like sorted(..., key=..., reverse=True) in Python)
            var ranked = result
                .OrderByDescending(t => t.Skills.Count(s => requiredSkills.Contains(s, StringComparer.OrdinalIgnoreCase)))
                .ToList();

            logger.LogInformation("Found {Count} available technicians matching skills", ranked.Count);
            return ranked;
        }
        catch (CosmosException ex)
        {
            logger.LogError(ex, "Cosmos DB error while querying technicians (status {Status})", ex.StatusCode);
            throw;
        }
    }

    /// <summary>Parts inventory entries for the given part numbers.</summary>
    public async Task<List<Part>> GetPartsInventoryAsync(
        IReadOnlyList<string> partNumbers, CancellationToken ct = default)
    {
        if (partNumbers.Count == 0)
        {
            logger.LogInformation("No part numbers requested, skipping inventory lookup");
            return [];
        }

        try
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE ARRAY_CONTAINS(@partNumbers, c.partNumber)")
                .WithParameter("@partNumbers", partNumbers);

            var result = await ReadAllAsync<Part>(Parts, query, ct);
            logger.LogInformation("Fetched {Count} parts", result.Count);
            return result;
        }
        catch (CosmosException ex)
        {
            logger.LogError(ex, "Cosmos DB error while querying parts (status {Status})", ex.StatusCode);
            throw;
        }
    }

    /// <summary>Stores the work order (partition key: status) and returns the saved document.</summary>
    public async Task<WorkOrder> CreateWorkOrderAsync(WorkOrder workOrder, CancellationToken ct = default)
    {
        try
        {
            var response = await WorkOrders.CreateItemAsync(
                workOrder, new PartitionKey(workOrder.Status), cancellationToken: ct);
            logger.LogInformation("Saved work order {Number} (id={Id}, RU={Charge})",
                workOrder.WorkOrderNumber, workOrder.Id, response.RequestCharge);
            return response.Resource;
        }
        catch (CosmosException ex)
        {
            logger.LogError(ex, "Cosmos DB error while saving work order {Number} (status {Status})",
                workOrder.WorkOrderNumber, ex.StatusCode);
            throw;
        }
    }

    private static async Task<List<T>> ReadAllAsync<T>(Container container, QueryDefinition query, CancellationToken ct)
    {
        var items = new List<T>();
        using var iterator = container.GetItemQueryIterator<T>(query);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            items.AddRange(page);
        }
        return items;
    }

    public void Dispose() => _client.Dispose();
}
