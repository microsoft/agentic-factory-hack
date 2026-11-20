using System.Diagnostics;

namespace LeakOps.Contracts.Logic;

/// <summary>
/// The sequential pipeline: Triage, Diagnostic, Communications, Resource Matchmaker, Procurement, Scheduling. Each agent gets the
/// answers of the earlier ones. The runner decides when to stop: a false alarm closes the run, and any agent that says a person has to
/// decide ends it there (with the reasons), so nothing further is booked on a doubtful basis. It never throws for an agent problem:
/// the run ends as "failed" and says which step failed. Every step is safe to run again (the agents are idempotent), so an operator
/// can simply run the same alarm once more. After the Diagnostic step it hands the report to the GIS feed (if any), best effort.
/// </summary>
public class WorkflowRunner(IAgentGateway gateway, TimeProvider time, IGisFeed? gis = null)
{
    public async Task<WorkflowSummary> RunAsync(AnomalyAlarm alarm, Func<WorkflowEvent, ValueTask> emit, CancellationToken cancellationToken, PauseGate? pause = null)
    {
        var started = time.GetUtcNow();
        var runId = $"run-{started:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
        var clock = Stopwatch.StartNew();
        var steps = new List<StepRecord>();
        var total = WorkflowSteps.All.Count;
        var incidentId = (string?)null;

        ValueTask Emit(string type, string? step = null, long? durationMs = null, string? headline = null, object? data = null,
            string? outcome = null, IReadOnlyList<string>? reasons = null, WorkflowSummary? summary = null) =>
            emit(new WorkflowEvent(type, runId, time.GetUtcNow(), step, step is null ? null : WorkflowSteps.All.ToList().IndexOf(step) + 1,
                total, durationMs, headline, data, outcome, reasons, summary));

        async Task<T?> Step<T>(string step, object body, Func<T, string> describe) where T : class
        {
            await Emit(WorkflowEventTypes.StepStarted, step);
            var watch = Stopwatch.StartNew();
            try
            {
                var result = await gateway.CallAsync<T>(step, body, cancellationToken);
                var headline = describe(result.Result);
                steps.Add(new StepRecord(step, "completed", watch.ElapsedMilliseconds, headline));
                await Emit(WorkflowEventTypes.StepCompleted, step, watch.ElapsedMilliseconds, headline, result);
                return result.Result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                var message = ex is AgentCallException call ? $"{call.Message} ({call.StatusCode} {call.Reason})" : $"{ex.GetType().Name}: {ex.Message}";
                steps.Add(new StepRecord(step, "failed", watch.ElapsedMilliseconds, message));
                await Emit(WorkflowEventTypes.StepFailed, step, watch.ElapsedMilliseconds, message);
                return null;
            }
        }

        async Task<WorkflowSummary> Finish(string outcome, string? stoppedAt, IReadOnlyList<string> reasons, string? workOrderId = null, DateTimeOffset? eta = null)
        {
            var informed = steps.Any(s => s.Step == WorkflowSteps.Communications && s.Status == "completed");
            IReadOnlyList<string> allReasons = informed && outcome is WorkflowOutcomes.NeedsReview or WorkflowOutcomes.Failed
                ? [.. reasons, $"Residents have already been informed ({incidentId})."]
                : reasons;
            var summary = new WorkflowSummary(runId, alarm.AlarmId, outcome, started, clock.ElapsedMilliseconds, steps, stoppedAt, allReasons, incidentId, workOrderId, eta);
            await Emit(WorkflowEventTypes.RunCompleted, outcome: outcome, reasons: allReasons, summary: summary);
            return summary;
        }

        await Emit(WorkflowEventTypes.RunStarted, data: alarm);

        var triage = await Step<TriageVerdict>(WorkflowSteps.Triage, alarm, v =>
            $"{v.Verdict} (confidence {v.Confidence:0.00})" + (v.SuspectedSegmentId is null ? "" : $", suspected segment {v.SuspectedSegmentId}"));
        if (triage is null) return await Finish(WorkflowOutcomes.Failed, WorkflowSteps.Triage, [steps[^1].Headline]);
        if (triage.Verdict == "false_alarm")
            return await Finish(WorkflowOutcomes.ClosedFalseAlarm, WorkflowSteps.Triage, ["Triage found no leak: " + triage.Summary]);
        if (triage.Verdict != "genuine_leak")
            return await Finish(WorkflowOutcomes.NeedsReview, WorkflowSteps.Triage, [$"Triage is not sure ({triage.Verdict}, confidence {triage.Confidence:0.00}): " + triage.Summary]);

        var report = await Step<DiagnosticReport>(WorkflowSteps.Diagnostic, new DiagnosticRequest(alarm, triage), r =>
            $"{r.SegmentId} {SchedulingRules.StreetName(r.Street)}: DN{r.DiameterMm} {r.Material.Replace('_', ' ')}, {r.BreachType}, severity {r.Severity}, {r.CustomersAffected} customers"
            + (r.NeedsReview ? " - needs review" : ""));
        if (report is null) return await Finish(WorkflowOutcomes.Failed, WorkflowSteps.Diagnostic, [steps[^1].Headline]);

        // Show the diagnosis on the simulated GIS map, also when it needs review (the map flags that). Never let the map stop the run.
        if (gis is not null)
        {
            try { await gis.PublishAsync(report, cancellationToken); }
            catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
        }

        if (report.NeedsReview) return await Finish(WorkflowOutcomes.NeedsReview, WorkflowSteps.Diagnostic, report.ReviewReasons);

        // The operator may ask to pause before a step, so the audience can look at the outside world first. Returns false when the run is cancelled.
        async Task<bool> PauseBefore(string step, string headline)
        {
            if (pause is null || !pause.Applies(step)) return true;
            if (!await pause.Wait(runId, () => Emit(WorkflowEventTypes.RunPaused, step, headline: headline), cancellationToken)) return false;
            await Emit(WorkflowEventTypes.RunResumed, step, headline: "Resumed");
            return true;
        }

        // Nothing has been written yet.
        if (!await PauseBefore(WorkflowSteps.Communications, "Paused before Communications: residents have not been told yet"))
            return await Finish(WorkflowOutcomes.Cancelled, WorkflowSteps.Diagnostic, ["Paused before Communications and not resumed: nothing was published or booked."]);

        var communications = await Step<CommunicationsResult>(WorkflowSteps.Communications, new CommunicationsRequest(report), c =>
            $"Published {c.IncidentId} on Waterstoring" + (c.UsedFallbackText ? " (safe template text)" : ""));
        if (communications is null) return await Finish(WorkflowOutcomes.Failed, WorkflowSteps.Communications, [steps[^1].Headline]);
        incidentId = communications.IncidentId;

        // Residents know by now, but nobody is assigned, no stock is reserved and no work order exists.
        if (!await PauseBefore(WorkflowSteps.Matchmaker, "Paused before Resource Matchmaker: no technician is assigned yet"))
            return await Finish(WorkflowOutcomes.Cancelled, WorkflowSteps.Communications,
                [$"Paused before Resource Matchmaker and not resumed: residents were already informed ({incidentId}), but no technician, stock or work order was booked."]);

        var match = await Step<ResourceMatch>(WorkflowSteps.Matchmaker, new MatchmakerRequest(report), m =>
            m.Primary is null
                ? "Nobody eligible"
                : $"{m.Primary.Name} ({m.Primary.Id}), {m.Primary.EstimatedTravelMinutes} min away" + (m.Backup is null ? "" : $"; backup {m.Backup.Id}"));
        if (match is null) return await Finish(WorkflowOutcomes.Failed, WorkflowSteps.Matchmaker, [steps[^1].Headline]);
        if (match.NeedsReview || match.Primary is null) return await Finish(WorkflowOutcomes.NeedsReview, WorkflowSteps.Matchmaker, match.ReviewReasons);

        // A technician is proposed, but no stock is reserved, nothing is ordered and there is no work order yet.
        if (!await PauseBefore(WorkflowSteps.Procurement, "Paused before Procurement: nothing is reserved or ordered yet"))
            return await Finish(WorkflowOutcomes.Cancelled, WorkflowSteps.Matchmaker,
                [$"Paused before Procurement and not resumed: residents were already informed ({incidentId}) and {match.Primary.Name} was proposed, but no stock was reserved, nothing was ordered and no work order was made."]);

        var package = await Step<ProcurementResult>(WorkflowSteps.Procurement, new ProcurementRequest(report), p =>
            $"{p.Lines.Count(l => l.Reserved > 0)} items reserved, {p.Lines.Count(l => l.Short > 0)} short, {p.PurchaseOrders.Count} purchase orders"
            + (p.PackageComplete ? "" : " - package not complete"));
        if (package is null) return await Finish(WorkflowOutcomes.Failed, WorkflowSteps.Procurement, [steps[^1].Headline]);
        if (package.NeedsReview) return await Finish(WorkflowOutcomes.NeedsReview, WorkflowSteps.Procurement, package.ReviewReasons);

        var schedule = await Step<SchedulingResult>(WorkflowSteps.Scheduling, new SchedulingRequest(report, match, package), s =>
            $"Work order {s.WorkOrder.Id}, arrives {SchedulingRules.Hm(s.WorkOrder.ExpectedArrivalUtc)}, done {SchedulingRules.Hm(s.WorkOrder.ExpectedCompletionUtc)}, residents told "
            + (s.ExpectedResolutionUtc is { } eta ? SchedulingRules.Hm(eta) : "'to be determined'"));
        if (schedule is null) return await Finish(WorkflowOutcomes.Failed, WorkflowSteps.Scheduling, [steps[^1].Headline]);

        return await Finish(WorkflowOutcomes.Completed, null, [], schedule.WorkOrder.Id, schedule.ExpectedResolutionUtc);
    }
}
