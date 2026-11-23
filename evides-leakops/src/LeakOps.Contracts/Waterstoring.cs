namespace LeakOps.Contracts;

/// <summary>Public status values shown on the (fake) Waterstoring site.</summary>
public static class WaterstoringStatus
{
    public const string Reported = "reported";                 // Melding ontvangen
    public const string RepairPlanned = "repair_planned";      // Reparatie gepland
    public const string RepairInProgress = "repair_in_progress"; // Reparatie bezig
    public const string Resolved = "resolved";                 // Opgelost

    public static readonly IReadOnlyList<string> All = [Reported, RepairPlanned, RepairInProgress, Resolved];

    public static bool IsValid(string? status) => status is not null && All.Contains(status);
    public static bool IsActive(string status) => status != Resolved;
}

/// <summary>Body of POST /api/incidents. Sent by the Communications agent. Sending the same IncidentId again replaces the incident.</summary>
public record WaterstoringIncidentRequest(
    string IncidentId,
    string Title,                              // public headline, Dutch
    string Message,                            // public text, Dutch
    string? TitleEn,
    string? MessageEn,
    string Status,                             // see WaterstoringStatus
    string Place,
    IReadOnlyList<string> AffectedStreets,
    IReadOnlyList<string> AffectedPostalCodes,
    GeoPoint Location,
    IReadOnlyList<GeoPoint>? AffectedPath,     // drawn as a line on the map
    int CustomersAffected,
    DateTimeOffset? ExpectedResolutionUtc,
    string? Advice,
    string? AdviceEn);

/// <summary>Body of PATCH /api/incidents/{id}. Only the fields that are set change. An expected resolution time that has become unknown is withdrawn with ClearExpectedResolution.</summary>
public record WaterstoringIncidentUpdate(
    string? Status,
    string? Message,
    string? MessageEn,
    DateTimeOffset? ExpectedResolutionUtc,
    bool? ClearExpectedResolution = null);

public record WaterstoringTimelineEntry(
    DateTimeOffset AtUtc,
    string Status,
    string Message,
    string? MessageEn,
    DateTimeOffset? ExpectedResolutionUtc);

/// <summary>An incident as published on the site.</summary>
public record WaterstoringIncident(
    string IncidentId,
    string Title,
    string Message,
    string? TitleEn,
    string? MessageEn,
    string Status,
    string Place,
    IReadOnlyList<string> AffectedStreets,
    IReadOnlyList<string> AffectedPostalCodes,
    GeoPoint Location,
    IReadOnlyList<GeoPoint>? AffectedPath,
    int CustomersAffected,
    DateTimeOffset? ExpectedResolutionUtc,
    string? Advice,
    string? AdviceEn,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    IReadOnlyList<WaterstoringTimelineEntry> Timeline);
