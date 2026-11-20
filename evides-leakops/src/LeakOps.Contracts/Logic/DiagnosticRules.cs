namespace LeakOps.Contracts.Logic;

/// <summary>
/// Builds the final diagnostic report from the model's judgement and the authoritative facts. The model decides what only it can
/// (burst or leak, the order to close valves, the explanation); everything factual comes from GIS data and fixed rules. Whenever the
/// model's answer had to be corrected, or something looks risky, a warning is added so a person can see it.
/// </summary>
public static class DiagnosticRules
{
    private static readonly TimeSpan StiffValveAge = TimeSpan.FromDays(2 * 365);

    /// <summary>The rule from the diagnostic instructions: a burst when the inlet flow is this far above what is expected, or when the pressure fell this far.</summary>
    public const double BurstInletExcessM3h = 40;
    public const double BurstDropBar = 1.0;

    public static DiagnosticReport Build(
        DiagnosisJudgement judgement,
        PipeSegment segment,
        IReadOnlyList<IsolationValveInfo> valves,
        RepairProcedure? procedure,
        TriageVerdict triage,
        IReadOnlyList<SegmentDrop> pressureDrops,
        DiagnosticFacts? facts = null)
    {
        var warnings = new List<string>();
        var reviewReasons = new List<string>();

        if (triage.SuspectedSegmentId is { Length: > 0 } suspected && suspected != segment.Id)
            warnings.Add($"This diagnosis is for {segment.Id}, not {suspected} as Triage suspected.");

        // Cross-check with the data: the breach should be where the pressure fell most.
        var strongest = pressureDrops.OrderByDescending(d => d.MaxDropBar).FirstOrDefault();
        if (strongest is not null && strongest.SegmentId != segment.Id)
        {
            var own = pressureDrops.FirstOrDefault(d => d.SegmentId == segment.Id)?.MaxDropBar ?? 0;
            if (own < 0.5 * strongest.MaxDropBar)
                reviewReasons.Add($"The strongest pressure drop is on {strongest.SegmentId} ({strongest.MaxDropBar:F2} bar), but this diagnosis is for {segment.Id} ({own:F2} bar).");
        }

        var breachType = judgement.BreachType is "burst" or "leak" ? judgement.BreachType : "burst";
        if (breachType != judgement.BreachType)
            warnings.Add($"Breach type '{judgement.BreachType}' is not valid; assumed 'burst' (the safer assumption).");

        // The data has a say: when code measured the inlet flow and the pressure drop, the burst rule applies. If the model and the rule
        // disagree the safer reading (burst) stands, so a real burst is never downgraded by a wrong answer.
        var ownDrop = pressureDrops.FirstOrDefault(d => d.SegmentId == segment.Id)?.MaxDropBar;
        if (facts is not null && (facts.InletExcessM3h is not null || ownDrop is not null))
        {
            var byRule = (facts.InletExcessM3h ?? 0) >= BurstInletExcessM3h || (ownDrop ?? 0) >= BurstDropBar ? "burst" : "leak";
            if (byRule != breachType)
            {
                var chosen = byRule == "burst" || breachType == "burst" ? "burst" : "leak";
                warnings.Add($"The model said '{breachType}', but the data says '{byRule}' (inlet flow {facts.InletExcessM3h?.ToString("F0") ?? "unknown"} m3/h above expected, pressure fell {ownDrop?.ToString("F2") ?? "unknown"} bar); '{chosen}' was used.");
                breachType = chosen;
            }
        }

        // The position comes from the pressure-gradient locator that code ran, not from the model's number.
        double chainage, uncertainty;
        if (facts?.EstimatedChainageM is { } estimated && facts.EstimateUncertaintyM is { } estimatedUncertainty)
        {
            chainage = estimated;
            uncertainty = estimatedUncertainty;
            if (double.IsNaN(judgement.ChainageM) || Math.Abs(judgement.ChainageM - estimated) > 1 || double.IsNaN(judgement.UncertaintyM) || Math.Abs(judgement.UncertaintyM - estimatedUncertainty) > 1)
                warnings.Add($"The model gave position {judgement.ChainageM:F0} m (±{judgement.UncertaintyM:F0} m); the locator's {estimated:F0} m (±{estimatedUncertainty:F0} m) was used.");
        }
        else if (facts is not null)
        {
            chainage = segment.LengthM / 2;
            uncertainty = segment.LengthM / 2;
            warnings.Add($"No pressure sensor on {segment.Id} fell enough to locate the breach; the position along the segment is unknown.");
        }
        else
        {
            chainage = double.IsNaN(judgement.ChainageM) ? segment.LengthM / 2 : Math.Clamp(judgement.ChainageM, 0, segment.LengthM);
            if (chainage != judgement.ChainageM)
                warnings.Add($"Position {judgement.ChainageM} m is outside the segment (0 to {segment.LengthM} m); corrected to {chainage} m.");
            uncertainty = double.IsNaN(judgement.UncertaintyM) ? segment.LengthM / 2 : Math.Max(0, judgement.UncertaintyM);
        }
        chainage = Math.Clamp(chainage, 0, segment.LengthM);
        if (uncertainty > 0.4 * segment.LengthM)
            reviewReasons.Add($"The position along {segment.Id} is only known to within ±{Math.Round(uncertainty)} m of a {segment.LengthM} m segment.");

        var assessment = SeverityRubric.Classify(breachType, segment.CustomersServed, segment.CriticalCustomers, segment.DiameterMm);

        var plan = BuildIsolationPlan(judgement.IsolationValveIds, segment, valves, warnings);

        foreach (var valve in valves.Where(v => segment.IsolationValveIds.Contains(v.Id)))
        {
            if (valve.Status != "open")
                warnings.Add($"Valve {valve.Id} is {valve.Status}: isolation may not work, plan a backup valve.");
            else if (valve.LastOperatedUtc is { } last && triage.TriggeredAtUtc - last > StiffValveAge)
                warnings.Add($"Valve {valve.Id} was last operated {last:yyyy-MM-dd}: it may be stiff or seized, have a backup valve ready.");
        }

        if (procedure is null)
            reviewReasons.Add($"No standard repair procedure exists for {segment.Material} DN{segment.DiameterMm} ({breachType}), so the crew, parts and equipment cannot be planned automatically.");
        if (string.IsNullOrWhiteSpace(judgement.Summary))
            warnings.Add("The model gave no summary.");
        if ((judgement.Evidence ?? []).Count == 0)
            warnings.Add("The model gave no evidence.");

        return new DiagnosticReport(
            AlarmId: triage.AlarmId,
            DistrictId: triage.DistrictId,
            TriggeredAtUtc: LeakOpsTime.Utc(triage.TriggeredAtUtc),
            SegmentId: segment.Id,
            Street: segment.Street,
            Place: segment.Place,
            ServedStreets: segment.ServedStreets,
            ChainageM: Math.Round(chainage),
            UncertaintyM: Math.Round(uncertainty),
            Location: Geo.PointAt(segment, chainage),
            SegmentPath: [segment.Start, segment.End],
            Material: segment.Material,
            DiameterMm: segment.DiameterMm,
            InstallYear: segment.InstallYear,
            DepthM: segment.DepthM,
            Surface: segment.Surface,
            RoadType: segment.RoadType,
            BreachType: breachType,
            Severity: assessment.Severity,
            SeverityReasons: assessment.Reasons,
            CustomersAffected: segment.CustomersServed,
            CriticalCustomers: segment.CriticalCustomers,
            PostalCodes: segment.PostalCodes,
            IsolationPlan: plan,
            RepairProcedureId: procedure?.Id,
            Summary: judgement.Summary?.Trim() ?? "",
            Evidence: (judgement.Evidence ?? []).Where(e => !string.IsNullOrWhiteSpace(e.Finding)).ToList(),
            Warnings: warnings,
            NeedsReview: reviewReasons.Count > 0,
            ReviewReasons: reviewReasons);
    }

