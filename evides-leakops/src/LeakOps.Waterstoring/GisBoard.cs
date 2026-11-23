using System.Text.RegularExpressions;
using LeakOps.Contracts;
using LeakOps.Contracts.Logic;

namespace LeakOps.Waterstoring;

public record GisValve(
    string Id, string DistrictId, string SegmentId, double ChainageM, GeoPoint Location, string Type, int DiameterMm,
    string Status, int TurnsToClose, DateTimeOffset? LastOperatedUtc, bool MayBeStiff);

public record GisNetwork(IReadOnlyList<PipeSegment> Pipes, IReadOnlyList<GisValve> Valves, IReadOnlyList<Sensor> Sensors);

public record GisIsolationStep(int Order, string ValveId, string Action, string Status, int TurnsToClose, GeoPoint Location, DateTimeOffset? LastOperatedUtc, bool MayBeStiff);

public record GisBackupValve(string ValveId, string SegmentId, string Status, GeoPoint Location);

public record GisIncident(
    string IncidentId, string AlarmId, string DistrictId, DateTimeOffset TriggeredAtUtc, DateTimeOffset DiagnosedAtUtc,
    string SegmentId, string Street, string Place, string Material, int DiameterMm, int InstallYear,
    double ChainageM, double UncertaintyM,
    GeoPoint Location,                         // the chainage turned into a coordinate on the segment
    IReadOnlyList<GeoPoint> UncertainStretch,  // the part of the pipe where the break can be: chainage ± uncertainty
    IReadOnlyList<GeoPoint> SegmentPath,
    string BreachType, string Severity, IReadOnlyList<string> SeverityReasons,
    int CustomersAffected, IReadOnlyList<string> CriticalCustomers, IReadOnlyList<string> PostalCodes, IReadOnlyList<string> ServedStreets,
    IReadOnlyList<GisIsolationStep> IsolationPlan, IReadOnlyList<GisBackupValve> BackupValves,
    string Summary, IReadOnlyList<string> Warnings, bool NeedsReview, IReadOnlyList<string> ReviewReasons);

public record GisBoardResult(DateTimeOffset AtUtc, bool Simulated, GisNetwork Network, GisIncident? Incident);

/// <summary>
/// The simulated network map: pipes, valves and sensors always; once the Diagnostic agent has run, also the latest incident.
/// The leak coordinate is computed with <see cref="Geo.PointAt"/> (the code behind the get-point-on-segment tool) and the stiff-valve
/// check with <see cref="DiagnosticRules.MayBeStiff"/>, so this page and the agents cannot disagree. It never reads scenarios.json.
/// </summary>
public static partial class GisBoard
{
    public static GisBoardResult Build(IEnumerable<PipeSegment> segments, IEnumerable<Valve> valves, IEnumerable<Sensor> sensors,
        IEnumerable<GisDiagnosis> diagnoses, DateTimeOffset atUtc)
    {
        var pipes = segments.OrderBy(s => s.DistrictId, StringComparer.Ordinal).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
        var valveList = valves.OrderBy(v => v.Id, StringComparer.Ordinal).ToList();
        var network = new GisNetwork(
            pipes,
            [.. valveList.Select(v => new GisValve(v.Id, v.DistrictId, v.SegmentId, v.ChainageM, v.Location, v.Type, v.DiameterMm, v.Status,
                v.TurnsToClose, v.LastOperatedUtc, DiagnosticRules.MayBeStiff(v.LastOperatedUtc, atUtc)))],
            [.. sensors.OrderBy(s => s.Id, StringComparer.Ordinal)]);

        var latest = diagnoses.OrderByDescending(d => d.ReceivedUtc).FirstOrDefault();
        return new GisBoardResult(atUtc, true, network, latest is null ? null : Incident(latest, pipes, valveList));
    }

    private static GisIncident Incident(GisDiagnosis diagnosis, IReadOnlyList<PipeSegment> pipes, IReadOnlyList<Valve> valves)
    {
        var r = diagnosis.Report;
        var segment = pipes.FirstOrDefault(s => s.Id == r.SegmentId);
        var byId = valves.ToDictionary(v => v.Id);

        // Without the segment in the network (should not happen) the map falls back to what the report says.
        var location = segment is null ? r.Location : Geo.PointAt(segment, r.ChainageM);
        IReadOnlyList<GeoPoint> stretch = segment is null
            ? [r.Location]
            : [Geo.PointAt(segment, r.ChainageM - r.UncertaintyM), Geo.PointAt(segment, r.ChainageM + r.UncertaintyM)];
        IReadOnlyList<GeoPoint> path = segment is null ? r.SegmentPath : [segment.Start, segment.End];

        var plan = r.IsolationPlan.OrderBy(s => s.Order).Select(s =>
        {
            var valve = byId.GetValueOrDefault(s.ValveId);
            var lastOperated = valve?.LastOperatedUtc ?? s.LastOperatedUtc;
            return new GisIsolationStep(s.Order, s.ValveId, s.Action, s.Status, s.TurnsToClose, valve?.Location ?? s.Location, lastOperated,
                DiagnosticRules.MayBeStiff(lastOperated, r.TriggeredAtUtc));
        }).ToList();

        var inPlan = plan.Select(s => s.ValveId).ToHashSet();
        var backups = BackupValveIds(r)
            .Where(id => !inPlan.Contains(id) && byId.ContainsKey(id))
            .Select(id => byId[id])
            .Select(v => new GisBackupValve(v.Id, v.SegmentId, v.Status, v.Location))
            .ToList();

        return new GisIncident(
            IncidentIds.For(r.AlarmId), r.AlarmId, r.DistrictId, r.TriggeredAtUtc, diagnosis.ReceivedUtc,
            r.SegmentId, SchedulingRules.StreetName(r.Street), r.Place, r.Material, r.DiameterMm, r.InstallYear,
            r.ChainageM, r.UncertaintyM, location, stretch, path,
            r.BreachType, r.Severity, r.SeverityReasons,
            r.CustomersAffected, r.CriticalCustomers, r.PostalCodes, r.ServedStreets,
            plan, backups, r.Summary, r.Warnings, r.NeedsReview, r.ReviewReasons);
    }

    /// <summary>Valve ids named in a sentence about a backup (or reserve) valve, in the summary, warnings or evidence of the report.</summary>
    private static IEnumerable<string> BackupValveIds(DiagnosticReport r) =>
        new[] { r.Summary }.Concat(r.Warnings).Concat(r.Evidence.Select(e => e.Finding))
            .Where(text => !string.IsNullOrEmpty(text))
            .SelectMany(text => SentenceSplit().Split(text))
            .Where(sentence => BackupWord().IsMatch(sentence))
            .SelectMany(sentence => ValveId().Matches(sentence).Select(m => m.Value))
            .Distinct();

    [GeneratedRegex(@"(?<=[.;!?])\s+")]
    private static partial Regex SentenceSplit();

    [GeneratedRegex(@"\b(backup|back-up|reserve)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BackupWord();

    [GeneratedRegex(@"\bV-\d{4}-[A-Z]\b")]
    private static partial Regex ValveId();
}
