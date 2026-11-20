using System.Globalization;
using System.Text.RegularExpressions;

namespace LeakOps.Contracts.Logic;

public record TravelLegInfo(string Name, double DistanceKm, int Minutes, string Traffic, double AvgSpeedKmh);

/// <summary>Result of the get-travel-estimate tool. TotalMinutes includes the minutes spent at the stop.</summary>
public record TravelPlan(DateTimeOffset DepartUtc, DateTimeOffset ArriveUtc, int TotalMinutes, double TotalKm, int StopMinutes, IReadOnlyList<TravelLegInfo> Legs);

/// <summary>The warehouse the technician collects the reserved parts and equipment from on the way to the site.</summary>
public record Depot(string Name, GeoPoint Location, IReadOnlyList<string> Skus);

/// <summary>Everything the Scheduling agent works out before it books anything.</summary>
public record SchedulePlan(
    DateTimeOffset DepartUtc,
    DateTimeOffset ArriveUtc,
    int IsolationMinutes,
    double RepairHours,
    int PartsWaitMinutes,
    DateTimeOffset CompletionUtc,
    DateTimeOffset? ExpectedResolutionUtc,
    IReadOnlyList<RouteStop> Route,
    IReadOnlyList<string> TimeBreakdown,
    IReadOnlyList<string> Warnings,
    string TechnicianNotes);

/// <summary>
/// The rules of the Scheduling agent. Every time in the schedule is computed here from tool facts: departure, travel through the
/// warehouse, closing the valves, the repair, any wait for a part that is still on order, and the flushing before water may be
/// used again. The model only writes the briefing text, which is checked against these numbers.
/// </summary>
public static class SchedulingRules
{
    public const int MobilisationMinutes = 10;          // from the alarm to the technician driving off
    public const int LoadingMinutes = 15;               // collecting parts at the warehouse
    public const int ExcavatorHitchMinutes = 15;        // extra for hitching and securing the excavator
    public const double IsolationMinutesPerTurn = 0.1;  // hand-turning a DN300 gate valve is slow
    public const int BetweenValvesMinutes = 5;
    public const int StiffValveBufferMinutes = 15;      // a valve not operated for years may be seized
    public const int StiffValveYears = 2;
    public const int FittingMinutes = 60;               // fitting a part that arrives while the repair is under way
    public const int FlushingMinutes = 60;              // flushing and disinfecting before the supply returns to normal

    public static DateTimeOffset DepartureFor(DiagnosticReport report) => LeakOpsTime.Utc(report.TriggeredAtUtc).AddMinutes(MobilisationMinutes);

    public static int StopMinutesFor(ProcurementResult package) =>
        LoadingMinutes + (package.Lines.Any(l => l.Kind == "equipment" && l.Sku.StartsWith("EQP-EXC", StringComparison.OrdinalIgnoreCase) && l.Reserved > 0) ? ExcavatorHitchMinutes : 0);

    /// <summary>The warehouse to stop at: the one holding the excavator (it must be towed), otherwise the one holding most of the reserved items.</summary>
    public static (Depot? Depot, List<string> Warnings) ChooseDepot(IReadOnlyList<StockReservation> reservations, IReadOnlyList<StockView> stock)
    {
        var warnings = new List<string>();
        var stockBySku = stock.ToDictionary(s => s.Sku, StringComparer.OrdinalIgnoreCase);
        var byWarehouse = reservations.Where(r => stockBySku.ContainsKey(r.Sku))
            .GroupBy(r => stockBySku[r.Sku].Warehouse)
            .Select(g => (Name: g.Key, Location: stockBySku[g.First().Sku].WarehouseLocation, Skus: g.Select(r => r.Sku).ToList()))
            .ToList();
        if (byWarehouse.Count == 0) return (null, warnings);

        var chosen = byWarehouse.FirstOrDefault(w => w.Skus.Any(s => s.StartsWith("EQP-EXC", StringComparison.OrdinalIgnoreCase)));
        if (chosen.Name is null) chosen = byWarehouse.OrderByDescending(w => w.Skus.Count).First();
        foreach (var other in byWarehouse.Where(w => w.Name != chosen.Name))
            warnings.Add($"{string.Join(", ", other.Skus)} is reserved at {other.Name} and is not on the route; it must be collected or delivered separately.");

        return (new Depot(chosen.Name, chosen.Location, chosen.Skus), warnings);
    }

