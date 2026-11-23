using System.Text.RegularExpressions;

namespace LeakOps.Contracts.Logic;

public record PlannedReservation(string Sku, int Quantity);

public record PlannedOrder(string Sku, int Quantity, bool Express, string Purpose, string Reason);   // Purpose: urgent | replenishment

/// <summary>What the Procurement agent will do, decided before anything is booked.</summary>
public record ProcurementPlan(
    IReadOnlyList<PackageLine> Lines,
    IReadOnlyList<PlannedReservation> Reservations,
    IReadOnlyList<PlannedOrder> Orders,
    IReadOnlyList<string> Contingencies,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> ReviewReasons);

/// <summary>
/// The rules of the Procurement agent. The model says which conditional parts the repair needs and which equipment to take, and
/// explains it. These rules decide what is really needed (the repair procedure), how much of it can be reserved, what has to be
/// ordered and how, and they never let the model reserve something outside the procedure.
/// </summary>
public static class ProcurementRules
{
    private enum Decision { Required, Contingent, Judgement }

    public static ProcurementPlan Plan(
        DiagnosticReport report,
        RepairProcedure? procedure,
        IReadOnlyList<StockView> stock,
        IReadOnlyList<StockReservation> alreadyReserved,
        ProcurementJudgement judgement)
    {
        var warnings = new List<string>();
        var review = new List<string>();
        var contingencies = new List<string>();
        var lines = new List<PackageLine>();
        var reservations = new List<PlannedReservation>();
        var orders = new List<PlannedOrder>();

        if (procedure is null)
        {
            review.Add("There is no repair procedure for this pipe, so the parts and equipment it needs are unknown.");
            return new ProcurementPlan(lines, reservations, orders, contingencies, warnings, review);
        }

        var incidentId = IncidentIds.For(report.AlarmId);
        var stockBySku = stock.ToDictionary(s => s.Sku, StringComparer.OrdinalIgnoreCase);
        var haveBySku = alreadyReserved.GroupBy(r => r.Sku, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity), StringComparer.OrdinalIgnoreCase);
        var modelParts = new HashSet<string>(judgement.PartSkus ?? [], StringComparer.OrdinalIgnoreCase);
        var modelEquipment = new HashSet<string>(judgement.EquipmentSkus ?? [], StringComparer.OrdinalIgnoreCase);

        foreach (var sku in modelParts.Where(s => procedure.Parts.All(p => !p.Sku.Equals(s, StringComparison.OrdinalIgnoreCase))))
            warnings.Add($"The model asked for {sku}, which is not in the repair procedure; it was ignored.");

