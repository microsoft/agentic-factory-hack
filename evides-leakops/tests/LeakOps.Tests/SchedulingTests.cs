using LeakOps.Contracts;
using LeakOps.Contracts.Logic;
using LeakOps.Seeder;
using Xunit;

namespace LeakOps.Tests;

public class SchedulingTests
{
    private static readonly SeedData Data = SeedDataLoader.Load(SeedDataLoader.FindDataDirectory());
    private static readonly Scenario Burst = Data.Scenarios.Single(s => s.Id == "scn-burst-cast-iron");
    private static readonly RepairProcedure Procedure = Data.RepairProcedures.Single(p => p.Id == "RP-CI-300-400-BURST");

    private static DiagnosticReport BurstReport()
    {
        var segment = Data.PipeSegments.Single(s => s.Id == "SEG-0412");
        var valves = Data.Valves.Where(v => v.SegmentId == segment.Id)
            .Select(v => new IsolationValveInfo(v.Id, v.Type, v.Status, v.TurnsToClose, v.LastOperatedUtc, v.Location, v.ChainageM)).ToList();
        var triage = new TriageVerdict(Burst.Alarm.AlarmId, segment.DistrictId, Burst.Alarm.SensorId, Burst.Alarm.TriggeredAtUtc,
            "genuine_leak", 0.97, "Leak.", [], segment.Id, "diagnose");
        var judgement = new DiagnosisJudgement(segment.Id, segment.LengthM / 3, 30, "burst", valves.Select(v => v.Id).ToList(), "Burst.",
            [new EvidenceItem("estimate-leak-position", "PS-101 fell 2.01 bar.", "leak")]);
        return DiagnosticRules.Build(judgement, segment, valves, Procedure, triage, [new SegmentDrop(segment.Id, 2.0)]);
    }

    private static List<StockView> Stock(Func<InventoryItem, InventoryItem>? change = null)
    {
        var warehouses = Data.Warehouses.ToDictionary(w => w.Id);
        var suppliers = Data.Suppliers.ToDictionary(s => s.Id);
        return Data.Inventory.Select(i => StockViews.Create(change?.Invoke(i) ?? i, Burst.Alarm.TriggeredAtUtc, warehouses, suppliers)).ToList();
    }

    private static ResourceMatch MatchFor(DiagnosticReport report)
    {
        var roster = Data.Technicians.Select(t => WorkforceAvailability.Evaluate(t, report.TriggeredAtUtc, Procedure.RequiredCertifications, report.Location)).ToList();
        return MatchmakerRules.Build(report, Procedure, roster, new MatchJudgement("T-02", "T-05", "Samira El Idrissi goes."));
    }

    /// <summary>The package Procurement would reserve, with its orders built the way the MCP tool builds them.</summary>
    private static (ProcurementResult Package, List<StockReservation> Reservations) PackageFor(DiagnosticReport report, List<StockView> stock)
    {
        var judgement = new ProcurementJudgement(["CLP-CI-300", "GSK-300", "BLT-KIT-300", "DIS-KIT", "PIP-DI-300-6M", "CPL-FLEX-300"], ["EQP-EXC-8T", "EQP-TRK-01", "EQP-PMP-01"], "x");
        var plan = ProcurementRules.Plan(report, Procedure, stock, [], judgement);
        var incident = IncidentIds.For(report.AlarmId);
        var orders = plan.Orders.Select(o =>
        {
            var item = Data.Inventory.Single(i => i.Id == o.Sku);
            return Procurement.BuildOrder(item, Data.Suppliers.Single(s => s.Id == item.SupplierId), incident, o.Quantity, o.Express, o.Reason,
                report.TriggeredAtUtc, o.Purpose == "replenishment" ? Procurement.ReplenishmentSuffix : "");
        }).ToList();
        var reservations = plan.Reservations.Select(r =>
            new StockReservation($"{incident}-{r.Sku}", incident, r.Sku, r.Sku, "part", r.Quantity, "WH-01", report.TriggeredAtUtc)).ToList();
        return (ProcurementRules.Assemble(report, Procedure, plan, new Dictionary<string, string>(), orders, judgement), reservations);
    }

    /// <summary>What the get-travel-estimate tool returns for technician -> warehouse -> site.</summary>
    private static TravelPlan TravelFor(DiagnosticReport report, ResourceMatch match, Depot depot, ProcurementResult package)
    {
        var depart = SchedulingRules.DepartureFor(report);
        var stop = SchedulingRules.StopMinutesFor(package);
        var first = TravelEstimator.Estimate(match.Primary!.LastKnownLocation, depot.Location, depart);
        var second = TravelEstimator.Estimate(depot.Location, report.Location, depart.AddMinutes(first.Minutes + stop));
        var legs = new List<TravelLegInfo>
        {
            new("start to stop", first.DistanceKm, first.Minutes, first.Traffic, first.AvgSpeedKmh),
            new("stop to destination", second.DistanceKm, second.Minutes, second.Traffic, second.AvgSpeedKmh),
        };
        var total = first.Minutes + stop + second.Minutes;
        return new TravelPlan(depart, depart.AddMinutes(total), total, first.DistanceKm + second.DistanceKm, stop, legs);
    }