    /// <summary>
    /// A break inside a segment has to be isolated on both sides, so the plan always contains every isolation valve of the segment.
    /// The model's order is kept for the valves it named; unknown valve ids are dropped; missing valves are added, nearest first.
    /// </summary>
    private static List<IsolationStep> BuildIsolationPlan(
        IReadOnlyList<string>? proposed, PipeSegment segment, IReadOnlyList<IsolationValveInfo> valves, List<string> warnings)
    {
        var known = valves.Where(v => segment.IsolationValveIds.Contains(v.Id)).ToDictionary(v => v.Id);
        var ordered = new List<string>();

        foreach (var id in proposed ?? [])
        {
            if (known.ContainsKey(id)) { if (!ordered.Contains(id)) ordered.Add(id); }
            else warnings.Add($"Valve {id} is not an isolation valve of {segment.Id}; left out of the plan.");
        }

        var missing = known.Values.Where(v => !ordered.Contains(v.Id)).OrderBy(v => v.DistanceAlongPipeM).Select(v => v.Id).ToList();
        if (missing.Count > 0)
        {
            warnings.Add($"The plan was missing isolation valve(s) {string.Join(", ", missing)}; added, because a break inside a segment must be closed off on both sides.");
            ordered.AddRange(missing);
        }

        return ordered.Select((id, index) =>
        {
            var v = known[id];
            return new IsolationStep(index + 1, id, "close", v.TurnsToClose, v.Status, v.Location, v.LastOperatedUtc);
        }).ToList();
    }
}
