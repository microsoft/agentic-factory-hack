using LeakOps.Contracts;
using LeakOps.Contracts.Logic;

namespace LeakOps.Seeder;

/// <summary>Deterministic generator for sensor readings, weather and usage profiles (same input = same output).</summary>
public static class SimulatedDataGenerator
{
    public const int ReadingIntervalMinutes = 10;

    // Demand factor (0..1) per LOCAL hour of the day.
    private static readonly double[] Weekday =
        [0.32, 0.28, 0.26, 0.27, 0.35, 0.55, 0.85, 1.00, 0.95, 0.80, 0.70, 0.68, 0.72, 0.70, 0.65, 0.66, 0.72, 0.82, 0.90, 0.85, 0.70, 0.55, 0.42, 0.36];
    private static readonly double[] Weekend =
        [0.34, 0.30, 0.27, 0.27, 0.30, 0.38, 0.50, 0.70, 0.90, 1.00, 0.95, 0.85, 0.80, 0.75, 0.70, 0.70, 0.75, 0.85, 0.90, 0.85, 0.72, 0.60, 0.50, 0.40];

    public static double[] Profile(string dayType) => dayType == "weekend" ? Weekend : Weekday;

    /// <summary>Demand factor at a moment in time, linearly interpolated between the hourly values.</summary>
    public static double DemandFactor(DateTimeOffset utc)
    {
        var local = LeakOpsTime.ToLocal(utc);
        var profile = Profile(LeakOpsTime.DayType(utc));
        var hour = local.Hour;
        var fraction = local.Minute / 60.0;
        return profile[hour] + (profile[(hour + 1) % 24] - profile[hour]) * fraction;
    }

    public static double NormalValue(SensorSimulation sim, DateTimeOffset utc) =>
        sim.BaseValue + sim.DemandSensitivity * DemandFactor(utc);

    public static List<SensorReading> GenerateReadings(IReadOnlyList<Sensor> sensors, SimulationConfig config)
    {
        var readings = new List<SensorReading>();
        foreach (var sim in config.Sensors)
        {
            var sensor = sensors.Single(s => s.Id == sim.SensorId);
            var window = config.Districts.Single(d => d.DistrictId == sensor.DistrictId);
            var rng = new Random(StableHash(sim.SensorId));   // seeded: the same sensor always gets the same noise
            var points = (sim.Anomaly ?? []).OrderBy(p => p.TimeUtc).ToList();

            for (var t = window.StartUtc; t <= window.EndUtc; t = t.AddMinutes(ReadingIntervalMinutes))
            {
                var noise = (rng.NextDouble() - 0.5) * 2 * sim.NoiseSd;   // always drawn, so overrides do not shift later noise
                var value = points.Count > 0 && t >= points[0].TimeUtc
                    ? Interpolate(points, t)
                    : NormalValue(sim, t) + noise;

                readings.Add(new SensorReading(
                    Id: $"{sensor.Id}-{t:yyyyMMddTHHmm}",
                    SensorId: sensor.Id,
                    DistrictId: sensor.DistrictId,
                    TimestampUtc: t,
                    Value: Math.Round(value, sensor.Metric == "flow" ? 1 : 2),
                    Unit: sensor.Unit,
                    Quality: "good"));
            }
        }
        return readings;
    }

    public static List<WeatherObservation> GenerateWeather(SimulationConfig config)
    {
        var observations = new List<WeatherObservation>();
        foreach (var window in config.Districts)
        {
            var rng = new Random(StableHash(window.DistrictId));
            var first = new DateTimeOffset(window.StartUtc.Year, window.StartUtc.Month, window.StartUtc.Day, window.StartUtc.Hour, 0, 0, TimeSpan.Zero);
            if (first < window.StartUtc) first = first.AddHours(1);

            for (var t = first; t <= window.EndUtc; t = t.AddHours(1))
            {
                var progress = (t - window.StartUtc) / (window.EndUtc - window.StartUtc);
                var localHour = LeakOpsTime.ToLocal(t).Hour;
                var temperature = window.StartTempC + (window.EndTempC - window.StartTempC) * progress
                                  + 1.2 * Math.Sin(2 * Math.PI * (localHour - 9) / 24.0);
                var rain = window.Rain.Where(r => t >= r.FromUtc && t < r.ToUtc).Sum(r => r.MmPerHour);
                var wind = window.AvgWindKmh + (rng.NextDouble() - 0.5) * 6;

                observations.Add(new WeatherObservation(
                    Id: $"{window.DistrictId}-{t:yyyyMMddTHH}",
                    DistrictId: window.DistrictId,
                    TimestampUtc: t,
                    TemperatureC: Math.Round(temperature, 1),
                    PrecipitationMm: Math.Round(rain, 1),
                    WindKmh: Math.Round(wind, 0),
                    Frost: temperature < 0,
                    Source: "simulated weather station"));
            }
        }
        return observations;
    }

    public static List<UsageProfile> GenerateUsageProfiles(IReadOnlyList<Sensor> sensors, SimulationConfig config)
    {
        var profiles = new List<UsageProfile>();
        foreach (var window in config.Districts)
        {
            // Flow comes from the district inlet meter, pressure from the first pressure logger of the district.
            var flow = sensors.Single(s => s.DistrictId == window.DistrictId && s.Metric == "flow");
            var pressure = sensors.Where(s => s.DistrictId == window.DistrictId && s.Metric == "pressure").OrderBy(s => s.Id).First();
            var flowSim = config.Sensors.Single(s => s.SensorId == flow.Id);
            var pressureSim = config.Sensors.Single(s => s.SensorId == pressure.Id);

            foreach (var dayType in new[] { "weekday", "weekend" })
            {
                var profile = Profile(dayType);
                for (var hour = 0; hour < 24; hour++)
                {
                    var avgFlow = flowSim.BaseValue + flowSim.DemandSensitivity * profile[hour];
                    profiles.Add(new UsageProfile(
                        Id: $"{window.DistrictId}-{dayType}-{hour:00}",
                        DistrictId: window.DistrictId,
                        DayType: dayType,
                        LocalHour: hour,
                        AvgFlowM3h: Math.Round(avgFlow, 1),
                        P95FlowM3h: Math.Round(avgFlow * 1.18, 1),
                        AvgPressureBar: Math.Round(pressureSim.BaseValue + pressureSim.DemandSensitivity * profile[hour], 2)));
                }
            }
        }
        return profiles;
    }

    private static double Interpolate(List<AnomalyPoint> points, DateTimeOffset t)
    {
        if (t >= points[^1].TimeUtc) return points[^1].Value;
        for (var i = 0; i < points.Count - 1; i++)
        {
            if (t >= points[i].TimeUtc && t <= points[i + 1].TimeUtc)
            {
                var span = (points[i + 1].TimeUtc - points[i].TimeUtc).TotalSeconds;
                var fraction = span == 0 ? 0 : (t - points[i].TimeUtc).TotalSeconds / span;
                return points[i].Value + (points[i + 1].Value - points[i].Value) * fraction;
            }
        }
        return points[0].Value;
    }

    // string.GetHashCode() changes on every run in .NET, so use a fixed hash (FNV-1a) for repeatable noise.
    private static int StableHash(string text)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in text) hash = (hash ^ c) * 16777619;
            return hash;
        }
    }
}
