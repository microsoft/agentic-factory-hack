namespace LeakOps.Contracts.Logic;

public record TravelLeg(double DistanceKm, int Minutes, string Traffic, double AvgSpeedKmh);

/// <summary>A simple, explainable travel model (no real routing service): road distance = 1.3 x straight line, speed depends on the time of day.</summary>
public static class TravelEstimator
{
    private const double RoadFactor = 1.3;

    public static TravelLeg Estimate(GeoPoint from, GeoPoint to, DateTimeOffset departUtc)
    {
        var local = LeakOpsTime.ToLocal(departUtc);
        var hour = local.Hour + local.Minute / 60.0;
        var weekday = LeakOpsTime.DayType(departUtc) == "weekday";

        var (traffic, speed) = weekday && (hour is >= 7 and < 9.5 || hour is >= 16 and < 18.5) ? ("rush hour", 35.0)
            : hour >= 22 || hour < 5.5 ? ("night, quiet roads", 70.0)
            : ("normal traffic", 55.0);

        var roadKm = Geo.Metres(from, to) / 1000.0 * RoadFactor;
        return new TravelLeg(Math.Round(roadKm, 1), (int)Math.Ceiling(roadKm / speed * 60), traffic, speed);
    }
}
