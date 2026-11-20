namespace LeakOps.Contracts.Logic;

public static class Geo
{
    /// <summary>Haversine: great-circle distance between two coordinates in metres.</summary>
    public static double Metres(GeoPoint a, GeoPoint b)
    {
        const double earthRadiusM = 6_371_000;
        static double Rad(double degrees) => degrees * Math.PI / 180;
        var dLat = Rad(b.Lat - a.Lat);
        var dLon = Rad(b.Lon - a.Lon);
        var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Rad(a.Lat)) * Math.Cos(Rad(b.Lat)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * earthRadiusM * Math.Asin(Math.Sqrt(h));
    }

    /// <summary>The coordinate at a distance (chainage) along a segment, assuming a straight pipe between Start and End.</summary>
    public static GeoPoint PointAt(PipeSegment segment, double chainageM)
    {
        var t = Math.Clamp(chainageM / segment.LengthM, 0, 1);
        return new GeoPoint(
            segment.Start.Lat + (segment.End.Lat - segment.Start.Lat) * t,
            segment.Start.Lon + (segment.End.Lon - segment.Start.Lon) * t);
    }
}
