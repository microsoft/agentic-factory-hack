using System.Net;
using System.Net.Http.Json;
using LeakOps.Contracts;
using LeakOps.Contracts.Logic;
using LeakOps.Seeder;
using LeakOps.Waterstoring;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LeakOps.Tests;

public class GisBoardTests
{
    private static readonly SeedData Data = SeedDataLoader.Load(SeedDataLoader.FindDataDirectory());
    private static readonly Scenario Burst = Data.Scenarios.Single(s => s.Id == "scn-burst-cast-iron");
    private static readonly DateTimeOffset AlarmTime = Burst.Alarm.TriggeredAtUtc;

    private static DiagnosticReport BurstReport(string alarmId = "ALM-2026-1114-0001")
    {
        var segment = Data.PipeSegments.Single(s => s.Id == "SEG-0412");
        var valves = Data.Valves.Where(v => v.SegmentId == segment.Id)
            .Select(v => new IsolationValveInfo(v.Id, v.Type, v.Status, v.TurnsToClose, v.LastOperatedUtc, v.Location, v.ChainageM)).ToList();
        var triage = new TriageVerdict(alarmId, segment.DistrictId, Burst.Alarm.SensorId, AlarmTime, "genuine_leak", 0.97, "Leak.", [], segment.Id, "diagnose");
        var judgement = new DiagnosisJudgement(segment.Id, 174, 54, "burst", ["V-0412-A", "V-0412-B"],
            "Close V-0412-A first and then V-0412-B; V-0412-B is old, so keep backup valve V-0415-A ready if isolation does not hold.",
            [new EvidenceItem("get-pipe-segment", "SEG-0412 joins SEG-0399 at V-0399-A.", "neutral")]);
        var procedure = Data.RepairProcedures.Single(p => p.Id == "RP-CI-300-400-BURST");
        return DiagnosticRules.Build(judgement, segment, valves, procedure, triage, [new SegmentDrop(segment.Id, 2.0)]);
    }

    private static GisBoardResult Board(params GisDiagnosis[] diagnoses) =>
        GisBoard.Build(Data.PipeSegments, Data.Valves, Data.Sensors, diagnoses, AlarmTime);

    [Fact]
    public void Without_a_diagnosis_the_map_shows_only_the_simulated_network()
    {
        var board = Board();

        Assert.True(board.Simulated);
        Assert.Null(board.Incident);
        Assert.Equal(Data.PipeSegments.Count, board.Network.Pipes.Count);
        Assert.Equal(Data.Valves.Count, board.Network.Valves.Count);
        Assert.Equal(Data.Sensors.Count, board.Network.Sensors.Count);
        // V-0412-B was last operated in 2023: more than two years before the alarm, the same rule the Diagnostic agent uses.
        Assert.True(board.Network.Valves.Single(v => v.Id == "V-0412-B").MayBeStiff);
        Assert.False(board.Network.Valves.Single(v => v.Id == "V-0412-A").MayBeStiff);
    }

    [Fact]
    public void A_diagnosis_puts_the_leak_on_the_segment_with_the_valves_in_closing_order_and_the_backup_valve()
    {
        var report = BurstReport();
        var incident = Board(new GisDiagnosis(AlarmTime.AddMinutes(2), report)).Incident!;
        var segment = Data.PipeSegments.Single(s => s.Id == "SEG-0412");

        Assert.Equal("INC-2026-1114-0001", incident.IncidentId);
        Assert.Equal("Dorpsweg", incident.Street);

        // The chainage turned into a coordinate lies on the pipe, at the same point the get-point-on-segment tool gives.
        Assert.Equal(Geo.PointAt(segment, 174), incident.Location);
        var viaLeak = Geo.Metres(segment.Start, incident.Location) + Geo.Metres(incident.Location, segment.End);
        Assert.InRange(viaLeak - Geo.Metres(segment.Start, segment.End), 0, 0.5);
        Assert.Equal(54, incident.UncertaintyM);
        Assert.Equal([Geo.PointAt(segment, 120), Geo.PointAt(segment, 228)], incident.UncertainStretch);

        Assert.Equal(["V-0412-A", "V-0412-B"], incident.IsolationPlan.Select(s => s.ValveId));
        Assert.Equal([1, 2], incident.IsolationPlan.Select(s => s.Order));
        Assert.True(incident.IsolationPlan[1].MayBeStiff);
        // Only the valve named as backup, not the plan's own valves or another valve the evidence mentions.
        Assert.Equal(["V-0415-A"], incident.BackupValves.Select(b => b.ValveId));

        Assert.Equal(1840, incident.CustomersAffected);
        Assert.Contains(incident.CriticalCustomers, c => c.Contains("Zorgcentrum De Hoven"));
        Assert.NotEmpty(incident.PostalCodes);
        Assert.NotEmpty(incident.ServedStreets);
    }

    [Fact]
    public void The_latest_diagnosis_is_the_incident_on_the_map()
    {
        var older = new GisDiagnosis(AlarmTime, BurstReport("ALM-2026-1114-0001"));
        var newer = new GisDiagnosis(AlarmTime.AddMinutes(5), BurstReport("ALM-2026-1114-0002"));

        Assert.Equal("ALM-2026-1114-0002", Board(newer, older).Incident!.AlarmId);
    }

    [Fact]
    public async Task The_gis_page_is_public_takes_diagnoses_only_with_a_key_and_is_emptied_by_the_demo_reset()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var page = await client.GetStringAsync("/gis");
        Assert.Contains("leaflet@1.9.4", page);
        Assert.Contains("integrity=", page);
        Assert.Null((await client.GetFromJsonAsync<GisBoardResult>("/api/gis"))!.Incident);

        var report = BurstReport();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/gis/diagnosis", report)).StatusCode);

        client.DefaultRequestHeaders.Add("X-Api-Key", "dev-waterstoring-key");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/gis/diagnosis", report with { AlarmId = "nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/gis/diagnosis", report)).StatusCode);

        var json = await client.GetStringAsync("/api/gis");
        Assert.DoesNotContain("groundTruth", json, StringComparison.OrdinalIgnoreCase);
        var board = await client.GetFromJsonAsync<GisBoardResult>("/api/gis");
        Assert.Equal("SEG-0412", board!.Incident!.SegmentId);
        Assert.Equal(["V-0415-A"], board.Incident.BackupValves.Select(b => b.ValveId));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/admin/reset-demo", null)).StatusCode);
        Assert.Null((await client.GetFromJsonAsync<GisBoardResult>("/api/gis"))!.Incident);
    }
}
