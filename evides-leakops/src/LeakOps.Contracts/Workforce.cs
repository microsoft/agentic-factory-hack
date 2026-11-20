namespace LeakOps.Contracts;

public record Shift(DateTimeOffset StartUtc, DateTimeOffset EndUtc, string Type);   // Type: day | night_on_call

/// <summary>A job the technician is already doing (blocks availability).</summary>
public record BusyBlock(DateTimeOffset StartUtc, DateTimeOffset EndUtc, string WorkOrderId, string Description);

public record Vehicle(string Type, string Plate, bool CanTowExcavator);

/// <summary>Field technician with roster and certification matrix.</summary>
public record Technician(
    string Id,
    string Name,
    string Region,
    IReadOnlyList<string> Certifications,
    IReadOnlyList<Shift> Shifts,
    IReadOnlyList<BusyBlock> BusyBlocks,
    GeoPoint Location,              // last known position
    DateTimeOffset LocationAsOfUtc,
    Vehicle Vehicle);
