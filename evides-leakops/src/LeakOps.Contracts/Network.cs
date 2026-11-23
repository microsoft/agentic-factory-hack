namespace LeakOps.Contracts;

// C# note: a "record" is an immutable data class (like a Python frozen dataclass). The positional parameters become
// properties; Cosmos DB and System.Text.Json/Newtonsoft fill them through the constructor.
// All timestamps are UTC. In November the Netherlands is UTC+1 (CET).

public record GeoPoint(double Lat, double Lon);

/// <summary>A SCADA/IoT measuring point (pressure logger or flow meter) on a pipe segment.</summary>
public record Sensor(
    string Id,
    string Name,
    string DistrictId,
    string SegmentId,
    double ChainageM,          // distance along the segment from its Start point
    GeoPoint Location,
    string Metric,             // "pressure" | "flow"
    string Unit,               // "bar" | "m3/h"
    double? AlarmLow,
    double? AlarmHigh,
    int SamplingMinutes,
    string Status);

public record SensorReading(
    string Id,
    string SensorId,
    string DistrictId,
    DateTimeOffset TimestampUtc,
    double Value,
    string Unit,
    string Quality);

/// <summary>A GIS pipe segment between two nodes of the distribution network.</summary>
public record PipeSegment(
    string Id,
    string DistrictId,
    string Street,
    string Place,                      // town or village, shown to residents
    IReadOnlyList<string> ServedStreets,   // public street names the segment supplies, for residents
    string Material,           // cast_iron | ductile_iron | pvc | pe | asbestos_cement | steel
    int DiameterMm,
    int InstallYear,
    double DepthM,
    double LengthM,
    string PressureClass,
    GeoPoint Start,
    GeoPoint End,
    int CustomersServed,
    IReadOnlyList<string> CriticalCustomers,
    IReadOnlyList<string> PostalCodes,
    IReadOnlyList<string> IsolationValveIds,
    int PreviousBursts,
    string Surface,            // asphalt | pavement | grass ...
    string RoadType);          // arterial | local | industrial ...

public record Valve(
    string Id,
    string DistrictId,
    string SegmentId,
    double ChainageM,
    GeoPoint Location,
    string Type,
    int DiameterMm,
    string Status,             // open | closed | seized
    int TurnsToClose,          // hand-wheel turns: drives how long isolation takes
    DateTimeOffset? LastOperatedUtc);
