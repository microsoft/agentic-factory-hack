using LeakOps.Contracts;
using LeakOps.Contracts.Logic;
using LeakOps.Seeder;
using Xunit;

namespace LeakOps.Tests;

public class WorkflowRunnerTests
{
    private static readonly SeedData Data = SeedDataLoader.Load(SeedDataLoader.FindDataDirectory());
    private static readonly Scenario Burst = Data.Scenarios.Single(s => s.Id == "scn-burst-cast-iron");
    private static readonly RepairProcedure Procedure = Data.RepairProcedures.Single(p => p.Id == "RP-CI-300-400-BURST");
    private static readonly AgentTrace Trace = new("Test", "model", 1, null, null, []);

    private static TriageVerdict Triage(string verdict, double confidence = 0.97) => new(Burst.Alarm.AlarmId, "D-01", Burst.Alarm.SensorId, Burst.Alarm.TriggeredAtUtc,
        verdict, confidence, verdict == "false_alarm" ? "Planned works explain the dip." : "Pressure collapsed.", [], verdict == "genuine_leak" ? "SEG-0412" : null,
        verdict == "genuine_leak" ? "diagnose" : verdict == "false_alarm" ? "close_alarm" : "manual_review");

    private static DiagnosticReport Report()
    {
        var segment = Data.PipeSegments.Single(s => s.Id == "SEG-0412");
        var valves = Data.Valves.Where(v => v.SegmentId == segment.Id)
            .Select(v => new IsolationValveInfo(v.Id, v.Type, v.Status, v.TurnsToClose, v.LastOperatedUtc, v.Location, v.ChainageM)).ToList();
        var judgement = new DiagnosisJudgement(segment.Id, segment.LengthM / 3, 30, "burst", valves.Select(v => v.Id).ToList(), "Burst.", []);
        return DiagnosticRules.Build(judgement, segment, valves, Procedure, Triage("genuine_leak"), [new SegmentDrop(segment.Id, 2.0)]);
    }

    private static CommunicationsResult Communications(DiagnosticReport report)
    {
        var notice = new PublicNotice("t", "m", "a", "t", "m", "a");
        var incident = new WaterstoringIncident(IncidentIds.For(report.AlarmId), "t", "m", null, null, "reported", report.Place, report.ServedStreets, report.PostalCodes,
            report.Location, null, report.CustomersAffected, null, null, null, Burst.Alarm.TriggeredAtUtc, Burst.Alarm.TriggeredAtUtc, []);
        return new CommunicationsResult(incident.IncidentId, notice, false, [], incident, "http://site/evides");
    }

    private static ResourceMatch Match(DiagnosticReport report)
    {
        var roster = Data.Technicians.Select(t => WorkforceAvailability.Evaluate(t, report.TriggeredAtUtc, Procedure.RequiredCertifications, report.Location)).ToList();
        return MatchmakerRules.Build(report, Procedure, roster, new MatchJudgement("T-02", "T-05", "Samira El Idrissi goes."));
    }

    private static ProcurementResult Package(DiagnosticReport report) =>
        new(IncidentIds.For(report.AlarmId), report.AlarmId, Procedure.Id, true,
            [new PackageLine("CLP-CI-300", "clamp", "part", 1, 1, 0, "always", "reserved")], [], [], "Reserved.", "Why.", [], false, []);

    private static SchedulingResult Schedule(DiagnosticReport report, ResourceMatch match)
    {
        var at = Burst.Alarm.TriggeredAtUtc;
        var order = new WorkOrder("WO-" + IncidentIds.For(report.AlarmId), IncidentIds.For(report.AlarmId), report.SegmentId, report.Street, report.ChainageM, report.Location,
            "burst", "S1", "x", "T-02", "Samira", Procedure.Id, [], [], [], at, 37, 46, 0, 6, at.AddMinutes(47), at.AddHours(7), "scheduled", at, null, at);
        var incident = Communications(report).Published;
        return new SchedulingResult(order.IncidentId, order, [], "Go.", false, [], [], at.AddHours(8), incident, []);
    }

    internal class FakeGateway : IAgentGateway
    {
        public List<(string Step, object Body)> Calls { get; } = [];
        public Dictionary<string, Func<object, object>> Answers { get; } = [];

