using LeakOps.Contracts;
using LeakOps.Contracts.Logic;
using LeakOps.Seeder;
using Xunit;

namespace LeakOps.Tests;

public class DiagnosticLogicTests
{
    private static readonly SeedData Data = SeedDataLoader.Load(SeedDataLoader.FindDataDirectory());
    private static readonly Scenario Burst = Data.Scenarios.Single(s => s.Id == "scn-burst-cast-iron");
    private static readonly PipeSegment Segment = Data.PipeSegments.Single(s => s.Id == "SEG-0412");

    private static List<SensorDrop> DropsOnSegment(string segmentId, params string[] sensorIds)
    {
        var until = Burst.Alarm.TriggeredAtUtc;
        return Data.Sensors.Where(s => s.SegmentId == segmentId && sensorIds.Contains(s.Id)).Select(sensor =>
        {
            var readings = Data.SensorReadings.Where(r => r.SensorId == sensor.Id && r.TimestampUtc >= until.AddMinutes(-30) && r.TimestampUtc <= until)
                .OrderBy(r => r.TimestampUtc).ToList();
            return new SensorDrop(sensor.Id, sensor.ChainageM, readings[0].Value, readings[^1].Value);
        }).ToList();
    }

    [Fact]
    public void Leak_position_from_the_scenario_readings_lands_close_to_the_simulated_break()
    {
        var estimate = LeakLocator.Estimate(DropsOnSegment("SEG-0412", "PS-101", "PS-103"), Segment.LengthM)!;
        var truth = Burst.GroundTruth.ChainageM!.Value;

        Assert.InRange(Math.Abs(estimate.ChainageM - truth), 0, 30);
        Assert.True(Math.Abs(estimate.ChainageM - truth) <= estimate.UncertaintyM, "the stated uncertainty must cover the real position");
        Assert.True(estimate.ChainageM < (40 + 400) / 2.0, "PS-101 dropped more, so the break is nearer PS-101");
    }

    [Fact]
    public void Leak_position_degrades_honestly_with_fewer_sensors()
    {
        var onlyOne = LeakLocator.Estimate([new SensorDrop("PS-101", 40, 4.86, 2.85), new SensorDrop("PS-103", 400, 4.81, 4.70)], 438)!;
        Assert.Equal(40, onlyOne.ChainageM);
        Assert.Equal(219, onlyOne.UncertaintyM);   // half the segment: the position along it is unknown
        Assert.Contains("PS-101", onlyOne.Method);

        Assert.Null(LeakLocator.Estimate([new SensorDrop("PS-101", 40, 4.86, 4.80)], 438));   // no real drop, no estimate
        Assert.Null(LeakLocator.Estimate([], 438));
    }

    [Theory]
    [InlineData("burst", 1840, true, 300, "S1")]    // care home and many customers
    [InlineData("burst", 1000, false, 150, "S1")]   // 1000 customers
    [InlineData("burst", 20, false, 400, "S1")]     // trunk main
    [InlineData("burst", 420, false, 200, "S2")]
    [InlineData("burst", 20, false, 250, "S2")]     // large pipe, few customers
    [InlineData("burst", 22, false, 160, "S3")]
    [InlineData("leak", 1840, false, 300, "S2")]
    [InlineData("leak", 20, true, 150, "S2")]       // critical customer
    [InlineData("leak", 420, false, 200, "S3")]
    [InlineData("leak", 22, false, 160, "S4")]
    public void Severity_follows_the_fixed_rubric(string breach, int customers, bool critical, int diameter, string expected)
    {
        var assessment = SeverityRubric.Classify(breach, customers, critical ? ["Care home"] : [], diameter);

        Assert.Equal(expected, assessment.Severity);
        Assert.Contains(assessment.Reasons, r => r.Contains($"DN{diameter}"));
    }

    private static IReadOnlyList<IsolationValveInfo> ValvesOf(PipeSegment segment, double chainage) =>
        Data.Valves.Where(v => v.SegmentId == segment.Id)
            .Select(v => new IsolationValveInfo(v.Id, v.Type, v.Status, v.TurnsToClose, v.LastOperatedUtc, v.Location, Math.Abs(v.ChainageM - chainage)))
            .ToList();

    private static TriageVerdict Triage() => new(
        Burst.Alarm.AlarmId, Burst.Alarm.DistrictId, Burst.Alarm.SensorId, Burst.Alarm.TriggeredAtUtc,
        "genuine_leak", 0.97, "Leak.", [], "SEG-0412", "diagnose");

