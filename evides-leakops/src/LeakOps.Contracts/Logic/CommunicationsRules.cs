namespace LeakOps.Contracts.Logic;

public static class IncidentIds
{
    private static readonly System.Text.RegularExpressions.Regex ValidAlarmId = new(@"^ALM-[A-Za-z0-9._-]{1,55}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Alarm ids look like ALM-2026-1114-0001. Only that shape is accepted, because the incident id (and with it every reservation, order
    /// and work order) is derived from it: any other characters could make two different alarms end up with the same incident.
    /// </summary>
    public static bool IsValidAlarmId(string? alarmId) => alarmId is not null && ValidAlarmId.IsMatch(alarmId);

    /// <summary>The public incident id for an alarm: ALM-2026-1114-0001 becomes INC-2026-1114-0001. Every step of the workflow uses the same id.</summary>
    public static string For(string alarmId)
    {
        var id = alarmId.Trim();
        id = id.StartsWith("ALM-", StringComparison.OrdinalIgnoreCase) ? "INC-" + id[4..] : "INC-" + id;
        var safe = new string(id.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-').ToArray());
        return safe.Length <= 64 ? safe : safe[..64];
    }
}

/// <summary>
/// Hard rules for what may be published to residents. The model drafts the text; this code decides whether it may go out, and supplies
/// a safe template when the draft keeps failing. Residents must always get an accurate message, so publishing never depends on the model.
/// </summary>
public static class CommunicationsRules
{
    public const string HelpLine = "0900 0787";

    private const int MaxTitle = 90;
    private const int MaxMessage = 500;
    private const int MaxAdvice = 300;

    // (pattern, what it is). Anything matching is internal information, an invented promise, or blame, and must not be published.
    private static readonly (System.Text.RegularExpressions.Regex Pattern, string What)[] Forbidden =
    [
        (Rx(@"\b(SEG|PS|FS|WH|WO|PO|INC|ALM|EQP|CLP|VLV|GSK|BLT|DIS)-\d"), "an internal id"),
        (Rx(@"\bV-\d{4}\b|\bT-\d{2}\b"), "a valve or technician id"),
        (Rx(@"\bS[1-4]\b"), "a severity code"),
        (Rx(@"\bDN\s?\d{2,3}\b"), "a pipe diameter"),
        (Rx(@"gietijzer|cast[ -]?iron|asbest|ductile|nodulair|\bPVC\b"), "the pipe material"),
        (Rx(@"€|\bEUR\b|\beuro"), "a cost"),
        (Rx(@"【|†source"), "a citation mark from the knowledge base"),
        (Rx(@"\b\d{1,2}[:.]\d{2}\b|\b\d+\s*(uur|uren|hours?|minuten|minutes?)\b"), "a time or duration (no repair time is known yet)"),
        (Rx(@"schuld|nalatig|verwijtbaar|aansprakelijk|negligen|\bblame|\bliable\b|fault of"), "blame"),
        // Anything about whether the water is safe, contaminated or what to do about it is health advice. That is not for a model to write.
        (Rx(@"\bdrink(baar|en)?\b|veilig|\bsafe|\bkook|\bkoken|\bboil|besmet|contamin|vervuil|pollut|giftig|toxic|bacter|gezondheidsrisico|health risk|overstrom|flood|ziekte|disease"), "a health or safety claim"),
    ];

    private static System.Text.RegularExpressions.Regex Rx(string pattern) =>
        new(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>What the model is allowed to see: only facts that are meant for the public.</summary>
    public static object PublicFactsFor(DiagnosticReport report) => new
    {
        place = report.Place,
        streets = report.ServedStreets,
        postalCodes = report.PostalCodes,
        customersAffected = report.CustomersAffected,
        breachType = report.BreachType,
        vulnerableCustomersAffected = report.CriticalCustomers.Count > 0,
        status = WaterstoringStatus.Reported,
        helpLine = HelpLine,
    };

    /// <summary>Returns every reason the draft may not be published. An empty list means it passes.</summary>
    public static IReadOnlyList<string> Check(PublicNotice notice, DiagnosticReport report)
    {
        var issues = new List<string>();

        void Field(string name, string? value, int max, bool required = true)
        {
            if (string.IsNullOrWhiteSpace(value)) { if (required) issues.Add($"{name} is empty."); }
            else if (value.Length > max) issues.Add($"{name} is too long ({value.Length} characters, the maximum is {max}).");
        }
        Field("titleNl", notice.TitleNl, MaxTitle);
        Field("messageNl", notice.MessageNl, MaxMessage);
        Field("adviceNl", notice.AdviceNl, MaxAdvice, required: false);   // the advice is always the standard advice below, whatever the model writes
        Field("titleEn", notice.TitleEn, MaxTitle);
        Field("messageEn", notice.MessageEn, MaxMessage);
        Field("adviceEn", notice.AdviceEn, MaxAdvice, required: false);

        var text = string.Join("\n", new[] { notice.TitleNl, notice.MessageNl, notice.AdviceNl, notice.TitleEn, notice.MessageEn, notice.AdviceEn }.Select(t => t ?? ""));
        foreach (var (pattern, what) in Forbidden)
            if (pattern.IsMatch(text)) issues.Add($"The text contains {what}, which must not be published.");

        foreach (var customer in report.CriticalCustomers)
        {
            var name = customer.Split('(')[0].Trim();
            if (name.Length > 3 && text.Contains(name, StringComparison.OrdinalIgnoreCase))
                issues.Add("The text names a specific customer. Say only that vulnerable customers are taken into account, without names.");
        }

        var places = report.ServedStreets.Append(report.Place).ToList();
        bool Mentions(string? t) => places.Any(p => (t ?? "").Contains(p, StringComparison.OrdinalIgnoreCase));
        if (!Mentions(notice.TitleNl + " " + notice.MessageNl)) issues.Add("The Dutch title and message name neither the place nor an affected street.");
        if (!Mentions(notice.TitleEn + " " + notice.MessageEn)) issues.Add("The English title and message name neither the place nor an affected street.");

        if (!string.IsNullOrWhiteSpace(notice.MessageNl) && string.Equals(notice.MessageNl.Trim(), notice.MessageEn?.Trim(), StringComparison.OrdinalIgnoreCase))
            issues.Add("The English message is a copy of the Dutch one.");

        return issues;
    }

    /// <summary>What the drafting loop decided: the notice to publish, whether it is the safe template, and why earlier drafts were rejected.</summary>
    public record NoticeDecision(PublicNotice Notice, bool UsedFallback, IReadOnlyList<string> RejectedIssues, int Attempts);

    /// <summary>
    /// Asks for a draft (the model, in production), checks it, and asks again with the reasons when it is rejected. After
    /// <paramref name="maxAttempts"/> rejected drafts the safe template is used, so residents always get an accurate message.
    /// <paramref name="draft"/> receives feedback text for the model (empty on the first attempt).
    /// </summary>
    public static async Task<NoticeDecision> ChooseNoticeAsync(DiagnosticReport report, Func<string, Task<PublicNotice>> draft, int maxAttempts = 2)
    {
        var rejected = new List<string>();
        var feedback = "";

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var notice = await draft(feedback);
            var issues = Check(notice, report);
            if (issues.Count == 0) return new NoticeDecision(WithStandardAdvice(notice, report), false, rejected, attempt);

            rejected.AddRange(issues);
            feedback = $"Your previous draft was rejected for these reasons: {string.Join(" ", issues)} Write a new draft that fixes all of them.";
        }

        return new NoticeDecision(Fallback(report), true, rejected, maxAttempts);
    }

    /// <summary>The advice residents get is written here, never by the model: what is true in every case, and the help line.</summary>
    public static PublicNotice WithStandardAdvice(PublicNotice notice, DiagnosticReport report)
    {
        var (nl, en) = StandardAdvice(report);
        return notice with { AdviceNl = nl, AdviceEn = en };
    }

    private static (string Dutch, string English) StandardAdvice(DiagnosticReport report)
    {
        var vulnerableNl = report.CriticalCustomers.Count > 0
            ? " Heeft u medische apparatuur of een zorgvraag waarvoor u water nodig heeft? Bel dan direct."
            : "";
        var vulnerableEn = report.CriticalCustomers.Count > 0
            ? " Do you rely on medical equipment or care that needs water? Please call right away."
            : "";
        return ($"Na herstel kan het water tijdelijk troebel zijn: laat de kraan dan even doorlopen. Voor vragen kunt u Evides bellen: {HelpLine} (optie 2).{vulnerableNl}",
            $"After the repair the water may be cloudy for a while: let the tap run for a moment. For questions you can call Evides: {HelpLine} (option 2).{vulnerableEn}");
    }

    /// <summary>A plain, accurate notice built from the data alone. Used when the model's draft is rejected twice.</summary>
    public static PublicNotice Fallback(DiagnosticReport report)
    {
        var street = report.ServedStreets.FirstOrDefault() ?? report.Street;
        var streetsNl = string.Join(", ", report.ServedStreets);
        var (adviceNl, adviceEn) = StandardAdvice(report);

        return new PublicNotice(
            TitleNl: $"Storing waterleiding {street}, {report.Place}",
            MessageNl: $"Door een storing aan de waterleiding hebben bewoners en bedrijven in een deel van {report.Place} geen of weinig water. Het gaat om ongeveer {report.CustomersAffected} aansluitingen, onder meer rond {streetsNl}. We onderzoeken de storing en houden deze pagina bijgewerkt.",
            AdviceNl: adviceNl,
            TitleEn: $"Water main disruption {street}, {report.Place}",
            MessageEn: $"A fault in the water main means residents and businesses in part of {report.Place} have little or no water. About {report.CustomersAffected} connections are affected, including around {streetsNl}. We are investigating and will keep this page up to date.",
            AdviceEn: adviceEn);
    }

    /// <summary>The payload for the (fake) Waterstoring ingest API. Every fact comes from the diagnosis; only the wording comes from the notice.</summary>
    public static WaterstoringIncidentRequest BuildRequest(PublicNotice notice, DiagnosticReport report) => new(
        IncidentId: IncidentIds.For(report.AlarmId),
        Title: notice.TitleNl.Trim(),
        Message: notice.MessageNl.Trim(),
        TitleEn: notice.TitleEn.Trim(),
        MessageEn: notice.MessageEn.Trim(),
        Status: WaterstoringStatus.Reported,
        Place: report.Place,
        AffectedStreets: report.ServedStreets,
        AffectedPostalCodes: report.PostalCodes,
        Location: report.Location,
        AffectedPath: report.SegmentPath,
        CustomersAffected: report.CustomersAffected,
        ExpectedResolutionUtc: null,     // unknown until the work is scheduled; the Scheduling agent adds it later
        Advice: notice.AdviceNl.Trim(),
        AdviceEn: notice.AdviceEn.Trim());
}
