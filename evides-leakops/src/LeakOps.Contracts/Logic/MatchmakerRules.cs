namespace LeakOps.Contracts.Logic;

/// <summary>
/// The rules of the Resource Matchmaker. The model proposes a technician and writes the explanation; these rules decide who is
/// allowed: on shift, not busy, holding every certification the repair procedure requires. Among the eligible the fastest to arrive wins.
/// </summary>
public static class MatchmakerRules
{
    public static List<CandidateAssessment> Assess(IEnumerable<TechnicianStatus> statuses) =>
        statuses.Select(s => new CandidateAssessment(
                s.Id, s.Name, s.Available && s.HasAllRequiredCertifications, ReasonFor(s), s.EstimatedTravelMinutes,
                s.StraightLineKm))
            .OrderByDescending(c => c.Eligible)
            .ThenBy(c => c.TravelMinutes ?? int.MaxValue)
            .ThenBy(c => c.TechnicianId, StringComparer.Ordinal)
            .ToList();

    public static string ReasonFor(TechnicianStatus s)
    {
        var problems = new List<string>();
        if (!s.OnShift)
            problems.Add(s.NextShiftStartsUtc is { } next ? $"not on shift (next shift starts {Local(next)})" : "not on shift");
        else if (s.BusyUntilUtc is { } until)
            problems.Add($"busy with {s.BusyWith ?? "another job"} until {Local(until)}");
        if (!s.HasAllRequiredCertifications)
            problems.Add($"missing {string.Join(", ", s.MissingCertifications)}");
        return problems.Count == 0 ? "on shift, free and fully certified" : string.Join("; ", problems);
    }

    /// <summary>Builds the report from facts. The model's choice is kept when it is eligible and as fast as the best option; otherwise it is corrected.</summary>
    public static ResourceMatch Build(
        DiagnosticReport report,
        RepairProcedure? procedure,
        IReadOnlyList<TechnicianStatus> statuses,
        MatchJudgement judgement)
    {
        var warnings = new List<string>();
        var reviewReasons = new List<string>();
        var required = procedure?.RequiredCertifications ?? [];
        var needsExcavator = procedure?.Equipment.Any(e => e.EquipmentType == "excavator") ?? false;

        if (procedure is null)
            reviewReasons.Add("There is no repair procedure for this pipe, so the required certifications are unknown.");

        var candidates = Assess(statuses);
        var eligible = statuses
            .Where(s => s.Available && s.HasAllRequiredCertifications)
            .OrderBy(s => s.EstimatedTravelMinutes ?? int.MaxValue)
            .ThenBy(s => s.StraightLineKm ?? double.MaxValue)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

        TechnicianStatus? primary = null, backup = null;
        var corrected = false;
        if (procedure is not null && eligible.Count > 0)
        {
            var best = eligible[0];
            var proposed = eligible.FirstOrDefault(s => Same(s.Id, judgement.PrimaryTechnicianId));
            if (proposed is not null && proposed.EstimatedTravelMinutes == best.EstimatedTravelMinutes)
                primary = proposed;
            else
            {
                primary = best;
                corrected = true;
                warnings.Add(proposed is null
                    ? $"The model proposed {Show(judgement.PrimaryTechnicianId)}, who is not eligible or unknown; {best.Id} was chosen instead."
                    : $"The model proposed {proposed.Id}, but {best.Id} arrives sooner ({best.EstimatedTravelMinutes} min against {proposed.EstimatedTravelMinutes} min); {best.Id} was chosen.");
            }

            var others = eligible.Where(s => s.Id != primary.Id).ToList();
            backup = others.FirstOrDefault(s => Same(s.Id, judgement.BackupTechnicianId)) ?? others.FirstOrDefault();
            if (backup is not null && !Same(backup.Id, judgement.BackupTechnicianId) && !string.IsNullOrWhiteSpace(judgement.BackupTechnicianId))
                warnings.Add($"The model proposed {judgement.BackupTechnicianId.Trim()} as backup, who is not eligible; {backup.Id} was chosen.");
            if (backup is null)
                warnings.Add("There is no eligible backup technician.");
        }
        else if (procedure is not null)
        {
            reviewReasons.Add("No technician is on shift, free and holding every required certification (" + string.Join(", ", required) + ").");
            var soonest = EarliestQualified(statuses);
            if (soonest is not null)
                reviewReasons.Add(soonest);
        }

        if (primary is not null)
        {
            if (needsExcavator && !primary.CanTowExcavator)
                warnings.Add($"{primary.Name} cannot tow an excavator; the excavator must be delivered separately.");
            if (primary.ShiftEndsUtc is { } ends && procedure is not null)
            {
                var arrival = LeakOpsTime.Utc(report.TriggeredAtUtc).AddMinutes(primary.EstimatedTravelMinutes ?? 0);
                var repairEnds = arrival.AddHours(procedure.EstimatedRepairHours);
                if (ends < repairEnds)
                    warnings.Add($"{primary.Name}'s shift ends at {Local(ends)}, before the repair is expected to finish ({Local(repairEnds)}); a handover or overtime is needed.");
            }
        }

        var summary = primary is null || corrected || !MentionsTechnician(judgement.Summary, primary)
            ? FallbackSummary(primary, backup, candidates, reviewReasons)
            : judgement.Summary.Trim();
        if (primary is not null && summary != judgement.Summary?.Trim())
            warnings.Add("The summary was written by the rules because the model's text did not match the chosen technician.");

        return new ResourceMatch(
            IncidentId: IncidentIds.For(report.AlarmId),
            AlarmId: report.AlarmId,
            SegmentId: report.SegmentId,
            Site: report.Location,
            RequiredCertifications: required,
            RepairProcedureId: procedure?.Id,
            EstimatedRepairHours: procedure?.EstimatedRepairHours ?? 0,
            NeedsExcavator: needsExcavator,
            Primary: primary,
            Backup: backup,
            Candidates: candidates,
            Summary: summary,
            Warnings: warnings,
            NeedsReview: reviewReasons.Count > 0,
            ReviewReasons: reviewReasons);
    }

