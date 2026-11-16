using System.Net;
using System.Net.Http.Json;
using LeakOps.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LeakOps.Tests;

public class WaterstoringApiTests
{
    private const string ApiKey = "dev-waterstoring-key";   // the Development default of the fake site

    private static HttpClient NewClient(WebApplicationFactory<Program> factory, bool withKey = true)
    {
        var client = factory.CreateClient();
        if (withKey) client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }

    private static WaterstoringIncidentRequest Sample(string id = "INC-1", string status = WaterstoringStatus.Reported) => new(
        IncidentId: id,
        Title: "Storing waterleiding Dorpsweg",
        Message: "Door een leidingbreuk is er geen of weinig water.",
        TitleEn: "Water main disruption Dorpsweg",
        MessageEn: "A pipe burst means little or no water.",
        Status: status,
        Place: "Spijkenisse",
        AffectedStreets: ["Dorpsweg", "Havenlaan"],
        AffectedPostalCodes: ["3201 AB", "3202 BA"],
        Location: new GeoPoint(51.8608, 4.3303),
        AffectedPath: [new GeoPoint(51.8612, 4.3281), new GeoPoint(51.8601, 4.3342)],
        CustomersAffected: 1840,
        ExpectedResolutionUtc: null,
        Advice: "Houd uw kranen dicht tot het water terug is.",
        AdviceEn: "Keep your taps closed until the water is back.");

