using LeakOps.Contracts;
using LeakOps.Contracts.Logic;

namespace LeakOps.Waterstoring;

public record TechnicianAssignment(
    string WorkOrderId, string IncidentId, string Street, string Summary,
    DateTimeOffset LeavesUtc, DateTimeOffset ArrivesUtc, DateTimeOffset DoneUtc);

public record TechnicianCard(
    string Id, string Name, string State, string Region,
    IReadOnlyList<string> Certifications, string Vehicle, bool CanTowExcavator,
    GeoPoint LastPosition, DateTimeOffset LastPositionAsOfUtc,
    DateTimeOffset? ShiftEndsUtc, DateTimeOffset? NextShiftStartsUtc,
    string? BusyWith, DateTimeOffset? BusyUntilUtc,
    string Photo, TechnicianAssignment? Assignment);

public record TechnicianSummary(int Total, int Available, int Assigned, int Busy, int OffShift);

public record TechnicianBoardResult(DateTimeOffset AtUtc, bool Simulated, TechnicianSummary Summary, IReadOnlyList<TechnicianCard> Technicians);

/// <summary>
/// The roster as the Resource Matchmaker sees it, one card per technician. The state is one of:
/// assigned (a scheduled work order of this demo names the technician), off_shift, busy (a job from the roster), or available.
/// It reuses <see cref="WorkforceAvailability"/>, so this page and the agents cannot disagree.
/// </summary>
public static class TechnicianBoard
{
    public const string Available = "available";
    public const string Assigned = "assigned";
    public const string Busy = "busy";
    public const string OffShift = "off_shift";

    public static TechnicianBoardResult Build(IEnumerable<Technician> technicians, IEnumerable<WorkOrder> workOrders, DateTimeOffset atUtc)
    {
        var scheduled = workOrders.Where(w => w.Status == "scheduled").ToList();
        var cards = technicians.OrderBy(t => t.Id, StringComparer.Ordinal).Select(t =>
        {
            var status = WorkforceAvailability.Evaluate(t, atUtc, [], null);
            // The most recent work order wins if a technician somehow has more than one.
            var order = scheduled.Where(w => w.TechnicianId == t.Id).OrderByDescending(w => w.CreatedUtc).FirstOrDefault();
            var state = order is not null ? Assigned : !status.OnShift ? OffShift : status.Available ? Available : Busy;

            return new TechnicianCard(
                t.Id, t.Name, state, t.Region, t.Certifications, status.Vehicle, status.CanTowExcavator,
                t.Location, t.LocationAsOfUtc, status.ShiftEndsUtc, status.NextShiftStartsUtc, status.BusyWith, status.BusyUntilUtc,
                $"/technicians/{t.Id}.jpg",
                order is null ? null : new TechnicianAssignment(order.Id, order.IncidentId, SchedulingRules.StreetName(order.Street), order.Summary,
                    order.ScheduledStartUtc, order.ExpectedArrivalUtc, order.ExpectedCompletionUtc));
        }).ToList();

        var summary = new TechnicianSummary(cards.Count, cards.Count(c => c.State == Available), cards.Count(c => c.State == Assigned),
            cards.Count(c => c.State == Busy), cards.Count(c => c.State == OffShift));
        return new TechnicianBoardResult(atUtc, Simulated: true, summary, cards);
    }
}