    /// <summary>Who would be the first qualified person if nobody is free now: helps the dispatcher decide how long to wait.</summary>
    private static string? EarliestQualified(IReadOnlyList<TechnicianStatus> statuses)
    {
        var options = statuses.Where(s => s.HasAllRequiredCertifications)
            .Select(s => (Status: s, At: s.BusyUntilUtc ?? s.NextShiftStartsUtc))
            .Where(x => x.At is not null)
            .OrderBy(x => x.At)
            .FirstOrDefault();
        return options.Status is null ? "Nobody holding the required certifications is rostered." : $"The first qualified technician is {options.Status.Name} ({options.Status.Id}) from {Local(options.At!.Value)}.";
    }

    private static string FallbackSummary(TechnicianStatus? primary, TechnicianStatus? backup, IReadOnlyList<CandidateAssessment> candidates, IReadOnlyList<string> reviewReasons)
    {
        if (primary is null)
            return "Nobody can be dispatched yet. " + string.Join(" ", reviewReasons);

        var skipped = candidates.Where(c => !c.Eligible).Take(3).Select(c => $"{c.Name} ({c.TechnicianId}) is out: {c.Reason}");
        var text = $"{primary.Name} ({primary.Id}) is the nearest eligible technician, {primary.EstimatedTravelMinutes} min away. ";
        if (backup is not null) text += $"{backup.Name} ({backup.Id}) is the backup, {backup.EstimatedTravelMinutes} min away. ";
        return (text + string.Join(". ", skipped)).Trim();
    }

    private static bool MentionsTechnician(string? text, TechnicianStatus technician) =>
        !string.IsNullOrWhiteSpace(text) &&
        (text.Contains(technician.Id, StringComparison.OrdinalIgnoreCase) || text.Contains(technician.Name, StringComparison.OrdinalIgnoreCase));

    private static bool Same(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Show(string? id) => string.IsNullOrWhiteSpace(id) ? "nobody" : id.Trim();

    private static string Local(DateTimeOffset utc) => LeakOpsTime.ToLocal(utc).ToString("ddd d MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