    private static readonly EvidenceItem Evidence = new("estimate-leak-position", "PS-101 fell 2.01 bar and PS-103 fell 1.19 bar.", "leak");
    private static readonly RepairProcedure Procedure = Data.RepairProcedures.Single(p => p.Id == "RP-CI-300-400-BURST");

    // What the rank-segments-by-pressure-drop tool reports for the burst scenario (30 minutes before the alarm).
    private static readonly SegmentDrop[] Drops = [new("SEG-0412", 2.01), new("SEG-0415", 0.32)];

    [Fact]
    public void The_position_comes_from_the_locator_that_code_ran_not_from_the_model()
    {
        // The model claims a precise position at the far end of the segment; the locator measured 174 m (±54 m).
        var judgement = new DiagnosisJudgement("SEG-0412", 400, 2, "burst", ["V-0412-A", "V-0412-B"], "Burst.", [Evidence]);

        var report = DiagnosticRules.Build(judgement, Segment, ValvesOf(Segment, 174), Procedure, Triage(), Drops, new DiagnosticFacts(174, 54, 80));

        Assert.Equal((174, 54), (report.ChainageM, report.UncertaintyM));
        Assert.Equal(Geo.PointAt(Segment, 174), report.Location);
        Assert.Contains(report.Warnings, w => w.Contains("locator's 174 m"));
    }

    [Fact]
    public void A_model_position_that_matches_the_locator_is_accepted_without_a_warning()
    {
        var judgement = new DiagnosisJudgement("SEG-0412", 174.2, 54, "burst", ["V-0412-A", "V-0412-B"], "Burst.", [Evidence]);

        var report = DiagnosticRules.Build(judgement, Segment, ValvesOf(Segment, 174), Procedure, Triage(), Drops, new DiagnosticFacts(174, 54, 80));

        Assert.DoesNotContain(report.Warnings, w => w.Contains("locator"));
    }

    [Fact]
    public void When_no_sensor_fell_enough_to_locate_the_breach_the_position_is_unknown_and_a_person_checks()
    {
        var judgement = new DiagnosisJudgement("SEG-0412", 100, 5, "burst", ["V-0412-A", "V-0412-B"], "Burst.", [Evidence]);

        var report = DiagnosticRules.Build(judgement, Segment, ValvesOf(Segment, 100), Procedure, Triage(), Drops, new DiagnosticFacts(null, null, 80));

        Assert.Equal(Segment.LengthM / 2.0, report.UncertaintyM, 0);
        Assert.True(report.NeedsReview);
        Assert.Contains(report.Warnings, w => w.Contains("position along the segment is unknown"));
    }

    [Theory]
    [InlineData("leak", 80.0, 2.01, "burst")]     // flow far above normal and a 2 bar drop: a burst, whatever the model says
    [InlineData("leak", 5.0, 1.2, "burst")]       // the pressure rule alone is enough
    [InlineData("burst", 5.0, 0.4, "burst")]      // the model is more cautious than the data: the safer reading stands
    [InlineData("leak", 5.0, 0.4, "leak")]        // both agree it is small
    public void The_burst_rule_is_applied_to_the_measured_values_and_the_safer_reading_wins(string model, double excessM3h, double dropBar, string expected)
    {
        var judgement = new DiagnosisJudgement("SEG-0412", 174, 54, model, ["V-0412-A", "V-0412-B"], "x", [Evidence]);

        var report = DiagnosticRules.Build(judgement, Segment, ValvesOf(Segment, 174), Procedure, Triage(), [new("SEG-0412", dropBar)], new DiagnosticFacts(174, 54, excessM3h));

        Assert.Equal(expected, report.BreachType);
    }

    [Fact]
    public void A_good_judgement_becomes_a_report_with_facts_from_gis_and_rules()
    {
        var judgement = new DiagnosisJudgement("SEG-0412", 174, 54, "burst", ["V-0412-A", "V-0412-B"], "Burst on Dorpsweg.", [Evidence]);

        var report = DiagnosticRules.Build(judgement, Segment, ValvesOf(Segment, 174), Procedure, Triage(), Drops);

        Assert.Equal(("cast_iron", 300, 1962, "arterial"), (report.Material, report.DiameterMm, report.InstallYear, report.RoadType));
        Assert.Equal("S1", report.Severity);                       // burst, care home, 1840 customers
        Assert.Equal(1840, report.CustomersAffected);
        Assert.Equal(Geo.PointAt(Segment, 174), report.Location);
        Assert.Equal("RP-CI-300-400-BURST", report.RepairProcedureId);
        Assert.Equal(["V-0412-A", "V-0412-B"], report.IsolationPlan.Select(s => s.ValveId));
        Assert.Equal(TimeSpan.Zero, report.TriggeredAtUtc.Offset);
        Assert.False(report.NeedsReview);

        // The only warning is the real one in the data: V-0412-B was last operated in 2023.
        var warning = Assert.Single(report.Warnings);
        Assert.Contains("V-0412-B", warning);
        Assert.Contains("stiff", warning);
    }