        public static FakeGateway Happy(string verdict = "genuine_leak")
        {
            var report = Report();
            var match = Match(report);
            var gateway = new FakeGateway();
            gateway.Answers[WorkflowSteps.Triage] = _ => Triage(verdict);
            gateway.Answers[WorkflowSteps.Diagnostic] = _ => report;
            gateway.Answers[WorkflowSteps.Communications] = _ => Communications(report);
            gateway.Answers[WorkflowSteps.Matchmaker] = _ => match;
            gateway.Answers[WorkflowSteps.Procurement] = _ => Package(report);
            gateway.Answers[WorkflowSteps.Scheduling] = _ => Schedule(report, match);
            return gateway;
        }

        public Task<AgentResult<T>> CallAsync<T>(string step, object body, CancellationToken cancellationToken)
        {
            Calls.Add((step, body));
            return Task.FromResult(new AgentResult<T>((T)Answers[step](body), Trace));   // an answer function may throw, like a failing agent
        }
    }

    private static async Task<(WorkflowSummary Summary, List<WorkflowEvent> Events)> Run(FakeGateway gateway)
    {
        var events = new List<WorkflowEvent>();
        var runner = new WorkflowRunner(gateway, TimeProvider.System);
        var summary = await runner.RunAsync(Burst.Alarm, e => { events.Add(e); return ValueTask.CompletedTask; }, CancellationToken.None);
        return (summary, events);
    }

    [Fact]
    public async Task A_genuine_leak_runs_all_six_agents_in_order_and_each_gets_what_the_earlier_ones_found()
    {
        var gateway = FakeGateway.Happy();
        var (summary, events) = await Run(gateway);

        Assert.Equal(WorkflowOutcomes.Completed, summary.Outcome);
        Assert.Equal(WorkflowSteps.All, gateway.Calls.Select(c => c.Step));
        Assert.Equal("INC-2026-1114-0001", summary.IncidentId);
        Assert.Equal("WO-INC-2026-1114-0001", summary.WorkOrderId);
        Assert.NotNull(summary.ExpectedResolutionUtc);
        Assert.Null(summary.StoppedAt);

        // The later agents receive the answers of the earlier ones.
        Assert.IsType<AnomalyAlarm>(gateway.Calls[0].Body);
        Assert.Equal("genuine_leak", ((DiagnosticRequest)gateway.Calls[1].Body).Triage.Verdict);
        Assert.Equal("SEG-0412", ((CommunicationsRequest)gateway.Calls[2].Body).Report.SegmentId);
        var scheduling = (SchedulingRequest)gateway.Calls[5].Body;
        Assert.Equal("T-02", scheduling.Match.Primary!.Id);
        Assert.Equal("CLP-CI-300", scheduling.Package.Lines[0].Sku);

        // run_started, then started and completed for each of the six steps, then run_completed.
        Assert.Equal(WorkflowEventTypes.RunStarted, events[0].Type);
        Assert.Equal(WorkflowEventTypes.RunCompleted, events[^1].Type);
        Assert.Equal(WorkflowSteps.All.SelectMany(s => new[] { WorkflowEventTypes.StepStarted + ":" + s, WorkflowEventTypes.StepCompleted + ":" + s }),
            events.Where(e => e.Step is not null).Select(e => e.Type + ":" + e.Step));
        Assert.All(events.Where(e => e.Type == WorkflowEventTypes.StepCompleted), e => Assert.False(string.IsNullOrWhiteSpace(e.Headline)));
        Assert.Equal(6, summary.Steps.Count(s => s.Status == "completed"));
    }

    [Fact]
    public async Task A_false_alarm_ends_the_workflow_after_triage()
    {
        var gateway = FakeGateway.Happy("false_alarm");
        var (summary, events) = await Run(gateway);

        Assert.Equal(WorkflowOutcomes.ClosedFalseAlarm, summary.Outcome);
        Assert.Equal([WorkflowSteps.Triage], gateway.Calls.Select(c => c.Step));
        Assert.Equal(WorkflowSteps.Triage, summary.StoppedAt);
        Assert.Contains("Planned works", summary.Reasons[0]);
        Assert.Null(summary.IncidentId);   // nothing was published
        Assert.Equal(WorkflowEventTypes.RunCompleted, events[^1].Type);
    }

