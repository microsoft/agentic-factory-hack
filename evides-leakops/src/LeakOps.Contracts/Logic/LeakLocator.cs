namespace LeakOps.Contracts.Logic;

/// <summary>How far one pressure sensor fell between two moments, and where it sits along the pipe segment.</summary>
public record SensorDrop(string SensorId, double ChainageM, double FromBar, double ToBar)
{
    public double DropBar => FromBar - ToBar;
}

public record LeakEstimate(double ChainageM, double UncertaintyM, string Method, IReadOnlyList<SensorDrop> SensorsUsed);

/// <summary>
/// Estimates where along a pipe segment a leak is, from the pressure drops at the sensors on that segment (pressure-gradient method):
/// the sensor closest to the break drops most, so the break lies between the two outer sensors, nearer the one with the larger drop.
/// It is a heuristic: it gives a search area for the crew, not an exact point.
/// </summary>
public static class LeakLocator
{
    public const double MinimumDropBar = 0.3;

    public static LeakEstimate? Estimate(IReadOnlyList<SensorDrop> drops, double segmentLengthM)
    {
        var fell = drops.Where(d => d.DropBar >= MinimumDropBar).OrderBy(d => d.ChainageM).ToList();
        if (fell.Count == 0) return null;

        if (fell.Count == 1)
            return new LeakEstimate(
                fell[0].ChainageM,
                Math.Round(segmentLengthM / 2),
                $"Only {fell[0].SensorId} saw a pressure drop, so the leak is on this segment but its position along it is unknown.",
                fell);

        var first = fell[0];
        var last = fell[^1];
        var spacing = last.ChainageM - first.ChainageM;
        var shareFromFirst = last.DropBar / (first.DropBar + last.DropBar);   // the bigger the drop at the far sensor, the closer to the first
        var chainage = Math.Clamp(first.ChainageM + spacing * shareFromFirst, 0, segmentLengthM);

        return new LeakEstimate(
            Math.Round(chainage),
            Math.Round(Math.Max(20, 0.15 * spacing)),
            $"Pressure gradient between {first.SensorId} (fell {first.DropBar:F2} bar) and {last.SensorId} (fell {last.DropBar:F2} bar): the break lies between them, nearer the sensor with the larger drop.",
            fell);
    }
}