    [Fact]
    public void Mistakes_in_the_model_answer_are_corrected_and_reported()
    {
        var judgement = new DiagnosisJudgement("SEG-0412", 9999, double.NaN, "explosion", ["V-9999", "V-0412-B"], "", []);

        var report = DiagnosticRules.Build(judgement, Segment, ValvesOf(Segment, 438), null, Triage(), Drops);

        Assert.Equal(Segment.LengthM, report.ChainageM);            // clamped into the segment
        Assert.Equal("burst", report.BreachType);                   // unknown type: assume the worse case
        Assert.Equal(["V-0412-B", "V-0412-A"], report.IsolationPlan.Select(s => s.ValveId));   // unknown valve dropped, missing one added, model order kept
        Assert.Contains(report.Warnings, w => w.Contains("outside the segment"));
        Assert.Contains(report.Warnings, w => w.Contains("not valid"));
        Assert.Contains(report.Warnings, w => w.Contains("V-9999"));
        Assert.Contains(report.Warnings, w => w.Contains("missing isolation valve"));
        Assert.Contains(report.Warnings, w => w.Contains("no summary"));
        Assert.Contains(report.Warnings, w => w.Contains("no evidence"));
        Assert.Null(report.RepairProcedureId);
        Assert.True(report.NeedsReview);
        Assert.Contains(report.ReviewReasons, r => r.Contains("No standard repair procedure"));
        Assert.Contains(report.ReviewReasons, r => r.Contains("only known to within"));   // NaN uncertainty means "unknown"
    }

    [Fact]
    public void A_diagnosis_for_a_segment_where_the_pressure_barely_moved_needs_review()
    {
        // Triage pointed at the neighbouring PVC pipe, where the pressure fell only 0.32 bar against 2.01 bar on SEG-0412.
        var other = Data.PipeSegments.Single(s => s.Id == "SEG-0415");
        var triage = Triage() with { SuspectedSegmentId = "SEG-0415" };
        var judgement = new DiagnosisJudgement("SEG-0415", 100, 40, "burst", ["V-0415-A"], "Burst on Havenlaan.", [Evidence]);
        var procedure = Data.RepairProcedures.Single(p => p.Id == "RP-PVC-100-299-BURST");

        var report = DiagnosticRules.Build(judgement, other, ValvesOf(other, 100), procedure, triage, Drops);

        Assert.True(report.NeedsReview);
        var reason = Assert.Single(report.ReviewReasons);
        Assert.Contains("SEG-0412", reason);
        Assert.Contains("2.01", reason);
    }

    [Fact]
    public void Switching_to_the_segment_with_the_strongest_drop_is_allowed_but_noted()
    {
        // Triage suspected SEG-0415, the agent verified against the data and diagnosed SEG-0412 instead: fine, but visible.
        var triage = Triage() with { SuspectedSegmentId = "SEG-0415" };
        var judgement = new DiagnosisJudgement("SEG-0412", 174, 54, "burst", ["V-0412-A", "V-0412-B"], "Burst on Dorpsweg.", [Evidence]);

        var report = DiagnosticRules.Build(judgement, Segment, ValvesOf(Segment, 174), Procedure, triage, Drops);

        Assert.False(report.NeedsReview);
        Assert.Contains(report.Warnings, w => w.Contains("not SEG-0415 as Triage suspected"));
    }

    [Fact]
    public void A_valve_that_is_not_open_is_flagged_as_an_isolation_risk()
    {
        var seized = ValvesOf(Segment, 160).Select(v => v.Id == "V-0412-A" ? v with { Status = "seized" } : v).ToList();
        var judgement = new DiagnosisJudgement("SEG-0412", 160, 54, "burst", ["V-0412-A", "V-0412-B"], "Burst.", [Evidence]);

        var report = DiagnosticRules.Build(judgement, Segment, seized, Procedure, Triage(), Drops);

        Assert.Contains(report.Warnings, w => w.Contains("V-0412-A") && w.Contains("seized"));
    }
}