    public static int IsolationMinutesFor(DiagnosticReport report, out bool stiffValve)
    {
        var steps = report.IsolationPlan;
        stiffValve = steps.Any(s => s.LastOperatedUtc is { } last && last < LeakOpsTime.Utc(report.TriggeredAtUtc).AddYears(-StiffValveYears));
        var turns = steps.Sum(s => s.TurnsToClose);
        return (int)Math.Ceiling(turns * IsolationMinutesPerTurn) + BetweenValvesMinutes * Math.Max(0, steps.Count - 1) + (stiffValve ? StiffValveBufferMinutes : 0);
    }

    public static SchedulePlan Build(DiagnosticReport report, ResourceMatch match, ProcurementResult package, Depot? depot, TravelPlan travel)
    {
        var technician = match.Primary ?? throw new InvalidOperationException("There is no technician to schedule.");
        var warnings = new List<string>();
        var breakdown = new List<string>();

        var depart = travel.DepartUtc;
        var arrive = travel.ArriveUtc;
        var isolation = IsolationMinutesFor(report, out var stiff);
        var repairHours = match.EstimatedRepairHours;
        var normalCompletion = arrive.AddMinutes(isolation).AddHours(repairHours);

        // A part that is still on order has to arrive and be fitted before the repair can finish.
        var wait = 0;
        var etaKnown = true;
        var lateParts = new List<string>();
        foreach (var line in package.Lines.Where(l => l.Kind == "part" && l.Short > 0))
        {
            var urgent = package.PurchaseOrders.FirstOrDefault(o => o.Sku == line.Sku && !o.Id.EndsWith(Procurement.ReplenishmentSuffix));
            if (urgent is null || urgent.ApprovalRequired)
            {
                etaKnown = false;
                warnings.Add($"{line.Sku} x{line.Short} is short and {(urgent is null ? "not ordered" : "its order waits for approval")}: the completion time cannot be promised.");
                continue;
            }
            var needed = urgent.ExpectedDeliveryUtc.AddMinutes(FittingMinutes);
            lateParts.Add($"{line.Sku} (arrives {Hm(urgent.ExpectedDeliveryUtc)})");
            wait = Math.Max(wait, (int)Math.Ceiling((needed - normalCompletion).TotalMinutes));
        }
        wait = Math.Max(0, wait);
        var completion = normalCompletion.AddMinutes(wait);
        var resolution = etaKnown ? RoundUpQuarter(completion.AddMinutes(FlushingMinutes)) : (DateTimeOffset?)null;

        // Route: start, warehouse (when there is something to collect), site.
        var route = new List<RouteStop> { new("Current position", "start", technician.LastKnownLocation, depart, depart, null) };
        if (depot is not null && travel.Legs.Count >= 2)
        {
            var atDepot = depart.AddMinutes(travel.Legs[0].Minutes);
            route.Add(new RouteStop(depot.Name, "depot", depot.Location, atDepot, atDepot.AddMinutes(travel.StopMinutes), "Collect: " + string.Join(", ", depot.Skus)));
        }
        route.Add(new RouteStop($"{report.Street}, {report.Place}", "site", report.Location, arrive, arrive, $"Breach at {report.ChainageM:F0} m from the start of the segment"));

        breakdown.Add($"Alarm {Hm(report.TriggeredAtUtc)}, the technician leaves at {Hm(depart)} ({MobilisationMinutes} min to get going).");
        breakdown.Add(depot is null
            ? $"Travel {travel.TotalMinutes} min ({travel.TotalKm:F1} km, {travel.Legs.FirstOrDefault()?.Traffic}); arrives {Hm(arrive)}."
            : $"Travel {travel.Legs.Sum(l => l.Minutes)} min ({travel.TotalKm:F1} km, {travel.Legs.FirstOrDefault()?.Traffic}) plus {travel.StopMinutes} min at {depot.Name}; arrives {Hm(arrive)}.");
        var turns = report.IsolationPlan.Sum(s => s.TurnsToClose);
        breakdown.Add($"Closing {report.IsolationPlan.Count} valves takes {isolation} min ({turns} turns{(report.IsolationPlan.Count > 1 ? $", {BetweenValvesMinutes} min between valves" : "")}{(stiff ? $", {StiffValveBufferMinutes} min extra for a valve not operated for over {StiffValveYears} years" : "")}).");
        breakdown.Add($"Repair {repairHours:0.#} h, so the repair is normally done at {Hm(normalCompletion)}.");
        if (wait > 0)
            breakdown.Add($"Waiting for {string.Join(", ", lateParts)} plus {FittingMinutes} min to fit it: completion moves to {Hm(completion)}.");
        breakdown.Add(resolution is { } eta
            ? $"Flushing and disinfecting takes {FlushingMinutes} min, so residents are told {Hm(eta)}."
            : "Residents are told that the expected time is still being determined.");

        if (technician.ShiftEndsUtc is { } shiftEnd && shiftEnd < completion)
            warnings.Add($"{technician.Name}'s shift ends at {Hm(shiftEnd)}, before the completion at {Hm(completion)}: hand over to the next shift.");
        if (wait > 0)
            warnings.Add($"The repair is delayed by {wait} min because {string.Join(", ", lateParts)} is still on order.");

        return new SchedulePlan(depart, arrive, isolation, repairHours, wait, completion, resolution, route, breakdown, warnings,
            TechnicianNotes(report, package, depot, route, travel, completion));
    }