    [Fact]
    public async Task An_unsure_triage_goes_to_a_person_instead_of_on()
    {
        var gateway = FakeGateway.Happy("uncertain");
        var (summary, _) = await Run(gateway);

        Assert.Equal(WorkflowOutcomes.NeedsReview, summary.Outcome);
        Assert.Equal([WorkflowSteps.Triage], gateway.Calls.Select(c => c.Step));
        Assert.Contains("not sure", summary.Reasons[0]);
    }

    [Fact]
    public async Task A_diagnosis_that_needs_review_stops_before_residents_are_told()
    {
        var gateway = FakeGateway.Happy();
        gateway.Answers[WorkflowSteps.Diagnostic] = _ => Report() with { NeedsReview = true, ReviewReasons = ["The position is not certain."] };
        var (summary, _) = await Run(gateway);

        Assert.Equal(WorkflowOutcomes.NeedsReview, summary.Outcome);
        Assert.Equal(WorkflowSteps.Diagnostic, summary.StoppedAt);
        Assert.Equal(["The position is not certain."], summary.Reasons);
        Assert.DoesNotContain(gateway.Calls, c => c.Step == WorkflowSteps.Communications);
    }

    [Fact]
    public async Task When_nobody_can_be_matched_the_run_stops_and_says_that_residents_were_already_informed()
    {
        var gateway = FakeGateway.Happy();
        gateway.Answers[WorkflowSteps.Matchmaker] = _ => Match(Report()) with { Primary = null, Backup = null, NeedsReview = true, ReviewReasons = ["Nobody is on shift."] };
        var (summary, _) = await Run(gateway);

        Assert.Equal(WorkflowOutcomes.NeedsReview, summary.Outcome);
        Assert.Equal(WorkflowSteps.Matchmaker, summary.StoppedAt);
        Assert.Contains("Nobody is on shift.", summary.Reasons);
        Assert.Contains(summary.Reasons, r => r.Contains("Residents have already been informed (INC-2026-1114-0001)"));
        Assert.DoesNotContain(gateway.Calls, c => c.Step is WorkflowSteps.Procurement or WorkflowSteps.Scheduling);
    }

    [Fact]
    public async Task A_package_that_cannot_be_equipped_stops_before_scheduling()
    {
        var gateway = FakeGateway.Happy();
        gateway.Answers[WorkflowSteps.Procurement] = _ => Package(Report()) with { NeedsReview = true, ReviewReasons = ["No excavator is available."] };
        var (summary, _) = await Run(gateway);

        Assert.Equal(WorkflowOutcomes.NeedsReview, summary.Outcome);
        Assert.Equal(WorkflowSteps.Procurement, summary.StoppedAt);
        Assert.DoesNotContain(gateway.Calls, c => c.Step == WorkflowSteps.Scheduling);
    }

    [Fact]
    public async Task An_agent_error_ends_the_run_as_failed_with_the_step_and_the_reason_and_does_not_throw()
    {
        var gateway = FakeGateway.Happy();
        gateway.Answers[WorkflowSteps.Procurement] = _ => throw new AgentCallException(WorkflowSteps.Procurement, 502, "blocked_by_content_filter", "The procurement agent failed.");
        var (summary, events) = await Run(gateway);

        Assert.Equal(WorkflowOutcomes.Failed, summary.Outcome);
        Assert.Equal(WorkflowSteps.Procurement, summary.StoppedAt);
        Assert.Contains("502 blocked_by_content_filter", summary.Reasons[0]);
        Assert.Contains(events, e => e.Type == WorkflowEventTypes.StepFailed && e.Step == WorkflowSteps.Procurement);
        Assert.Equal("failed", summary.Steps[^1].Status);
        Assert.DoesNotContain(gateway.Calls, c => c.Step == WorkflowSteps.Scheduling);
    }

