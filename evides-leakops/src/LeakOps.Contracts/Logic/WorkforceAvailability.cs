namespace LeakOps.Contracts.Logic;

public record TechnicianStatus(
    string Id,
    string Name,
    bool OnShift,
    DateTimeOffset? ShiftEndsUtc,
    DateTimeOffset? NextShiftStartsUtc,
    DateTimeOffset? BusyUntilUtc,
    string? BusyWith,
    bool Available,                          // on shift and not busy at the requested time
    bool HasAllRequiredCertifications,
    IReadOnlyList<string> MissingCertifications,
    IReadOnlyList<string> Certifications,
    GeoPoint LastKnownLocation,
    DateTimeOffset LocationAsOfUtc,
    double? StraightLineKm,
    int? EstimatedTravelMinutes,
    string Vehicle,
    bool CanTowExcavator);

public static class WorkforceAvailability
{
    /// <summary>
    /// A technician who already has a work order for another incident is busy for that time, like any other job. The incident being
    /// planned is excluded, so planning it again does not make its own technician look busy.
    /// </summary>
    public static Technician WithAssignments(Technician technician, IEnumerable<WorkOrder> workOrders, string? excludeIncidentId = null) =>
        technician with
        {
            BusyBlocks =
            [
                .. technician.BusyBlocks,
                .. workOrders
                    .Where(w => w.Status == "scheduled" && w.TechnicianId == technician.Id && !string.Equals(w.IncidentId, excludeIncidentId, StringComparison.OrdinalIgnoreCase))
                    .Select(w => new BusyBlock(w.ScheduledStartUtc, w.ExpectedCompletionUtc, w.Id, $"work order {w.Id} ({w.Street})")),
            ],
        };

    /// <summary>The work order of another incident that overlaps with a new job of this technician, if any.</summary>
    public static WorkOrder? FindConflict(IEnumerable<WorkOrder> workOrders, string technicianId, string incidentId, DateTimeOffset startUtc, DateTimeOffset endUtc) =>
        workOrders.FirstOrDefault(w => w.Status == "scheduled" && w.TechnicianId == technicianId
                                       && !string.Equals(w.IncidentId, incidentId, StringComparison.OrdinalIgnoreCase)
                                       && w.ScheduledStartUtc < endUtc && w.ExpectedCompletionUtc > startUtc);

    /// <summary>
    /// Evaluates one technician at a moment in time against a list of required certifications and an optional site. With a
    /// <paramref name="commitment"/> (how long the job will take) the technician must also be free for that whole time: a job that
    /// starts ten minutes from now counts as busy for a repair that takes six hours.
    /// </summary>
    public static TechnicianStatus Evaluate(Technician technician, DateTimeOffset atUtc, IReadOnlyCollection<string> requiredCertifications, GeoPoint? site, TimeSpan? commitment = null)
    {
        var at = LeakOpsTime.Utc(atUtc);
        var until = at + (commitment ?? TimeSpan.Zero);
        var shift = technician.Shifts.FirstOrDefault(s => s.StartUtc <= at && s.EndUtc > at);
        var busy = technician.BusyBlocks.FirstOrDefault(b => b.StartUtc <= at ? b.EndUtc > at : b.StartUtc < until);
        var next = technician.Shifts.Where(s => s.StartUtc > at).OrderBy(s => s.StartUtc).FirstOrDefault();
        var missing = requiredCertifications.Where(c => !technician.Certifications.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();

        return new TechnicianStatus(
            Id: technician.Id,
            Name: technician.Name,
            OnShift: shift is not null,
            ShiftEndsUtc: shift?.EndUtc,
            NextShiftStartsUtc: next?.StartUtc,
            BusyUntilUtc: busy?.EndUtc,
            BusyWith: busy?.Description,
            Available: shift is not null && busy is null,
            HasAllRequiredCertifications: missing.Count == 0,
            MissingCertifications: missing,
            Certifications: technician.Certifications,
            LastKnownLocation: technician.Location,
            LocationAsOfUtc: technician.LocationAsOfUtc,
            StraightLineKm: site is null ? null : Math.Round(Geo.Metres(technician.Location, site) / 1000.0, 1),
            EstimatedTravelMinutes: site is null ? null : TravelEstimator.Estimate(technician.Location, site, at).Minutes,
            Vehicle: $"{technician.Vehicle.Type} {technician.Vehicle.Plate}",
            CanTowExcavator: technician.Vehicle.CanTowExcavator);
    }
}