    private static string TechnicianNotes(DiagnosticReport report, ProcurementResult package, Depot? depot, IReadOnlyList<RouteStop> route, TravelPlan travel, DateTimeOffset completion)
    {
        var lines = new List<string>
        {
            "Route: " + string.Join(" -> ", route.Select(r => $"{r.Name} ({(r.Kind == "start" ? "leave " : "arrive ")}{Hm(r.Kind == "start" ? r.DepartUtc : r.ArriveUtc)})")) + ".",
            "Isolation: " + string.Join(", then ", report.IsolationPlan.OrderBy(s => s.Order).Select(s => $"close {s.ValveId} ({s.TurnsToClose} turns)")) + ".",
        };
        if (depot is not null) lines.Add($"Collect at {depot.Name}: {string.Join(", ", depot.Skus)}.");
        var missing = package.Lines.Where(l => l.Short > 0).Select(l => $"{l.Sku} x{l.Short}").ToList();
        if (missing.Count > 0) lines.Add("Not in the van yet: " + string.Join(", ", missing) + ".");
        lines.AddRange(report.Warnings);
        lines.Add($"Expected completion {Hm(completion)}.");
        return string.Join(" ", lines);
    }

    // ---- briefing ------------------------------------------------------------------------------------------------

    private static readonly Regex Clock = new(@"(?<![\d:])([01]?\d|2[0-3]):[0-5]\d(?![\d:])", RegexOptions.Compiled);
    private static readonly Regex Negation = new(@"\b(do not|don't|dont|never|skip|avoid|instead of|ignore|leave (it |them )?open|keep (it |them )?open|do nothing)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Checks the model's briefing against the schedule: it must name the street and the valves and may only mention times that are in the schedule.</summary>
    public static List<string> CheckBriefing(string? text, DiagnosticReport report, SchedulePlan plan)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return ["The briefing is empty."];
        if (text.Length > 900) issues.Add("The briefing is too long for a phone screen (over 900 characters).");
        if (!text.Contains(StreetName(report.Street), StringComparison.OrdinalIgnoreCase)) issues.Add($"The briefing does not mention the street ({StreetName(report.Street)}).");
        foreach (var step in report.IsolationPlan.Where(s => !text.Contains(s.ValveId, StringComparison.OrdinalIgnoreCase)))
            issues.Add($"The briefing does not mention valve {step.ValveId}.");

        // The valves must come in the order of the isolation plan, and the text may not tell the technician to leave something out:
        // what to close is decided by the plan, not by the wording of a model.
        var positions = report.IsolationPlan.OrderBy(s => s.Order).Select(s => text.IndexOf(s.ValveId, StringComparison.OrdinalIgnoreCase)).Where(p => p >= 0).ToList();
        if (positions.Zip(positions.Skip(1), (a, b) => a > b).Any(outOfOrder => outOfOrder))
            issues.Add("The briefing names the valves in a different order than the isolation plan.");
        if (Negation.IsMatch(text))
            issues.Add("The briefing tells the technician not to do something; instructions must follow the plan.");

        var allowed = plan.Route.SelectMany(r => new[] { Hm(r.ArriveUtc), Hm(r.DepartUtc) })
            .Append(Hm(plan.CompletionUtc)).Append(Hm(plan.ArriveUtc))
            .Concat(plan.Warnings.SelectMany(w => Clock.Matches(w).Select(m => Normalize(m.Value))))   // times the schedule itself names, such as a shift end
            .ToHashSet();
        foreach (var time in Clock.Matches(text).Select(m => Normalize(m.Value)).Distinct().Where(t => !allowed.Contains(t)))
            issues.Add($"The briefing mentions {time}, which is not a time in the schedule.");
        if (Regex.IsMatch(text, @"\d{4}-\d{2}-\d{2}T"))
            issues.Add("The briefing contains a raw timestamp; use local times as HH:mm.");
        return issues;
    }

