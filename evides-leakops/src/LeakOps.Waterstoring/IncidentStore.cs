using System.Collections.Concurrent;
using LeakOps.Contracts;
using Microsoft.Azure.Cosmos;

namespace LeakOps.Waterstoring;

/// <summary>Where incidents are kept. In memory when running on a laptop, in Cosmos DB when deployed, so a restart does not empty the page.</summary>
public interface IIncidentRepository
{
    Task<WaterstoringIncident?> GetAsync(string id);
    Task SaveAsync(WaterstoringIncident incident);
    Task<IReadOnlyList<WaterstoringIncident>> AllAsync();
    Task ClearAsync();
}

public class InMemoryIncidentRepository : IIncidentRepository
{
    private readonly ConcurrentDictionary<string, WaterstoringIncident> _incidents = new(StringComparer.OrdinalIgnoreCase);

    public Task<WaterstoringIncident?> GetAsync(string id) => Task.FromResult(_incidents.GetValueOrDefault(id));
    public Task SaveAsync(WaterstoringIncident incident) { _incidents[incident.IncidentId] = incident; return Task.CompletedTask; }
    public Task<IReadOnlyList<WaterstoringIncident>> AllAsync() => Task.FromResult<IReadOnlyList<WaterstoringIncident>>(_incidents.Values.ToList());
    public Task ClearAsync() { _incidents.Clear(); return Task.CompletedTask; }
}

/// <summary>One document per incident in the WaterstoringIncidents container (partition key /id).</summary>
public class CosmosIncidentRepository(Container container) : IIncidentRepository
{
    // C# note: a record with a primary constructor; the Cosmos serializer writes Id as the "id" property every document needs.
    private record Document(string Id, WaterstoringIncident Incident);

