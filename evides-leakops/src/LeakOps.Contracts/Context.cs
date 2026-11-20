namespace LeakOps.Contracts;

/// <summary>Planned maintenance that can legitimately change pressure/flow (valve work, flushing, renewals).</summary>
public record PlannedWork(
    string Id,
    string DistrictId,
    string Title,
    string WorkType,
    string Status,             // scheduled | in_progress | completed
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    IReadOnlyList<string> SegmentIds,
    double? ExpectedPressureImpactBar,
    string ExpectedFlowImpact,
    string Contractor,
    bool CustomersNotified);

public record WeatherObservation(
    string Id,
    string DistrictId,
    DateTimeOffset TimestampUtc,
    double TemperatureC,
    double PrecipitationMm,
    double WindKmh,
    bool Frost,
    string Source);

/// <summary>Typical demand for one district, day type and local hour (what "normal" looks like).</summary>
public record UsageProfile(
    string Id,
    string DistrictId,
    string DayType,            // weekday | weekend
    int LocalHour,
    double AvgFlowM3h,
    double P95FlowM3h,
    double AvgPressureBar);

/// <summary>A known historical deviation (heatwave, event, flushing) that is not a leak.</summary>
public record UsageEvent(
    string Id,
    string DistrictId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string Description,
    double FlowChangePercent,
    double? PressureChangeBar);