    public static string FallbackBriefing(DiagnosticReport report, ResourceMatch match, SchedulePlan plan)
    {
        var depot = plan.Route.FirstOrDefault(r => r.Kind == "depot");
        var valves = string.Join(", then ", report.IsolationPlan.OrderBy(s => s.Order).Select(s => s.ValveId));
        var text = $"{match.Primary!.Name}: leave at {Hm(plan.DepartUtc)}";
        if (depot is not null) text += $", collect the reserved parts and equipment at {depot.Name} ({Hm(depot.ArriveUtc)}-{Hm(depot.DepartUtc)})";
        text += $", and be at {report.Street}, {report.Place} at {Hm(plan.ArriveUtc)}. Close valves {valves}. ";
        text += $"Then repair the {report.DiameterMm} mm {report.Material.Replace('_', ' ')} main ({report.BreachType}, about {plan.RepairHours:0.#} h). Expected completion {Hm(plan.CompletionUtc)}.";
        return text;
    }

    // ---- public update -------------------------------------------------------------------------------------------

    /// <summary>The text residents see when the repair has been planned. Built from a fixed template, so it can never promise more than the schedule does.</summary>
    public static (string Dutch, string English) PublicMessage(SchedulePlan plan, DiagnosticReport report)
    {
        if (plan.ExpectedResolutionUtc is not { } eta)
            return ("Een monteur is ingepland voor de reparatie. Wanneer het water weer beschikbaar is, wordt nog bepaald. Deze pagina blijft actueel.",
                "A technician has been scheduled for the repair. When the water will be back is still being determined. This page is kept up to date.");

        var day = LeakOpsTime.ToLocal(eta).Date != LeakOpsTime.ToLocal(report.TriggeredAtUtc).Date;
        var dutchDay = day ? LeakOpsTime.ToLocal(eta).ToString("d MMMM ", new CultureInfo("nl-NL")) : "";
        var englishDay = day ? LeakOpsTime.ToLocal(eta).ToString("d MMMM ", CultureInfo.InvariantCulture) : "";
        return ($"Een monteur is ingepland voor de reparatie. We verwachten dat het water rond {dutchDay}{Hm(eta)} weer beschikbaar is. Dit is een schatting; de tijd kan veranderen.",
            $"A technician has been scheduled for the repair. We expect the water to be back at around {englishDay}{Hm(eta)}. This is an estimate and may change.");
    }

    public static string Hm(DateTimeOffset utc) => LeakOpsTime.ToLocal(utc).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"Dorpsweg (Kerkstraat to Havenlaan)" becomes "Dorpsweg": the name people use for the street.</summary>
    public static string StreetName(string street) => street.Split(" (")[0].Trim();

    private static string Normalize(string clock) => clock.Length == 4 ? "0" + clock : clock;

    private static DateTimeOffset RoundUpQuarter(DateTimeOffset value)
    {
        var utc = LeakOpsTime.Utc(value);
        var ticks = TimeSpan.FromMinutes(15).Ticks;
        return new DateTimeOffset((utc.UtcTicks + ticks - 1) / ticks * ticks, TimeSpan.Zero);
    }
}
