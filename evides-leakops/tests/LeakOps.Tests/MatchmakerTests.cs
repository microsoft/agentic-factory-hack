using LeakOps.Contracts;
using LeakOps.Contracts.Logic;
using LeakOps.Seeder;
using Xunit;

namespace LeakOps.Tests;

public class MatchmakerTests
{
    private static readonly SeedData Data = SeedDataLoader.Load(SeedDataLoader.FindDataDirectory());
    private static readonly Scenario Burst = Data.Scenarios.Single(s => s.Id == "scn-burst-cast-iron");

    private static DiagnosticReport BurstReport()
    {
        var segment = Data.PipeSegments.Single(s => s.Id == "SEG-0412");
        var valves = Data.Valves.Where(v => v.SegmentId == segment.Id)
            .Select(v => new IsolationValveInfo(v.Id, v.Type, v.Status, v.TurnsToClose, v.LastOperatedUtc, v.Location, v.ChainageM)).ToList();
        var triage = new TriageVerdict(Burst.Alarm.AlarmId, segment.DistrictId, Burst.Alarm.SensorId, Burst.Alarm.TriggeredAtUtc,
            "genuine_leak", 0.97, "Leak.", [], segment.Id, "diagnose");
        var judgement = new DiagnosisJudgement(segment.Id, segment.LengthM / 3, 30, "burst", valves.Select(v => v.Id).ToList(), "Burst.",
            [new EvidenceItem("estimate-leak-position", "PS-101 fell 2.01 bar.", "leak")]);
        return DiagnosticRules.Build(judgement, segment, valves, Procedure, triage, [new SegmentDrop(segment.Id, 2.0)]);
    }

    private static RepairProcedure Procedure => Data.RepairProcedures.Single(p => p.Id == "RP-CI-300-400-BURST");

    private static List<TechnicianStatus> Roster(DiagnosticReport report, DateTimeOffset? at = null) =>
        Data.Technicians.Select(t => WorkforceAvailability.Evaluate(t, at ?? report.TriggeredAtUtc, Procedure.RequiredCertifications, report.Location)).ToList();

    private static WorkOrder OrderFor(string technicianId, string incidentId, DateTimeOffset start, DateTimeOffset end) =>
        new($"WO-{incidentId}", incidentId, "SEG-0415", "Randweg", 100, new GeoPoint(51.8, 4.3), "burst", "S2", "x", technicianId, "Tech", "RP", [], [], [], start,
            10, 0, 0, 6, start.AddMinutes(10), end, "scheduled", start, null, start);

    [Fact]
    public void A_job_that_starts_soon_makes_a_technician_busy_for_a_repair_that_takes_hours()
    {
        var report = BurstReport();
        var soon = OrderFor("T-02", "INC-OTHER", report.TriggeredAtUtc.AddMinutes(10), report.TriggeredAtUtc.AddHours(6));
        var t02 = WorkforceAvailability.WithAssignments(Data.Technicians.Single(t => t.Id == "T-02"), [soon]);

        var momentOnly = WorkforceAvailability.Evaluate(t02, report.TriggeredAtUtc, Procedure.RequiredCertifications, report.Location);
        var sixHours = WorkforceAvailability.Evaluate(t02, report.TriggeredAtUtc, Procedure.RequiredCertifications, report.Location, TimeSpan.FromHours(6));
        var finishesBefore = WorkforceAvailability.Evaluate(t02, report.TriggeredAtUtc, Procedure.RequiredCertifications, report.Location, TimeSpan.FromMinutes(5));

        Assert.True(momentOnly.Available);       // free at the very instant of the alarm...
        Assert.False(sixHours.Available);        // ...but not for the six hours the repair takes
        Assert.True(finishesBefore.Available);   // a five minute job still fits before the other one starts
        Assert.Contains("work order WO-INC-OTHER", sixHours.BusyWith);
    }

    [Fact]
    public void A_technician_who_is_committed_to_another_incident_counts_as_busy_and_the_next_one_is_chosen()
    {
        var report = BurstReport();
        var other = OrderFor("T-02", "INC-OTHER", report.TriggeredAtUtc.AddMinutes(-30), report.TriggeredAtUtc.AddHours(5));
        var roster = Data.Technicians
            .Select(t => WorkforceAvailability.Evaluate(WorkforceAvailability.WithAssignments(t, [other]), report.TriggeredAtUtc, Procedure.RequiredCertifications, report.Location)).ToList();

        var match = MatchmakerRules.Build(report, Procedure, roster, new MatchJudgement("T-02", "T-05", "Samira El Idrissi goes."));

        Assert.Equal("T-05", match.Primary!.Id);
        Assert.Contains("busy with work order WO-INC-OTHER", match.Candidates.Single(c => c.TechnicianId == "T-02").Reason);
        Assert.Contains(match.Warnings, w => w.Contains("The model proposed T-02"));
    }

    [Fact]
    public void The_incident_being_planned_does_not_make_its_own_technician_look_busy()
    {
        var report = BurstReport();
        var own = OrderFor("T-02", "INC-2026-1114-0001", report.TriggeredAtUtc.AddMinutes(10), report.TriggeredAtUtc.AddHours(6));
        var roster = Data.Technicians
            .Select(t => WorkforceAvailability.Evaluate(WorkforceAvailability.WithAssignments(t, [own], "INC-2026-1114-0001"), report.TriggeredAtUtc, Procedure.RequiredCertifications, report.Location)).ToList();

        Assert.Equal("T-02", MatchmakerRules.Build(report, Procedure, roster, new MatchJudgement("T-02", "T-05", "Samira El Idrissi goes.")).Primary!.Id);
    }

