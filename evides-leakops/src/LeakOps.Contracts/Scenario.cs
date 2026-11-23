namespace LeakOps.Contracts;

/// <summary>The SCADA alarm that starts the workflow (the payload the operator page sends to the Triage agent).</summary>
public record AnomalyAlarm(
    string AlarmId,
    string SensorId,
    string DistrictId,
    DateTimeOffset TriggeredAtUtc,
    string Metric,
    double ObservedValue,
    string Unit,
    double? ThresholdLow,
    double? ThresholdHigh,
    string Message);

/// <summary>What really happened in the simulation. Used for tests and evaluation only - never exposed to the agents.</summary>
public record GroundTruth(
    string Outcome,                  // genuine_leak | false_alarm
    string? SegmentId,
    double? ChainageM,
    string? BreachType,
    string Explanation);

public record Scenario(
    string Id,
    string Name,
    string Description,
    AnomalyAlarm Alarm,
    GroundTruth GroundTruth);
