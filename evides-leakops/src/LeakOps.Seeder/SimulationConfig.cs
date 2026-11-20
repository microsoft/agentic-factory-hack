namespace LeakOps.Seeder;

// Settings that only the seeder needs (they describe how to GENERATE data; they are never stored in Cosmos DB).

public record SimulationConfig(
    IReadOnlyList<DistrictWindow> Districts,
    IReadOnlyList<SensorSimulation> Sensors);

/// <summary>Time window of generated data for one district, plus its weather story.</summary>
public record DistrictWindow(
    string DistrictId,
    string Name,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,            // = the moment the SCADA alarm fires; no data exists after it
    double StartTempC,
    double EndTempC,
    double AvgWindKmh,
    IReadOnlyList<RainWindow> Rain);

public record RainWindow(DateTimeOffset FromUtc, DateTimeOffset ToUtc, double MmPerHour);

/// <summary>
/// Normal value = BaseValue + DemandSensitivity * demandFactor (0..1) + noise.
/// Pressure has a negative sensitivity (more demand, lower pressure); flow has a positive one.
/// Anomaly points replace the normal value from the first point onwards (linear interpolation between points).
/// </summary>
public record SensorSimulation(
    string SensorId,
    double BaseValue,
    double DemandSensitivity,
    double NoiseSd,
    IReadOnlyList<AnomalyPoint>? Anomaly);

public record AnomalyPoint(DateTimeOffset TimeUtc, double Value);
