namespace LeakOps.Contracts.Logic;

/// <summary>
/// Deterministic checks on the Triage agent's answer. The model proposes, this code disposes: identity fields come from the
/// alarm, values are kept in range, the next step follows the verdict, and a verdict without enough evidence becomes "uncertain".
/// </summary>
public static class TriageRules
{
    public const string GenuineLeak = "genuine_leak";
    public const string FalseAlarm = "false_alarm";
    public const string Uncertain = "uncertain";

    private static readonly string[] Supports = ["leak", "no_leak", "neutral"];

    /// <summary>
    /// True when the text names the sensor. Models like to write typographic hyphens ("PS-201" with U+2011 or an en dash)
    /// and sometimes put a space instead, so those are treated as the plain hyphen before comparing.
    /// </summary>
    private static bool Mentions(string text, string sensorId)
    {
        static string Canonical(string value) =>
            new string(value.Select(c => c is '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2212' or ' ' or '\u00A0' ? '-' : c).ToArray())
                .ToUpperInvariant();
        return Canonical(text).Contains(Canonical(sensorId), StringComparison.Ordinal);
    }

    /// <summary>True when the finding quotes a reading with its unit, for example "3.72 bar" or "112 m3/h". "PS-999 does not exist" does not count.</summary>
    private static bool HasReading(string text) =>
        System.Text.RegularExpressions.Regex.IsMatch(text, @"\d\s*(bar|m3/h|m³/h)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static TriageVerdict Normalize(TriageVerdict verdict, AnomalyAlarm alarm)
    {
        var evidence = (verdict.Evidence ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Finding))
            .Select(e => new EvidenceItem(e.Source?.Trim() ?? "", e.Finding.Trim(), Supports.Contains(e.Supports) ? e.Supports : "neutral"))
            .ToList();

        var confidence = double.IsNaN(verdict.Confidence) ? 0 : Math.Clamp(verdict.Confidence, 0, 1);
        var summary = verdict.Summary?.Trim() ?? "";
        var result = verdict.Verdict is GenuineLeak or FalseAlarm or Uncertain ? verdict.Verdict : Uncertain;

        string? downgrade = null;
        if (result == GenuineLeak && !evidence.Any(e => e.Supports == "leak")) downgrade = "no evidence points to a leak";
        else if (result == FalseAlarm && !evidence.Any(e => e.Supports == "no_leak")) downgrade = "no evidence explains the alarm";
        else if (result != Uncertain && evidence.Count < 2) downgrade = "fewer than two pieces of evidence";
        else if (result != Uncertain && !evidence.Any(e => Mentions(e.Finding, alarm.SensorId) && HasReading(e.Finding)))
            downgrade = $"the readings of the alarm sensor {alarm.SensorId} are not in the evidence";

        if (downgrade is not null)
        {
            result = Uncertain;
            confidence = Math.Min(confidence, 0.4);
            summary = $"Downgraded to uncertain ({downgrade}). {summary}".Trim();
        }

        var segment = string.IsNullOrWhiteSpace(verdict.SuspectedSegmentId) ? null : verdict.SuspectedSegmentId.Trim();
        return new TriageVerdict(
            AlarmId: alarm.AlarmId,
            DistrictId: alarm.DistrictId,
            SensorId: alarm.SensorId,
            TriggeredAtUtc: LeakOpsTime.Utc(alarm.TriggeredAtUtc),
            Verdict: result,
            Confidence: Math.Round(confidence, 2),
            Summary: summary,
            Evidence: evidence,
            SuspectedSegmentId: result == GenuineLeak ? segment : null,
            NextStep: result switch { GenuineLeak => "diagnose", FalseAlarm => "close_alarm", _ => "manual_review" });
    }

    /// <summary>
    /// Closing a real leak is the most expensive mistake, so the model alone can never close an alarm: a false alarm needs a documented
    /// explanation that code fetched from the planned-works register. Without a planned work that is active in the district at the
    /// alarm time, the verdict becomes "uncertain" and a person looks at it.
    /// </summary>
    public static TriageVerdict RequireDocumentedExplanation(TriageVerdict verdict, IReadOnlyList<PlannedWorkFact> plannedWorks)
    {
        if (verdict.Verdict != FalseAlarm) return verdict;
        if (plannedWorks.Any(w => w.ActiveAtTime && !w.Status.Equals("completed", StringComparison.OrdinalIgnoreCase))) return verdict;

        return verdict with
        {
            Verdict = Uncertain,
            Confidence = Math.Min(verdict.Confidence, 0.4),
            SuspectedSegmentId = null,
            NextStep = "manual_review",
            Summary = $"Not closed: a false alarm needs a documented explanation, and no planned work is active in {verdict.DistrictId} at the alarm time. {verdict.Summary}".Trim(),
        };
    }
}

/// <summary>A planned work as returned by the get-planned-works tool.</summary>
public record PlannedWorkFact(string Id, string Status, bool ActiveAtTime);