    [Fact]
    public void A_new_work_order_may_not_overlap_with_another_incidents_work_order_of_the_same_technician()
    {
        var start = new DateTimeOffset(2026, 11, 14, 3, 20, 0, TimeSpan.Zero);
        var existing = OrderFor("T-02", "INC-OTHER", start, start.AddHours(7));

        Assert.NotNull(WorkforceAvailability.FindConflict([existing], "T-02", "INC-NEW", start.AddHours(1), start.AddHours(8)));      // overlaps
        Assert.Null(WorkforceAvailability.FindConflict([existing], "T-02", "INC-NEW", start.AddHours(7), start.AddHours(12)));        // starts when the other ends
        Assert.Null(WorkforceAvailability.FindConflict([existing], "T-05", "INC-NEW", start, start.AddHours(7)));                     // somebody else
        Assert.Null(WorkforceAvailability.FindConflict([existing], "T-02", "INC-OTHER", start, start.AddHours(7)));                   // planning the same incident again
    }

    [Fact]
    public void Burst_goes_to_T02_with_T05_as_backup_and_explains_everybody_else()
    {
        var report = BurstReport();
        var match = MatchmakerRules.Build(report, Procedure, Roster(report), new MatchJudgement("T-02", "T-05", "Samira El Idrissi (T-02) goes."));

        Assert.Equal("T-02", match.Primary!.Id);
        Assert.Equal("T-05", match.Backup!.Id);
        Assert.Equal("INC-" + report.AlarmId[4..], match.IncidentId);
        Assert.False(match.NeedsReview);
        Assert.DoesNotContain(match.Warnings, w => w.Contains("model"));

        var byId = match.Candidates.ToDictionary(c => c.TechnicianId);
        Assert.Contains("missing", byId["T-01"].Reason);                  // nearest on shift, but not certified for cast iron
        Assert.Contains("busy", byId["T-03"].Reason);
        Assert.Contains("not on shift", byId["T-04"].Reason);
        Assert.Equal(["T-02", "T-05"], match.Candidates.Where(c => c.Eligible).Select(c => c.TechnicianId));
    }

    [Theory]
    [InlineData("T-01")]    // nearest, but misses CI-REPAIR-L3 and EXCAVATOR-OP
    [InlineData("T-03")]    // certified, but busy at the alarm time
    [InlineData("T-04")]    // certified, but off shift
    [InlineData("T-99")]    // unknown
    [InlineData("")]
    public void A_model_choice_that_is_not_eligible_is_corrected(string proposed)
    {
        var report = BurstReport();
        var match = MatchmakerRules.Build(report, Procedure, Roster(report), new MatchJudgement(proposed, null, $"{proposed} goes."));

        Assert.Equal("T-02", match.Primary!.Id);
        Assert.Contains(match.Warnings, w => w.Contains("The model proposed"));
        Assert.DoesNotContain(proposed.Length > 0 ? proposed + " goes" : "~", match.Summary);   // the model's text about the wrong person is not published
        Assert.Contains("Samira", match.Summary);
    }

    [Fact]
    public void A_slower_but_eligible_choice_is_corrected_to_the_fastest()
    {
        var report = BurstReport();
        var match = MatchmakerRules.Build(report, Procedure, Roster(report), new MatchJudgement("T-05", "T-02", "Mark Visser (T-05) goes."));

        Assert.Equal("T-02", match.Primary!.Id);
        Assert.Equal("T-05", match.Backup!.Id);
        Assert.Contains(match.Warnings, w => w.Contains("arrives sooner"));
    }

    [Fact]
    public void When_nobody_is_eligible_nobody_is_dispatched_and_the_first_qualified_person_is_named()
    {
        var report = BurstReport();
        // Between the day and night shifts (13:30 UTC) nobody is on duty at all.
        var at = DateTimeOffset.Parse("2026-11-14T13:30:00Z");
        var match = MatchmakerRules.Build(report, Procedure, Roster(report, at), new MatchJudgement("T-04", null, "T-04 goes."));

        Assert.Null(match.Primary);
        Assert.Null(match.Backup);
        Assert.True(match.NeedsReview);
        Assert.Contains(match.ReviewReasons, r => r.Contains("first qualified technician"));
        Assert.StartsWith("Nobody can be dispatched", match.Summary);
    }

    [Fact]
    public void Without_a_repair_procedure_the_required_certifications_are_unknown_so_a_person_decides()
    {
        var report = BurstReport();
        var match = MatchmakerRules.Build(report, null, Roster(report), new MatchJudgement("T-02", "T-05", "T-02 goes."));

        Assert.Null(match.Primary);
        Assert.True(match.NeedsReview);
        Assert.Contains(match.ReviewReasons, r => r.Contains("no repair procedure"));
    }

    [Fact]
    public void Dispatcher_is_warned_when_the_shift_ends_before_the_repair_does()
    {
        var report = BurstReport();
        var match = MatchmakerRules.Build(report, Procedure, Roster(report), new MatchJudgement("T-02", "T-05", "T-02 goes."));

        // Alarm at 03:10Z, shift ends 05:00Z, the repair takes hours: a hand-over is needed.
        Assert.True(Procedure.EstimatedRepairHours > 2);
        Assert.Contains(match.Warnings, w => w.Contains("shift ends") && w.Contains("hand"));
    }

    [Fact]
    public void A_summary_that_names_the_chosen_technician_is_kept()
    {
        var report = BurstReport();
        const string text = "Samira El Idrissi is nearest and certified; Pieter de Vries is closer but not certified.";
        var match = MatchmakerRules.Build(report, Procedure, Roster(report), new MatchJudgement("T-02", "T-05", text));

        Assert.Equal(text, match.Summary);
        Assert.DoesNotContain(match.Warnings, w => w.Contains("summary was written by the rules"));
    }
}
