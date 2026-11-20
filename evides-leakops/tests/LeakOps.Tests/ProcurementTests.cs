using LeakOps.Contracts;
using LeakOps.Contracts.Logic;
using LeakOps.Seeder;
using Xunit;

namespace LeakOps.Tests;

public class ProcurementTests
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

    private static ProcurementJudgement Model(string[]? parts = null, string[]? equipment = null, string rationale = "Done.") =>
        new(parts ?? ["CLP-CI-300", "GSK-300", "BLT-KIT-300", "DIS-KIT", "PIP-DI-300-6M", "CPL-FLEX-300"],
            equipment ?? ["EQP-EXC-8T", "EQP-TRK-01", "EQP-PMP-01"], rationale);

    private static ProcurementPlan Plan(DiagnosticReport? report = null, ProcurementJudgement? model = null, List<StockView>? stock = null, List<StockReservation>? reserved = null) =>
        ProcurementRules.Plan(report ?? BurstReport(), Procedure, stock ?? Stock(), reserved ?? [], model ?? Model());

    [Fact]
    public void Severe_burst_reserves_the_whole_package_and_orders_what_is_short_and_low()
    {
        var plan = Plan();

        Assert.Empty(plan.ReviewReasons);
        Assert.Equal(
            ["CLP-CI-300:1", "GSK-300:1", "BLT-KIT-300:1", "DIS-KIT:1", "PIP-DI-300-6M:1", "CPL-FLEX-300:1", "EQP-EXC-8T:1", "EQP-TRK-01:1", "EQP-PMP-01:1"],
            plan.Reservations.Select(r => $"{r.Sku}:{r.Quantity}"));

        // S1 means the pipe section is replaced, so two couplings are needed and only one is in stock.
        var coupling = plan.Lines.Single(l => l.Sku == "CPL-FLEX-300");
        Assert.Equal((2, 1, 1, "partly_reserved"), (coupling.Needed, coupling.Reserved, coupling.Short, coupling.Status));

        var urgent = Assert.Single(plan.Orders, o => o.Purpose == "urgent");
        Assert.Equal(("CPL-FLEX-300", 1, true), (urgent.Sku, urgent.Quantity, urgent.Express));

        // Replenishment: only the parts that end below their reorder level (clamp 2 -> 1 of 3, coupling 1 -> 0 of 2).
        Assert.Equal(["CLP-CI-300:6", "CPL-FLEX-300:4"], plan.Orders.Where(o => o.Purpose == "replenishment").Select(o => $"{o.Sku}:{o.Quantity}"));
        Assert.All(plan.Orders.Where(o => o.Purpose == "replenishment"), o => Assert.False(o.Express));

        // The gate valve is only for a valve that fails: out of stock, but not reserved or ordered.
        Assert.DoesNotContain(plan.Lines, l => l.Sku == "VLV-GATE-300");
        Assert.DoesNotContain(plan.Orders, o => o.Sku == "VLV-GATE-300");
        Assert.Contains(plan.Contingencies, c => c.StartsWith("VLV-GATE-300") && c.Contains("0 available"));
    }

    [Fact]
    public void A_minor_severity_needs_no_pipe_section_or_couplings()
    {
        var plan = Plan(BurstReport() with { Severity = "S3" }, Model(parts: ["CLP-CI-300", "GSK-300", "BLT-KIT-300", "DIS-KIT"]));

        Assert.DoesNotContain(plan.Lines, l => l.Sku is "PIP-DI-300-6M" or "CPL-FLEX-300");
        Assert.DoesNotContain(plan.Orders, o => o.Purpose == "urgent");
        Assert.Equal(2, plan.Contingencies.Count(c => c.StartsWith("PIP-") || c.StartsWith("CPL-")));
    }

    [Fact]
    public void A_required_part_the_model_left_out_is_added_and_a_part_outside_the_procedure_is_ignored()
    {
        var plan = Plan(model: Model(parts: ["CLP-CI-300", "GSK-300", "BLT-KIT-300", "PIP-DI-300-6M", "CPL-FLEX-300", "VLV-GATE-250"]));

        Assert.Contains(plan.Lines, l => l.Sku == "DIS-KIT");
        Assert.Contains(plan.Warnings, w => w.StartsWith("DIS-KIT was added"));
        Assert.Contains(plan.Warnings, w => w.Contains("VLV-GATE-250") && w.Contains("not in the repair procedure"));
        Assert.DoesNotContain(plan.Lines, l => l.Sku == "VLV-GATE-250");
    }

    [Fact]
    public void A_gate_valve_the_model_wants_without_a_failed_valve_stays_a_contingency()
    {
        var plan = Plan(model: Model(parts: [.. Model().PartSkus, "VLV-GATE-300"]));

        Assert.DoesNotContain(plan.Lines, l => l.Sku == "VLV-GATE-300");
        Assert.Contains(plan.Warnings, w => w.Contains("VLV-GATE-300") && w.Contains("condition is not met"));
    }

    [Fact]
    public void A_valve_that_is_not_open_makes_the_gate_valve_a_requirement_and_its_express_order_needs_approval()
    {
        var report = BurstReport();
        var broken = report with { IsolationPlan = [report.IsolationPlan[0] with { Status = "closed" }, report.IsolationPlan[1]] };
        var plan = Plan(broken, Model(parts: [.. Model().PartSkus, "VLV-GATE-300"]));

        var valve = Assert.Single(plan.Lines, l => l.Sku == "VLV-GATE-300");
        Assert.Equal(("short", 1), (valve.Status, valve.Short));
        var order = Assert.Single(plan.Orders, o => o.Sku == "VLV-GATE-300" && o.Purpose == "urgent");
        Assert.True(order.Express);
        Assert.Contains(plan.Orders, o => o.Sku == "VLV-GATE-300" && o.Purpose == "replenishment" && !o.Express);   // none left afterwards either

        var item = Data.Inventory.Single(i => i.Id == "VLV-GATE-300");
        var po = Procurement.BuildOrder(item, Data.Suppliers.Single(s => s.Id == item.SupplierId), "INC-1", order.Quantity, order.Express, order.Reason, Burst.Alarm.TriggeredAtUtc);
        var result = ProcurementRules.Assemble(broken, Procedure, plan, new Dictionary<string, string>(), [po], Model());
        Assert.Contains(result.Warnings, w => w.Contains("needs a manager's approval") && w.Contains("not on its way"));
        Assert.False(result.PackageComplete);
    }

    [Fact]
    public void Equipment_that_is_not_available_is_replaced_by_one_that_is()
    {
        var plan = Plan(model: Model(equipment: ["EQP-EXC-14T", "EQP-TRK-01", "EQP-PMP-01"]));   // the 14 t excavator is in service until Monday

        Assert.Contains(plan.Reservations, r => r.Sku == "EQP-EXC-8T");
        Assert.DoesNotContain(plan.Reservations, r => r.Sku == "EQP-EXC-14T");
        Assert.Contains(plan.Warnings, w => w.Contains("EQP-EXC-14T") && w.Contains("EQP-EXC-8T"));
    }

    [Fact]
    public void Without_an_available_excavator_the_repair_cannot_be_equipped_and_a_person_decides()
    {
        var stock = Stock(i => i.Id == "EQP-EXC-8T" ? i with { QuantityReserved = 1 } : i);
        var plan = Plan(stock: stock);

        Assert.Contains(plan.ReviewReasons, r => r.Contains("excavator of at least 8 t") && r.Contains("EQP-EXC-14T is back on Mon 16 Nov"));
        Assert.DoesNotContain(plan.Reservations, r => r.Sku.StartsWith("EQP-EXC"));

        // Until a person decides nothing at all is booked: no reservations, no orders, nothing tied up for a repair that may change.
        Assert.Empty(plan.Reservations);
        Assert.Empty(plan.Orders);
        Assert.All(plan.Lines, l => Assert.Equal(("not_booked", 0), (l.Status, l.Reserved)));
    }

    [Fact]
    public void Running_it_again_does_not_reserve_or_order_twice()
    {
        var first = Plan();
        var reserved = first.Reservations.Select(r => new StockReservation($"INC-{r.Sku}", "INC", r.Sku, r.Sku, "part", r.Quantity, "WH-01", Burst.Alarm.TriggeredAtUtc)).ToList();
        // The stock as it is after the first run: what was reserved is now counted as reserved.
        var stockAfter = Stock(i => first.Reservations.FirstOrDefault(r => r.Sku == i.Id) is { } r ? i with { QuantityReserved = i.QuantityReserved + r.Quantity } : i);

        var second = Plan(stock: stockAfter, reserved: reserved);

        Assert.Empty(second.Reservations);
        Assert.Equal(first.Orders.Select(o => (o.Sku, o.Quantity, o.Purpose)), second.Orders.Select(o => (o.Sku, o.Quantity, o.Purpose)));
        Assert.Equal(first.Lines.Select(l => (l.Sku, l.Reserved, l.Short)), second.Lines.Select(l => (l.Sku, l.Reserved, l.Short)));
    }

    [Fact]
    public void Without_a_repair_procedure_nothing_is_reserved()
    {
        var plan = ProcurementRules.Plan(BurstReport(), null, Stock(), [], Model());

        Assert.Empty(plan.Reservations);
        Assert.Contains(plan.ReviewReasons, r => r.Contains("no repair procedure"));
    }

    [Fact]
    public void A_failed_reservation_is_reported_in_the_lines_the_summary_and_for_review()
    {
        var plan = Plan();
        var status = new Dictionary<string, string> { ["CLP-CI-300"] = "insufficient_stock" };
        var result = ProcurementRules.Assemble(BurstReport(), Procedure, plan, status, [], Model(rationale: "Everything will be reserved."));

        var clamp = result.Lines.Single(l => l.Sku == "CLP-CI-300");
        Assert.Equal((0, 1), (clamp.Reserved, clamp.Short));
        Assert.StartsWith("failed", clamp.Status);
        Assert.True(result.NeedsReview);
        Assert.False(result.PackageComplete);
        Assert.Contains("CLP-CI-300 x1 (not ordered)", result.Summary);
        Assert.Equal("Everything will be reserved.", result.Rationale);   // the model's reasoning is kept apart from the facts
    }

    [Fact]
    public void The_summary_is_built_from_the_outcome_with_orders_deliveries_and_approvals()
    {
        var plan = Plan();
        var orders = plan.Orders.Select(o =>
        {
            var item = Data.Inventory.Single(i => i.Id == o.Sku);
            return Procurement.BuildOrder(item, Data.Suppliers.Single(s => s.Id == item.SupplierId), "INC-1", o.Quantity, o.Express, o.Reason,
                Burst.Alarm.TriggeredAtUtc, o.Purpose == "replenishment" ? Procurement.ReplenishmentSuffix : "");
        }).ToList();
        var result = ProcurementRules.Assemble(BurstReport(), Procedure, plan, new Dictionary<string, string>(), orders, Model());

        Assert.False(result.PackageComplete);   // the second coupling is not here yet
        Assert.Contains("Still short: CPL-FLEX-300 x1 (ordered express, arrives Sat 14 Nov 16:10)", result.Summary);
        Assert.Contains("CLP-CI-300 x6 (4110 EUR, waiting for approval)", result.Summary);   // above 2,500 EUR
        Assert.Contains("CPL-FLEX-300 x4 (2160 EUR)", result.Summary);
        Assert.Contains("Kept as contingency, not reserved: VLV-GATE-300", result.Summary);
        Assert.Contains(result.Warnings, w => w.Contains("ordered express") && w.Contains("after the technician has left"));
        Assert.Contains(result.Warnings, w => w.Contains("PO-INC-1-CLP-CI-300-REPL") && w.Contains("needs a manager's approval"));
        Assert.Equal(new[] { "PO-INC-1-CPL-FLEX-300", "PO-INC-1-CPL-FLEX-300-REPL", "PO-INC-1-CLP-CI-300-REPL" }.Order(), orders.Select(o => o.Id).Order());
    }
}