        // Parts: the pipe section decides about its couplings, so decide it first.
        var decisions = new Dictionary<string, Decision>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in procedure.Parts.Where(p => !p.Condition.Contains("replacement pipe section", StringComparison.OrdinalIgnoreCase)))
            decisions[part.Sku] = Evaluate(part, report);
        var pipeSectionNeeded = procedure.Parts.Any(p => p.Sku.StartsWith("PIP-", StringComparison.OrdinalIgnoreCase)
                                                        && IsNeeded(decisions[p.Sku], p.Sku, modelParts));
        foreach (var part in procedure.Parts.Where(p => p.Condition.Contains("replacement pipe section", StringComparison.OrdinalIgnoreCase)))
            decisions[part.Sku] = pipeSectionNeeded ? Decision.Required : Decision.Contingent;

        var needs = new List<(string Sku, int Quantity, string Kind, string Why)>();
        foreach (var part in procedure.Parts)
        {
            var decision = decisions[part.Sku];
            var inModel = modelParts.Contains(part.Sku);
            if (decision == Decision.Contingent)
            {
                if (inModel) warnings.Add($"The model wanted {part.Sku}, but its condition is not met ({part.Condition}); it is kept as a contingency.");
                contingencies.Add(ContingencyText(part, stockBySku.GetValueOrDefault(part.Sku)));
                continue;
            }
            if (decision == Decision.Judgement && !inModel)
            {
                contingencies.Add(ContingencyText(part, stockBySku.GetValueOrDefault(part.Sku)));
                continue;
            }
            if (decision == Decision.Required && !inModel)
                warnings.Add($"{part.Sku} was added: the repair procedure needs it ({part.Condition}).");
            needs.Add((part.Sku, part.Quantity, "part", part.Condition == "always" ? "always needed for this repair" : part.Condition));
        }

        // Equipment: one usable item per requirement, preferably the model's pick, never one that is not available.
        foreach (var requirement in procedure.Equipment)
        {
            var label = requirement.EquipmentType + (requirement.MinCapacityTonnes is { } t ? $" of at least {t} t" : "");
            var candidates = stock.Where(s => s.Kind == "equipment" && s.EquipmentType == requirement.EquipmentType
                                              && (requirement.MinCapacityTonnes is null || s.CapacityTonnes >= requirement.MinCapacityTonnes)).ToList();
            var usable = candidates.Where(s => s.QuantityAvailable > 0 || haveBySku.ContainsKey(s.Sku)).ToList();
            var pick = usable.FirstOrDefault(s => modelEquipment.Contains(s.Sku))
                       ?? usable.OrderBy(s => s.CapacityTonnes ?? 0).ThenBy(s => s.Sku, StringComparer.Ordinal).FirstOrDefault();
            if (pick is null)
            {
                var back = candidates.Where(c => c.AvailableFromUtc is not null).OrderBy(c => c.AvailableFromUtc).FirstOrDefault();
                review.Add($"No {label} is available at the alarm time" + (back is null ? "." : $"; {back.Sku} is back on {Local(back.AvailableFromUtc!.Value)}."));
                continue;
            }
            var chosenByModel = candidates.FirstOrDefault(c => modelEquipment.Contains(c.Sku));
            if (chosenByModel is not null && chosenByModel.Sku != pick.Sku)
                warnings.Add($"The model chose {chosenByModel.Sku}, which is not available; {pick.Sku} is reserved instead.");
            else if (chosenByModel is null && modelEquipment.Count > 0 && !haveBySku.ContainsKey(pick.Sku))   // already reserved for this incident: a repeat run, nothing to correct
                warnings.Add($"The model's equipment choice did not fit the {label} the procedure needs; {pick.Sku} was chosen.");
            needs.Add((pick.Sku, 1, "equipment", $"the procedure needs {label}"));
        }

        foreach (var need in needs.GroupBy(n => n.Sku, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
        {
            if (!stockBySku.TryGetValue(need.Sku, out var item))
            {
                review.Add($"{need.Sku} is not in the inventory.");
                continue;
            }

            var have = Math.Min(haveBySku.GetValueOrDefault(need.Sku), need.Quantity);
            var take = have > 0 ? 0 : Math.Min(need.Quantity, item.QuantityAvailable);   // a reservation cannot be topped up, only made once
            var covered = have + take;
            var shortBy = need.Quantity - covered;
            if (take > 0) reservations.Add(new PlannedReservation(item.Sku, take));

            var status = covered == 0 ? "short" : shortBy > 0 ? "partly_reserved" : have > 0 ? "already_reserved" : "reserved";
            lines.Add(new PackageLine(item.Sku, item.Name, need.Kind, need.Quantity, covered, shortBy, need.Why, status));

            if (need.Kind != "part") continue;

            if (shortBy > 0)
                orders.Add(new PlannedOrder(item.Sku, shortBy, Express: true, "urgent",
                    $"{incidentId}: {shortBy} short for the repair ({need.Why})."));

            var remaining = have > 0 ? item.QuantityAvailable : item.QuantityAvailable - take;
            if (remaining < item.ReorderLevel && item.ReorderQuantity > 0)
                orders.Add(new PlannedOrder(item.Sku, item.ReorderQuantity, Express: false, "replenishment",
                    $"{incidentId}: {remaining} left after this repair, below the reorder level of {item.ReorderLevel}."));
        }

        // A blocker (for example no excavator at all) means a person decides first. Until then nothing is reserved or ordered, so stock is
        // not tied up for a repair that may not go ahead the way it was planned.
        if (review.Count > 0)
        {
            warnings.Add("Nothing was reserved or ordered: a person has to decide first.");
            var unbooked = lines.Select(l =>
            {
                var have = Math.Min(haveBySku.GetValueOrDefault(l.Sku), l.Needed);
                return l with { Reserved = have, Short = l.Needed - have, Status = have == 0 ? "not_booked" : have == l.Needed ? "already_reserved" : "partly_reserved" };
            }).ToList();
            return new ProcurementPlan(unbooked, [], [], contingencies, warnings, review);
        }

        return new ProcurementPlan(lines, reservations, orders, contingencies, warnings, review);
    }

    /// <summary>Builds the final answer from the plan and what really happened when it was carried out.</summary>
    public static ProcurementResult Assemble(
        DiagnosticReport report,
        RepairProcedure? procedure,
        ProcurementPlan plan,
        IReadOnlyDictionary<string, string> reserveStatusBySku,
        IReadOnlyList<PurchaseOrder> orders,
        ProcurementJudgement judgement)
    {
        var warnings = plan.Warnings.ToList();
        var review = plan.ReviewReasons.ToList();

        var lines = plan.Lines.Select(line =>
        {
            if (!reserveStatusBySku.TryGetValue(line.Sku, out var status) || status is "reserved" or "already_reserved") return line;
            var reserved = line.Reserved - plan.Reservations.Where(r => r.Sku == line.Sku).Sum(r => r.Quantity);
            warnings.Add($"Reserving {line.Sku} failed ({status}).");
            review.Add($"{line.Sku} could not be reserved ({status}).");
            return line with { Reserved = reserved, Short = line.Needed - reserved, Status = $"failed ({status})" };
        }).ToList();

        foreach (var order in orders.Where(o => o.ApprovalRequired))
            warnings.Add($"{order.Id} ({order.TotalEur:F0} EUR) needs a manager's approval before it is sent" +
                         (order.Reason.Contains("short for the repair") ? "; the missing part is not on its way until then." : "."));
        foreach (var order in orders.Where(o => o.Express))
            warnings.Add($"{order.Sku} x{order.Quantity} is ordered express and arrives at {Local(order.ExpectedDeliveryUtc)}, after the technician has left.");

        var complete = review.Count == 0 && lines.All(l => l.Short == 0) && lines.Count > 0;

        return new ProcurementResult(
            IncidentId: IncidentIds.For(report.AlarmId),
            AlarmId: report.AlarmId,
            RepairProcedureId: procedure?.Id,
            PackageComplete: complete,
            Lines: lines,
            PurchaseOrders: orders,
            Contingencies: plan.Contingencies,
            Summary: BuildSummary(lines, orders, plan.Contingencies, review),
            Rationale: judgement.Rationale?.Trim() ?? "",
            Warnings: warnings,
            NeedsReview: review.Count > 0,
            ReviewReasons: review);
    }

    private static Decision Evaluate(PartRequirement part, DiagnosticReport report)
    {
        var condition = part.Condition.Trim().ToLowerInvariant();
        if (condition == "always") return Decision.Required;
        if (condition.Contains("isolation valve"))
            return report.IsolationPlan.Any(s => !s.Status.Equals("open", StringComparison.OrdinalIgnoreCase)) ? Decision.Required : Decision.Contingent;

        var severity = Regex.Match(condition, @"severity is (s\d)(?: or (s\d))?");
        if (severity.Success)
        {
            var levels = severity.Groups.Cast<Group>().Skip(1).Where(g => g.Success).Select(g => g.Value);
            if (levels.Contains(report.Severity, StringComparer.OrdinalIgnoreCase)) return Decision.Required;
            return condition.Contains("split") ? Decision.Judgement : Decision.Contingent;   // "split lengthwise" cannot be known beforehand
        }
        return Decision.Judgement;
    }

    private static bool IsNeeded(Decision decision, string sku, HashSet<string> modelParts) =>
        decision == Decision.Required || (decision == Decision.Judgement && modelParts.Contains(sku));

    private static string Local(DateTimeOffset utc) => LeakOpsTime.ToLocal(utc).ToString("ddd d MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    private static string ContingencyText(PartRequirement part, StockView? stock)
    {
        var text = $"{part.Sku} x{part.Quantity} is not reserved (needed {(part.Condition.StartsWith("only", StringComparison.OrdinalIgnoreCase) ? "" : "only ")}{part.Condition})";
        return stock is null
            ? text + "."
            : text + $": {stock.QuantityAvailable} available; supplier {stock.Supplier} delivers in {stock.SupplierStandardLeadTimeDays} days, express in {stock.SupplierExpressLeadHours} h (+{stock.SupplierExpressSurchargePercent:F0}%).";
    }

    /// <summary>The outcome in plain words, built from what really happened (not from what the model expected).</summary>
    private static string BuildSummary(IReadOnlyList<PackageLine> lines, IReadOnlyList<PurchaseOrder> orders, IReadOnlyList<string> contingencies, IReadOnlyList<string> review)
    {
        var parts = new List<string>();
        var reserved = lines.Where(l => l.Reserved > 0).Select(l => $"{l.Sku} x{l.Reserved}").ToList();
        parts.Add("Reserved for the repair: " + (reserved.Count > 0 ? string.Join(", ", reserved) : "nothing") + ".");

        var missing = lines.Where(l => l.Short > 0).Select(l =>
        {
            var urgent = orders.FirstOrDefault(o => o.Sku == l.Sku && !o.Id.EndsWith(Procurement.ReplenishmentSuffix));
            return $"{l.Sku} x{l.Short}" + (urgent is null ? " (not ordered)" : $" (ordered{(urgent.Express ? " express" : "")}, arrives {Local(urgent.ExpectedDeliveryUtc)}{(urgent.ApprovalRequired ? ", waiting for approval" : "")})");
        }).ToList();
        parts.Add(missing.Count > 0 ? "Still short: " + string.Join(", ", missing) + "." : "Nothing is short.");

        var restock = orders.Where(o => o.Id.EndsWith(Procurement.ReplenishmentSuffix))
            .Select(o => $"{o.Sku} x{o.Quantity} ({o.TotalEur:F0} EUR{(o.ApprovalRequired ? ", waiting for approval" : "")})").ToList();
        if (restock.Count > 0) parts.Add("Restocking after the repair: " + string.Join(", ", restock) + ".");

        if (contingencies.Count > 0)
            parts.Add("Kept as contingency, not reserved: " + string.Join(", ", contingencies.Select(c => c.Split(' ')[0])) + ".");
        if (review.Count > 0) parts.Add(string.Join(" ", review));
        return string.Join(" ", parts);
    }
}