    [Fact]
    public async Task An_unexpected_exception_in_the_gateway_also_ends_the_run_as_failed()
    {
        var gateway = FakeGateway.Happy();
        gateway.Answers[WorkflowSteps.Triage] = _ => throw new HttpRequestException("connection refused");
        var (summary, _) = await Run(gateway);

        Assert.Equal(WorkflowOutcomes.Failed, summary.Outcome);
        Assert.Equal(WorkflowSteps.Triage, summary.StoppedAt);
        Assert.Contains("connection refused", summary.Reasons[0]);
    }

    [Fact]
    public async Task Cancelling_stops_the_run_with_an_exception_instead_of_reporting_a_failure()
    {
        var gateway = FakeGateway.Happy();
        using var cts = new CancellationTokenSource();
        gateway.Answers[WorkflowSteps.Triage] = _ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); };
        var runner = new WorkflowRunner(gateway, TimeProvider.System);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(Burst.Alarm, _ => ValueTask.CompletedTask, cts.Token));
    }

    private static PauseGate Gate(bool answer, List<string> log, Func<Task>? whileWaiting = null) => new(WorkflowSteps.Communications, async (runId, announce, ct) =>
    {
        await announce();
        log.Add("waiting");
        if (whileWaiting is not null) await whileWaiting();
        return answer;
    });

    [Fact]
    public async Task A_pause_before_Communications_waits_after_Diagnostic_and_publishes_nothing_until_resumed()
    {
        var gateway = FakeGateway.Happy();
        var events = new List<WorkflowEvent>();
        var seenWhileWaiting = new List<string>();
        var gate = Gate(true, [], () => { seenWhileWaiting.AddRange(gateway.Calls.Select(c => c.Step)); return Task.CompletedTask; });

        var summary = await new WorkflowRunner(gateway, TimeProvider.System).RunAsync(Burst.Alarm, e => { events.Add(e); return ValueTask.CompletedTask; }, CancellationToken.None, gate);

        Assert.Equal([WorkflowSteps.Triage, WorkflowSteps.Diagnostic], seenWhileWaiting);   // Communications had not been called when the run waited
        Assert.Equal(WorkflowOutcomes.Completed, summary.Outcome);
        Assert.Equal(WorkflowSteps.All, gateway.Calls.Select(c => c.Step));
        var order = events.Select(e => e.Type + ":" + e.Step).ToList();
        Assert.True(order.IndexOf("step_completed:diagnostic") < order.IndexOf("run_paused:communications"));
        Assert.True(order.IndexOf("run_paused:communications") < order.IndexOf("run_resumed:communications"));
        Assert.True(order.IndexOf("run_resumed:communications") < order.IndexOf("step_started:communications"));
    }

    [Fact]
    public async Task Cancelling_a_pause_ends_the_run_as_cancelled_without_telling_residents_or_booking_anything()
    {
        var gateway = FakeGateway.Happy();
        var events = new List<WorkflowEvent>();

        var summary = await new WorkflowRunner(gateway, TimeProvider.System).RunAsync(Burst.Alarm, e => { events.Add(e); return ValueTask.CompletedTask; }, CancellationToken.None, Gate(false, []));

        Assert.Equal(WorkflowOutcomes.Cancelled, summary.Outcome);
        Assert.Equal(WorkflowSteps.Diagnostic, summary.StoppedAt);
        Assert.Null(summary.IncidentId);
        Assert.Equal([WorkflowSteps.Triage, WorkflowSteps.Diagnostic], gateway.Calls.Select(c => c.Step));
        Assert.DoesNotContain(events, e => e.Type == WorkflowEventTypes.RunResumed);
        Assert.DoesNotContain("already been informed", string.Join(' ', summary.Reasons));
    }

    [Fact]
    public async Task A_pause_is_not_reached_when_the_run_stops_earlier()
    {
        var gateway = FakeGateway.Happy("false_alarm");
        var log = new List<string>();

        var summary = await new WorkflowRunner(gateway, TimeProvider.System).RunAsync(Burst.Alarm, _ => ValueTask.CompletedTask, CancellationToken.None, Gate(true, log));

        Assert.Equal(WorkflowOutcomes.ClosedFalseAlarm, summary.Outcome);
        Assert.Empty(log);
    }
}