    public async Task<WaterstoringIncident?> GetAsync(string id)
    {
        try
        {
            return (await container.ReadItemAsync<Document>(Key(id), new PartitionKey(Key(id)))).Resource.Incident;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task SaveAsync(WaterstoringIncident incident) =>
        await container.UpsertItemAsync(new Document(Key(incident.IncidentId), incident), new PartitionKey(Key(incident.IncidentId)));

    public async Task<IReadOnlyList<WaterstoringIncident>> AllAsync()
    {
        var found = new List<WaterstoringIncident>();
        using var feed = container.GetItemQueryIterator<Document>(new QueryDefinition("SELECT * FROM c"));
        while (feed.HasMoreResults) found.AddRange((await feed.ReadNextAsync()).Select(d => d.Incident));
        return found;
    }

    public async Task ClearAsync()
    {
        var ids = new List<string>();
        using var feed = container.GetItemQueryIterator<Document>(new QueryDefinition("SELECT * FROM c"));
        while (feed.HasMoreResults) ids.AddRange((await feed.ReadNextAsync()).Select(d => d.Id));
        foreach (var id in ids) await container.DeleteItemAsync<Document>(id, new PartitionKey(id));
    }

    // Incident ids are case-insensitive on the site; the document id is always lower case so "INC-1" and "inc-1" are one document.
    private static string Key(string incidentId) => incidentId.ToLowerInvariant();
}

/// <summary>
/// The incident list of the fake site and the rules for changing it. Every change is read, changed and saved under one lock, so two
/// calls at the same time cannot overwrite each other (the site runs as a single instance).
/// </summary>
public class IncidentStore(IIncidentRepository repository, TimeProvider time)
{
    // C# note: "(IIncidentRepository repository, TimeProvider time)" after the class name is a primary constructor; both are usable in every method.
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Creates the incident, or replaces it when the IncidentId already exists (so retries are safe). A repeated "reported" notice never
    /// takes an incident that has moved on (repair planned, in progress, resolved) back to "reported", and a repeat that changes nothing
    /// adds nothing to the public timeline.
    /// </summary>
    public async Task<(WaterstoringIncident Incident, bool Created)> UpsertAsync(WaterstoringIncidentRequest request)
    {
        await _gate.WaitAsync();
        try
        {
            var now = time.GetUtcNow();
            var existing = await repository.GetAsync(request.IncidentId);

            if (existing is not null)
            {
                if (request.Status == WaterstoringStatus.Reported && existing.Status != WaterstoringStatus.Reported)
                    request = request with
                    {
                        Status = existing.Status, Message = existing.Message, MessageEn = existing.MessageEn, ExpectedResolutionUtc = existing.ExpectedResolutionUtc,
                    };

                var unchanged = request.Status == existing.Status && request.Message == existing.Message && request.MessageEn == existing.MessageEn
                                && request.ExpectedResolutionUtc == existing.ExpectedResolutionUtc;
                var timeline = unchanged
                    ? existing.Timeline
                    : [.. existing.Timeline, new WaterstoringTimelineEntry(now, request.Status, request.Message, request.MessageEn, request.ExpectedResolutionUtc)];
                var replaced = FromRequest(request, existing.CreatedUtc, unchanged ? existing.UpdatedUtc : now, timeline);
                await repository.SaveAsync(replaced);
                return (replaced, false);
            }

            var entry = new WaterstoringTimelineEntry(now, request.Status, request.Message, request.MessageEn, request.ExpectedResolutionUtc);
            var created = FromRequest(request, now, now, [entry]);
            await repository.SaveAsync(created);
            return (created, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WaterstoringIncident?> UpdateAsync(string id, WaterstoringIncidentUpdate update)
    {
        await _gate.WaitAsync();
        try
        {
            var current = await repository.GetAsync(id);
            if (current is null) return null;

            var now = time.GetUtcNow();
            var status = update.Status ?? current.Status;
            var message = update.Message ?? current.Message;
            var messageEn = update.MessageEn ?? current.MessageEn;
            var eta = update.ClearExpectedResolution == true ? null : update.ExpectedResolutionUtc ?? current.ExpectedResolutionUtc;

            // A repeated update (an agent retry) changes nothing and must not add a line to the public timeline.
            if (status == current.Status && message == current.Message && messageEn == current.MessageEn && eta == current.ExpectedResolutionUtc)
                return current;

            var updated = current with
            {
                Status = status,
                Message = message,
                MessageEn = messageEn,
                ExpectedResolutionUtc = eta,
                UpdatedUtc = now,
                Timeline = [.. current.Timeline, new WaterstoringTimelineEntry(now, status, message, messageEn, eta)],
            };
            await repository.SaveAsync(updated);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<WaterstoringIncident?> GetAsync(string id) => repository.GetAsync(id);

    /// <summary>Active incidents first (newest update first), then resolved ones.</summary>
    public async Task<IReadOnlyList<WaterstoringIncident>> ListAsync(bool activeOnly) =>
        (await repository.AllAsync())
            .Where(i => !activeOnly || WaterstoringStatus.IsActive(i.Status))
            .OrderBy(i => WaterstoringStatus.IsActive(i.Status) ? 0 : 1)
            .ThenByDescending(i => i.UpdatedUtc)
            .ToList();

    /// <summary>Finds active incidents by postcode (4 digits or full), street or place.</summary>
    public async Task<IReadOnlyList<WaterstoringIncident>> SearchAsync(string? query)
    {
        var text = (query ?? "").Trim();
        if (text.Length < 3) return [];

        var compact = new string(text.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        var looksLikePostcode = compact.Length >= 4 && compact.Take(4).All(char.IsDigit);

        return (await ListAsync(activeOnly: true)).Where(i => looksLikePostcode
            ? i.AffectedPostalCodes.Any(p => Compact(p).StartsWith(compact, StringComparison.Ordinal))
            : i.Place.Contains(text, StringComparison.OrdinalIgnoreCase)
              || i.AffectedStreets.Any(s => s.Contains(text, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public async Task ClearAsync()
    {
        await _gate.WaitAsync();
        try { await repository.ClearAsync(); }
        finally { _gate.Release(); }
    }

    private static string Compact(string postcode) => postcode.Replace(" ", "").ToUpperInvariant();

    private static WaterstoringIncident FromRequest(
        WaterstoringIncidentRequest r, DateTimeOffset created, DateTimeOffset updated, IReadOnlyList<WaterstoringTimelineEntry> timeline) =>
        new(r.IncidentId, r.Title, r.Message, r.TitleEn, r.MessageEn, r.Status, r.Place, r.AffectedStreets, r.AffectedPostalCodes,
            r.Location, r.AffectedPath, r.CustomersAffected, r.ExpectedResolutionUtc, r.Advice, r.AdviceEn, created, updated, timeline);
}