    private static (DiagnosticReport Report, ResourceMatch Match, ProcurementResult Package, SchedulePlan Plan, Depot Depot) Scenario(Func<InventoryItem, InventoryItem>? change = null)
    {
        var report = BurstReport();
        var stock = Stock(change);
        var match = MatchFor(report);
        var (package, reservations) = PackageFor(report, stock);
        var (depot, _) = SchedulingRules.ChooseDepot(reservations, stock);
        var plan = SchedulingRules.Build(report, match, package, depot, TravelFor(report, match, depot!, package));
        return (report, match, package, plan, depot!);
    }

    [Fact]
    public void Burst_with_one_coupling_short_is_delayed_until_the_express_part_has_arrived_and_been_fitted()
    {
        var (_, _, _, plan, depot) = Scenario();

        Assert.Equal(Data.Warehouses.Single(w => w.Id == "WH-01").Name, depot.Name);   // the excavator is stored there
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 3, 20, 0, TimeSpan.Zero), plan.DepartUtc);   // alarm 03:10 plus 10 minutes to get going

        // 120 + 140 turns at 0.1 min = 26, plus 5 between the valves, plus 15 because V-0412-B was last operated in 2023.
        Assert.Equal(46, plan.IsolationMinutes);

        var normalCompletion = plan.ArriveUtc.AddMinutes(46).AddHours(6);
        Assert.True(normalCompletion < new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));

        // CPL-FLEX-300 is ordered express: 12 h from 03:10 = 15:10, plus 60 minutes to fit it.
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 16, 10, 0, TimeSpan.Zero), plan.CompletionUtc);
        Assert.Equal((int)(plan.CompletionUtc - normalCompletion).TotalMinutes, plan.PartsWaitMinutes);

        // Residents are told completion plus an hour for flushing, rounded up to the quarter hour: 17:10 -> 17:15 UTC = 18:15 local.
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 17, 15, 0, TimeSpan.Zero), plan.ExpectedResolutionUtc);
        Assert.Contains(plan.Warnings, w => w.Contains("delayed") && w.Contains("CPL-FLEX-300"));
        Assert.Contains(plan.TimeBreakdown, l => l.Contains("Waiting for CPL-FLEX-300"));
    }

    [Fact]
    public void With_a_complete_package_nothing_waits_and_the_route_runs_through_the_warehouse()
    {
        var (report, match, _, plan, depot) = Scenario(i => i.Id == "CPL-FLEX-300" ? i with { QuantityOnHand = 3 } : i);

        Assert.Equal(0, plan.PartsWaitMinutes);
        Assert.Equal(plan.ArriveUtc.AddMinutes(plan.IsolationMinutes).AddHours(match.EstimatedRepairHours), plan.CompletionUtc);
        Assert.DoesNotContain(plan.Warnings, w => w.Contains("delayed"));

        Assert.Equal(["start", "depot", "site"], plan.Route.Select(r => r.Kind));
        var atDepot = plan.Route[1];
        Assert.Equal(depot.Name, atDepot.Name);
        Assert.Equal(30, (atDepot.DepartUtc - atDepot.ArriveUtc).TotalMinutes);   // 15 min loading plus 15 min for the excavator
        Assert.Equal(plan.ArriveUtc, plan.Route[2].ArriveUtc);
        Assert.Equal(report.Street + ", " + report.Place, plan.Route[2].Name);
    }

    [Fact]
    public void A_missing_part_whose_order_waits_for_approval_means_no_promise_to_residents()
    {
        var report = BurstReport();
        var match = MatchFor(report);
        var (package, reservations) = PackageFor(report, Stock());
        var waiting = package with { PurchaseOrders = [.. package.PurchaseOrders.Select(o => o.Sku == "CPL-FLEX-300" && !o.Id.EndsWith("-REPL") ? o with { ApprovalRequired = true, Status = "pending_approval" } : o)] };
        var (depot, _) = SchedulingRules.ChooseDepot(reservations, Stock());

        var plan = SchedulingRules.Build(report, match, waiting, depot, TravelFor(report, match, depot!, waiting));

        Assert.Null(plan.ExpectedResolutionUtc);
        Assert.Contains(plan.Warnings, w => w.Contains("waits for approval"));
        var (dutch, english) = SchedulingRules.PublicMessage(plan, report);
        Assert.Contains("wordt nog bepaald", dutch);
        Assert.Contains("still being determined", english);
    }

    [Fact]
    public void The_public_message_gives_the_expected_time_as_an_estimate()
    {
        var (report, _, _, plan, _) = Scenario();
        var (dutch, english) = SchedulingRules.PublicMessage(plan, report);

        Assert.Contains("rond 18:15", dutch);
        Assert.Contains("schatting", dutch);
        Assert.Contains("around 18:15", english);
        Assert.Contains("estimate", english);
        Assert.DoesNotContain("INC-", dutch);   // nothing internal in public text
        Assert.DoesNotContain("T-02", dutch);
    }

    [Fact]
    public void The_warehouse_with_the_excavator_is_the_stop_and_items_elsewhere_are_flagged()
    {
        var stock = Stock(i => i.Id == "CLP-CI-300" ? i with { WarehouseId = "WH-02" } : i);
        var reservations = new List<StockReservation>
        {
            new("r1", "INC-1", "EQP-EXC-8T", "excavator", "equipment", 1, "WH-01", Burst.Alarm.TriggeredAtUtc),
            new("r2", "INC-1", "GSK-300", "gasket", "part", 1, "WH-01", Burst.Alarm.TriggeredAtUtc),
            new("r3", "INC-1", "CLP-CI-300", "clamp", "part", 1, "WH-02", Burst.Alarm.TriggeredAtUtc),
        };

        var (depot, warnings) = SchedulingRules.ChooseDepot(reservations, stock);

        Assert.Equal(["EQP-EXC-8T", "GSK-300"], depot!.Skus);
        Assert.Contains(warnings, w => w.Contains("CLP-CI-300") && w.Contains("collected or delivered separately"));
        Assert.Null(SchedulingRules.ChooseDepot([], stock).Depot);
    }

    [Fact]
    public void A_briefing_that_follows_the_schedule_is_accepted()
    {
        var (report, match, _, plan, _) = Scenario();
        var fallback = SchedulingRules.FallbackBriefing(report, match, plan);

        Assert.Empty(SchedulingRules.CheckBriefing(fallback, report, plan));   // the rules' own text always passes its own check
    }

    [Theory]
    [InlineData("Go to Dorpsweg and close V-0412-A then V-0412-B. Arrive at 03:25.", "not a time in the schedule")]            // a made-up time
    [InlineData("Go to the site, close V-0412-A then V-0412-B.", "does not mention the street")]
    [InlineData("Go to Dorpsweg and close V-0412-A.", "does not mention valve V-0412-B")]
    [InlineData("Go to Dorpsweg, close V-0412-A and V-0412-B. Leave 2026-11-14T03:20:00Z.", "raw timestamp")]
    [InlineData("", "empty")]
    [InlineData("Go to Dorpsweg. Close V-0412-B first, then V-0412-A.", "different order")]                          // not the order of the isolation plan
    [InlineData("Go to Dorpsweg. Close V-0412-A. Do not close V-0412-B.", "not to do something")]                    // names the valve but tells the opposite
    [InlineData("Go to Dorpsweg, close V-0412-A and V-0412-B, but skip the second one if it is stiff.", "not to do something")]
    public void A_briefing_that_contradicts_the_schedule_is_rejected(string text, string expectedIssue)
    {
        var (report, _, _, plan, _) = Scenario();

        Assert.Contains(SchedulingRules.CheckBriefing(text, report, plan), i => i.Contains(expectedIssue));
    }

    [Fact]
    public void Times_in_the_briefing_must_come_from_the_schedule()
    {
        var (report, _, _, plan, _) = Scenario();
        var leave = SchedulingRules.Hm(plan.DepartUtc);
        var arrive = SchedulingRules.Hm(plan.ArriveUtc);
        var done = SchedulingRules.Hm(plan.CompletionUtc);

        var good = $"Leave at {leave}, be at Dorpsweg at {arrive}. Close V-0412-A, then V-0412-B. Done around {done}.";
        Assert.Empty(SchedulingRules.CheckBriefing(good, report, plan));
    }

    [Fact]
    public void The_technician_is_warned_when_the_shift_ends_before_the_work_is_done()
    {
        var (_, match, _, plan, _) = Scenario();

        Assert.True(match.Primary!.ShiftEndsUtc < plan.CompletionUtc);
        Assert.Contains(plan.Warnings, w => w.Contains("shift ends at 06:00") && w.Contains("hand over"));
    }

    [Fact]
    public void The_technicians_notes_carry_the_route_the_valves_and_what_is_missing()
    {
        var (report, _, _, plan, _) = Scenario();

        Assert.Contains("close V-0412-A (120 turns), then close V-0412-B (140 turns)", plan.TechnicianNotes);
        Assert.Contains("Not in the van yet: CPL-FLEX-300 x1", plan.TechnicianNotes);
        Assert.Contains("V-0412-B was last operated 2023", plan.TechnicianNotes);   // the diagnosis warning about a stiff valve
        Assert.Contains(report.Street, plan.TechnicianNotes);
    }
}
