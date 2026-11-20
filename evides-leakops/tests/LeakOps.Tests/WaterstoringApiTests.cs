using System.Net;
using System.Net.Http.Json;
using LeakOps.Contracts;
using LeakOps.Contracts.Logic;
using LeakOps.Waterstoring;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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

    // ---- Technicians page and reset-demo ----

    private static readonly DateTimeOffset AlarmTime = DateTimeOffset.Parse("2026-11-14T03:10:00Z");

    private static WorkOrder OrderFor(string technicianId, string incidentId = "INC-ALM-2026-1114-0001") => new(
        "WO-" + incidentId, incidentId, "SEG-0412", "Dorpsweg (Spijkenisse)", 160, new GeoPoint(51.86, 4.33), "burst", "S1", "Burst on a cast-iron main",
        technicianId, "Name", "PROC-1", [], [], [], AlarmTime.AddMinutes(15), 20, 30, 0, 5, AlarmTime.AddMinutes(65), AlarmTime.AddHours(6), "scheduled", AlarmTime, null, AlarmTime);

    private sealed class FakeDemoData(params WorkOrder[] orders) : IDemoData
    {
        private readonly FileDemoData _seed = new(LeakOps.Seeder.SeedDataLoader.FindDataDirectory());
        public int Resets;
        public List<InventoryItem>? Stock;   // when set, replaces the seed stock (the test's "after Procurement" state)
        public List<StockReservation> Reservations = [];
        public List<PurchaseOrder> Orders = [];

        public Task<IReadOnlyList<Technician>> TechniciansAsync() => _seed.TechniciansAsync();
        public Task<IReadOnlyList<WorkOrder>> WorkOrdersAsync() => Task.FromResult<IReadOnlyList<WorkOrder>>(orders);
        public async Task<IReadOnlyList<InventoryItem>> InventoryAsync() => Stock ?? await _seed.InventoryAsync();
        public Task<IReadOnlyList<Warehouse>> WarehousesAsync() => _seed.WarehousesAsync();
        public Task<IReadOnlyList<Supplier>> SuppliersAsync() => _seed.SuppliersAsync();
        public Task<IReadOnlyList<StockReservation>> ReservationsAsync() => Task.FromResult<IReadOnlyList<StockReservation>>(Reservations);
        public Task<IReadOnlyList<PurchaseOrder>> PurchaseOrdersAsync() => Task.FromResult<IReadOnlyList<PurchaseOrder>>(Orders);
        public Task ResetAsync() { Resets++; return Task.CompletedTask; }
    }

    [Fact]
    public async Task The_warehouse_api_is_public_and_shows_the_seed_stock_with_nothing_reserved_or_ordered()
    {
        using var factory = new WebApplicationFactory<Program>();

        var board = await NewClient(factory, withKey: false).GetFromJsonAsync<WarehouseBoardResult>("/api/warehouse");

        Assert.Equal(23, board!.Summary.Items);
        Assert.Equal(4, board.Summary.Equipment);
        Assert.Equal(0, board.Summary.OpenOrders);
        Assert.Empty(board.Changes);
        var coupling = board.Items.Single(i => i.Sku == "CPL-FLEX-300");
        Assert.Equal(1, coupling.Available);
        Assert.Equal("low", coupling.Level);              // 1 available, reorder level 2
        Assert.Equal("out", board.Items.Single(i => i.Sku == "VLV-GATE-300").Level);
        var heavy = board.Items.Single(i => i.Sku == "EQP-EXC-14T");
        Assert.Equal(0, heavy.Available);                 // still in maintenance at the time of the alarm
        Assert.Equal("out", heavy.Level);
        Assert.Equal("ok", board.Items.Single(i => i.Sku == "GSK-300").Level);
    }

    [Fact]
    public async Task Reservations_and_orders_show_up_as_reserved_stock_open_orders_and_changes()
    {
        var seed = new FileDemoData(LeakOps.Seeder.SeedDataLoader.FindDataDirectory());
        var stock = (await seed.InventoryAsync()).Select(i => i.Id == "CLP-CI-300" ? i with { QuantityReserved = 1 } : i).ToList();
        var coupling = stock.Single(i => i.Id == "CPL-FLEX-300");
        var supplier = (await seed.SuppliersAsync()).Single(s => s.Id == coupling.SupplierId);
        var order = Procurement.BuildOrder(coupling, supplier, "INC-1", 4, express: true, "Short for this repair", AlarmTime.AddMinutes(16));
        var data = new FakeDemoData
        {
            Stock = stock,
            Reservations = [new StockReservation("INC-1-CLP-CI-300", "INC-1", "CLP-CI-300", "Clamp", "part", 1, "WH-01", AlarmTime.AddMinutes(15))],
            Orders = [order],
        };
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IDemoData>(data)));

        var board = await factory.CreateClient().GetFromJsonAsync<WarehouseBoardResult>("/api/warehouse");

        var clamp = board!.Items.Single(i => i.Sku == "CLP-CI-300");
        Assert.Equal(1, clamp.Reserved);
        Assert.Equal(1, clamp.Available);                 // 2 on hand, 1 reserved
        Assert.Equal("INC-1", Assert.Single(clamp.Reservations).IncidentId);
        var ordered = Assert.Single(board.Items.Single(i => i.Sku == "CPL-FLEX-300").Orders);
        Assert.True(ordered.Express);
        Assert.Equal(1, board.Summary.OpenOrders);
        Assert.Equal(order.Status == "pending_approval" ? 1 : 0, board.Summary.PendingApproval);
        Assert.Equal(["order", "reservation"], board.Changes.Select(c => c.Kind));   // newest first
    }

    [Fact]
    public async Task The_warehouse_page_is_served()
    {
        using var factory = new WebApplicationFactory<Program>();

        var response = await factory.CreateClient().GetAsync("/warehouse");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"grid-wrap\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_technicians_api_is_public_and_shows_the_roster_at_the_time_of_the_alarm()
    {
        using var factory = new WebApplicationFactory<Program>();

        var board = await NewClient(factory, withKey: false).GetFromJsonAsync<TechnicianBoardResult>("/api/technicians");

        Assert.Equal(8, board!.Summary.Total);
        Assert.Equal(AlarmTime, board.AtUtc);
        var states = board.Technicians.ToDictionary(t => t.Id, t => t.State);
        Assert.Equal("available", states["T-02"]);
        Assert.Equal("busy", states["T-03"]);       // a job from the roster
        Assert.Equal("off_shift", states["T-04"]);  // day shift, starts at 05:00
        Assert.Equal(0, board.Summary.Assigned);
        Assert.All(board.Technicians, t => Assert.Null(t.Assignment));
        Assert.Equal("/technicians/T-02.jpg", board.Technicians.Single(t => t.Id == "T-02").Photo);
    }

    [Fact]
    public async Task The_technicians_api_can_show_another_moment()
    {
        using var factory = new WebApplicationFactory<Program>();

        var board = await factory.CreateClient().GetFromJsonAsync<TechnicianBoardResult>("/api/technicians?at=2026-11-14T08:00:00Z");

        Assert.Equal("available", board!.Technicians.Single(t => t.Id == "T-04").State);
        Assert.Equal("off_shift", board.Technicians.Single(t => t.Id == "T-01").State);
    }

    [Fact]
    public async Task A_scheduled_work_order_makes_its_technician_assigned_with_the_job_details()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<IDemoData>(new FakeDemoData(OrderFor("T-02")))));

        var board = await factory.CreateClient().GetFromJsonAsync<TechnicianBoardResult>("/api/technicians");

        var samira = board!.Technicians.Single(t => t.Id == "T-02");
        Assert.Equal("assigned", samira.State);
        Assert.Equal("WO-INC-ALM-2026-1114-0001", samira.Assignment!.WorkOrderId);
        Assert.Equal("Dorpsweg", samira.Assignment.Street);
        Assert.Equal(1, board.Summary.Assigned);
        Assert.Equal(4 - 1, board.Summary.Available);   // T-01, T-05, T-07 are still free
    }

    [Fact]
    public async Task The_technicians_page_is_served()
    {
        using var factory = new WebApplicationFactory<Program>();

        var response = await factory.CreateClient().GetAsync("/technicians");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"planner\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reset_demo_needs_a_key_and_clears_the_page_and_the_demo_data()
    {
        var data = new FakeDemoData();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("OPERATOR_KEY", "op-key");
            b.ConfigureServices(s => s.AddSingleton<IDemoData>(data));
        });
        var writer = NewClient(factory);
        await writer.PostAsJsonAsync("/api/incidents", Sample());

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsync("/api/admin/reset-demo", null)).StatusCode);
        Assert.Equal(0, data.Resets);
        Assert.Single((await writer.GetFromJsonAsync<List<WaterstoringIncident>>("/api/incidents"))!);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/reset-demo");
        request.Headers.Add("X-Operator-Key", "op-key");
        Assert.Equal(HttpStatusCode.NoContent, (await factory.CreateClient().SendAsync(request)).StatusCode);
        Assert.Equal(1, data.Resets);
        Assert.Empty((await writer.GetFromJsonAsync<List<WaterstoringIncident>>("/api/incidents"))!);
    }
}