    [Fact]
    public async Task Writing_requires_the_api_key()
    {
        using var factory = new WebApplicationFactory<Program>();

        var response = await NewClient(factory, withKey: false).PostAsJsonAsync("/api/incidents", Sample());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await NewClient(factory, withKey: false).GetFromJsonAsync<List<WaterstoringIncident>>("/api/incidents") ?? []);
    }

    [Fact]
    public async Task Posting_the_same_incident_again_replaces_it()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = NewClient(factory);

        var first = await client.PostAsJsonAsync("/api/incidents", Sample());
        var second = await client.PostAsJsonAsync("/api/incidents", Sample(status: WaterstoringStatus.RepairPlanned));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var incidents = await client.GetFromJsonAsync<List<WaterstoringIncident>>("/api/incidents");
        var incident = Assert.Single(incidents!);
        Assert.Equal(WaterstoringStatus.RepairPlanned, incident.Status);
        Assert.Equal(2, incident.Timeline.Count);
    }

    [Fact]
    public async Task Patch_changes_status_and_expected_resolution_and_extends_the_timeline()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = NewClient(factory);
        await client.PostAsJsonAsync("/api/incidents", Sample());
        var eta = new DateTimeOffset(2026, 11, 14, 9, 30, 0, TimeSpan.Zero);

        var response = await client.PatchAsJsonAsync("/api/incidents/INC-1",
            new WaterstoringIncidentUpdate(WaterstoringStatus.RepairInProgress, "Monteur is ter plaatse.", null, eta));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var incident = (await response.Content.ReadFromJsonAsync<WaterstoringIncident>())!;
        Assert.Equal(WaterstoringStatus.RepairInProgress, incident.Status);
        Assert.Equal(eta, incident.ExpectedResolutionUtc);
        Assert.Equal("Monteur is ter plaatse.", incident.Message);
        Assert.Equal("A pipe burst means little or no water.", incident.MessageEn);   // untouched fields stay
        Assert.Equal(2, incident.Timeline.Count);
    }

    [Fact]
    public async Task A_repeated_reported_notice_does_not_take_a_planned_repair_back_to_reported()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = NewClient(factory);
        var eta = new DateTimeOffset(2026, 11, 14, 17, 15, 0, TimeSpan.Zero);
        await client.PostAsJsonAsync("/api/incidents", Sample());
        await client.PatchAsJsonAsync("/api/incidents/INC-1", new WaterstoringIncidentUpdate(WaterstoringStatus.RepairPlanned, "Monteur ingepland.", "Technician scheduled.", eta));

        var again = await client.PostAsJsonAsync("/api/incidents", Sample());   // Communications runs again, with status "reported"

        var incident = (await again.Content.ReadFromJsonAsync<WaterstoringIncident>())!;
        Assert.Equal(WaterstoringStatus.RepairPlanned, incident.Status);
        Assert.Equal(eta, incident.ExpectedResolutionUtc);
        Assert.Equal("Monteur ingepland.", incident.Message);
        Assert.Equal(2, incident.Timeline.Count);   // the notice, and the one real change: no flapping back and forth
    }

    [Fact]
    public async Task Posting_an_identical_incident_again_adds_nothing_to_the_timeline()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = NewClient(factory);

        await client.PostAsJsonAsync("/api/incidents", Sample());
        var again = await client.PostAsJsonAsync("/api/incidents", Sample());

        Assert.Single((await again.Content.ReadFromJsonAsync<WaterstoringIncident>())!.Timeline);
    }

    [Fact]
    public async Task An_expected_resolution_time_that_can_no_longer_be_promised_can_be_withdrawn()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = NewClient(factory);
        await client.PostAsJsonAsync("/api/incidents", Sample());
        await client.PatchAsJsonAsync("/api/incidents/INC-1", new WaterstoringIncidentUpdate(WaterstoringStatus.RepairPlanned, "a", "a", new DateTimeOffset(2026, 11, 14, 17, 15, 0, TimeSpan.Zero)));

        var cleared = await client.PatchAsJsonAsync("/api/incidents/INC-1", new WaterstoringIncidentUpdate(WaterstoringStatus.RepairPlanned, "b", "b", null, ClearExpectedResolution: true));

        Assert.Null((await cleared.Content.ReadFromJsonAsync<WaterstoringIncident>())!.ExpectedResolutionUtc);
    }

    [Fact]
    public async Task Repeating_the_same_patch_does_not_add_to_the_timeline()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = NewClient(factory);
        await client.PostAsJsonAsync("/api/incidents", Sample());
        var update = new WaterstoringIncidentUpdate(WaterstoringStatus.RepairPlanned, "Monteur ingepland.", null, new DateTimeOffset(2026, 11, 14, 17, 15, 0, TimeSpan.Zero));

        await client.PatchAsJsonAsync("/api/incidents/INC-1", update);
        var again = await client.PatchAsJsonAsync("/api/incidents/INC-1", update);

        var incident = (await again.Content.ReadFromJsonAsync<WaterstoringIncident>())!;
        Assert.Equal(2, incident.Timeline.Count);   // the sample incident plus one real change
    }

    [Fact]
    public async Task Patch_rejects_unknown_incident_and_invalid_status()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = NewClient(factory);
        await client.PostAsJsonAsync("/api/incidents", Sample());

        var missing = await client.PatchAsJsonAsync("/api/incidents/NOPE", new WaterstoringIncidentUpdate(null, "x", null, null));
        var invalid = await client.PatchAsJsonAsync("/api/incidents/INC-1", new WaterstoringIncidentUpdate("exploded", null, null, null));

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Invalid_incidents_are_rejected_with_details()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = NewClient(factory);

        var response = await client.PostAsJsonAsync("/api/incidents",
            Sample(id: "bad id!") with { Status = "unknown", AffectedStreets = [], AffectedPostalCodes = [] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("incidentId", body);
        Assert.Contains("status", body);
        Assert.Contains("affected", body);
    }

    [Theory]
    [InlineData("3201 AB", true)]
    [InlineData("3201ab", true)]
    [InlineData("3201", true)]
    [InlineData("3202", true)]
    [InlineData("dorpsweg", true)]
    [InlineData("Spijkenisse", true)]
    [InlineData("3999 ZZ", false)]
    [InlineData("Hoogvliet", false)]
    [InlineData("32", false)]
    public async Task Search_matches_postcode_street_and_place(string query, bool expected)
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = NewClient(factory);
        await client.PostAsJsonAsync("/api/incidents", Sample());

        var result = await client.GetFromJsonAsync<SearchResult>($"/api/search?q={Uri.EscapeDataString(query)}");

        Assert.Equal(expected, result!.HasIncident);
    }

    [Fact]
    public async Task Resolved_incidents_leave_the_search_but_stay_in_the_list()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = NewClient(factory);
        await client.PostAsJsonAsync("/api/incidents", Sample());
        await client.PatchAsJsonAsync("/api/incidents/INC-1", new WaterstoringIncidentUpdate(WaterstoringStatus.Resolved, "Opgelost.", null, null));

        var search = await client.GetFromJsonAsync<SearchResult>("/api/search?q=3201");
        var all = await client.GetFromJsonAsync<List<WaterstoringIncident>>("/api/incidents");
        var active = await client.GetFromJsonAsync<List<WaterstoringIncident>>("/api/incidents?active=true");

        Assert.False(search!.HasIncident);
        Assert.Single(all!);
        Assert.Empty(active!);
    }

    [Fact]
    public async Task Home_page_is_served_and_reset_clears_the_list()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = NewClient(factory);
        await client.PostAsJsonAsync("/api/incidents", Sample());

        var page = await client.GetStringAsync("/");
        var reset = await client.PostAsync("/api/admin/reset", null);

        Assert.Contains("waterstoring", page, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<WaterstoringIncident>>("/api/incidents"))!);
    }

    private record SearchResult(string? Query, bool HasIncident, List<WaterstoringIncident> Incidents);

    [Fact]
    public async Task Reset_also_accepts_the_operator_key_but_no_other_key()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("OPERATOR_KEY", "op-key"));
        var writer = NewClient(factory);
        await writer.PostAsJsonAsync("/api/incidents", Sample());

        using var wrong = new HttpRequestMessage(HttpMethod.Post, "/api/admin/reset");
        wrong.Headers.Add("X-Operator-Key", "not-the-key");
        using var right = new HttpRequestMessage(HttpMethod.Post, "/api/admin/reset");
        right.Headers.Add("X-Operator-Key", "op-key");
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(wrong)).StatusCode);
        Assert.Single((await writer.GetFromJsonAsync<List<WaterstoringIncident>>("/api/incidents"))!);
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.SendAsync(right)).StatusCode);
        Assert.Empty((await writer.GetFromJsonAsync<List<WaterstoringIncident>>("/api/incidents"))!);
    }

    [Fact]
    public async Task Operator_key_does_not_open_the_ingest_api_and_an_unset_key_never_matches()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("OPERATOR_KEY", "op-key"));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Operator-Key", "op-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/incidents", Sample())).StatusCode);

        using var unset = new WebApplicationFactory<Program>();   // no OPERATOR_KEY: an empty header must not match
        using var empty = new HttpRequestMessage(HttpMethod.Post, "/api/admin/reset");
        empty.Headers.Add("X-Operator-Key", "");
        Assert.Equal(HttpStatusCode.Unauthorized, (await unset.CreateClient().SendAsync(empty)).StatusCode);
    }
}
