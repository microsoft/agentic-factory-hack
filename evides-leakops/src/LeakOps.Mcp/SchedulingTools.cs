using LeakOps.Contracts;
using LeakOps.Contracts.Logic;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace LeakOps.Mcp;

/// <summary>Tools for the Scheduling agent: travel times and the final work order.</summary>
public class SchedulingTools(LeakOpsStore store, ILogger<SchedulingTools> logger)
{
    [Function(nameof(GetTravelEstimate))]
    public Task<string> GetTravelEstimate(
        [McpToolTrigger("get-travel-estimate", "Estimates road travel time and distance from one coordinate to another, optionally via a stop (for example a depot to collect an excavator). Speed depends on the departure time: night, normal, or rush hour on weekdays. Returns every leg and the total.")]
        ToolInvocationContext context,
        [McpToolProperty("fromLat", "Start latitude", isRequired: true)] double? fromLat,
        [McpToolProperty("fromLon", "Start longitude", isRequired: true)] double? fromLon,
        [McpToolProperty("toLat", "Destination latitude", isRequired: true)] double? toLat,
        [McpToolProperty("toLon", "Destination longitude", isRequired: true)] double? toLon,
        [McpToolProperty("departUtc", "Departure moment, ISO 8601 UTC", isRequired: true)] string departUtc,
        [McpToolProperty("viaLat", "Latitude of an intermediate stop", isRequired: false)] double? viaLat,
        [McpToolProperty("viaLon", "Longitude of an intermediate stop", isRequired: false)] double? viaLon,
        [McpToolProperty("stopMinutes", "Minutes spent at the intermediate stop (loading). Default 15.", isRequired: false)] int? stopMinutes)
    {
        if (fromLat is null || fromLon is null || toLat is null || toLon is null) return Task.FromResult(ToolJson.Error("'fromLat', 'fromLon', 'toLat' and 'toLon' are required."));
        if (!ToolJson.TryTime(departUtc, "departUtc", out var depart, out var error)) return Task.FromResult(error);
        if ((viaLat is null) != (viaLon is null)) return Task.FromResult(ToolJson.Error("Give both 'viaLat' and 'viaLon', or neither."));

        var from = new GeoPoint(fromLat.Value, fromLon.Value);
        var to = new GeoPoint(toLat.Value, toLon.Value);

        var legs = new List<object>();
        var minutes = 0;
        var km = 0.0;
        var clock = depart;

        void AddLeg(string name, GeoPoint a, GeoPoint b)
        {
            var leg = TravelEstimator.Estimate(a, b, clock);
            legs.Add(new { name, leg.DistanceKm, leg.Minutes, leg.Traffic, leg.AvgSpeedKmh });
            minutes += leg.Minutes;
            km += leg.DistanceKm;
            clock = clock.AddMinutes(leg.Minutes);
        }

        if (viaLat is not null)
        {
            var via = new GeoPoint(viaLat.Value, viaLon!.Value);
            AddLeg("start to stop", from, via);
            var stop = Math.Clamp(stopMinutes ?? 15, 0, 120);
            minutes += stop;
            clock = clock.AddMinutes(stop);
            AddLeg("stop to destination", via, to);
            logger.LogInformation("get-travel-estimate via stop -> {Minutes} min", minutes);
            return Task.FromResult(ToolJson.Ok(new { departUtc = depart, arriveUtc = clock, totalMinutes = minutes, totalKm = Math.Round(km, 1), stopMinutes = stop, legs }));
        }

        AddLeg("start to destination", from, to);
        logger.LogInformation("get-travel-estimate -> {Minutes} min", minutes);
        return Task.FromResult(ToolJson.Ok(new { departUtc = depart, arriveUtc = clock, totalMinutes = minutes, totalKm = Math.Round(km, 1), legs }));
    }

    [Function(nameof(CreateWorkOrder))]
    public async Task<string> CreateWorkOrder(
        [McpToolTrigger("create-work-order", "Creates the work order for an incident and dispatches it to the technician's mobile device. The reserved parts and equipment and the purchase orders of the incident are attached automatically. Computes the expected arrival and completion times. Refuses technicians who lack a certification required by the repair procedure. Safe to repeat: one work order per incident.")]
        ToolInvocationContext context,
        [McpToolProperty("incidentId", "Incident id, for example INC-2026-1114-0001", isRequired: true)] string incidentId,
        [McpToolProperty("segmentId", "Pipe segment id with the breach, for example SEG-0412", isRequired: true)] string segmentId,
        [McpToolProperty("chainageM", "Position of the breach in metres from the segment start", isRequired: true)] double? chainageM,
        [McpToolProperty("technicianId", "Technician id chosen by the matchmaker, for example T-02", isRequired: true)] string technicianId,
        [McpToolProperty("scheduledStartUtc", "When the technician leaves, ISO 8601 UTC", isRequired: true)] string scheduledStartUtc,
        [McpToolProperty("travelMinutes", "Total travel time to the site in minutes (from get-travel-estimate)", isRequired: true)] int? travelMinutes,
        [McpToolProperty("severity", "Severity from the diagnosis: S1 (critical) to S4 (minor)", isRequired: true)] string severity,
        [McpToolProperty("summary", "One or two sentences describing the job for the technician", isRequired: true)] string summary,
        [McpToolProperty("breachType", "burst or leak. Default burst.", isRequired: false)] string? breachType,
        [McpToolProperty("isolationMinutes", "Minutes needed to close the isolation valves on site. Default 0.", isRequired: false)] int? isolationMinutes,
        [McpToolProperty("partsWaitMinutes", "Extra minutes the repair has to wait for a part that is still on order, counted after the normal repair time. Default 0.", isRequired: false)] int? partsWaitMinutes,
        [McpToolProperty("repairHours", "Repair duration in hours. Default: the estimate of the repair procedure.", isRequired: false)] double? repairHours,
        [McpToolProperty("notes", "Extra notes for the technician", isRequired: false)] string? notes)
    {
        if (string.IsNullOrWhiteSpace(incidentId)) return ToolJson.Error("'incidentId' is required.");
        if (chainageM is null || travelMinutes is null) return ToolJson.Error("'chainageM' and 'travelMinutes' are required.");
        if (!ToolJson.TryTime(scheduledStartUtc, "scheduledStartUtc", out var start, out var error)) return error;

        var segment = await store.GetSegmentAsync(segmentId);
        if (segment is null) return ToolJson.Error($"Unknown segment '{segmentId}'.");
        if (chainageM < 0 || chainageM > segment.LengthM) return ToolJson.Error($"'chainageM' must be between 0 and {segment.LengthM} for {segmentId}.");

        var technician = await store.GetTechnicianAsync(technicianId);
        if (technician is null) return ToolJson.Error($"Unknown technician '{technicianId}'.");

        var breach = string.IsNullOrWhiteSpace(breachType) ? "burst" : breachType.Trim().ToLowerInvariant();
        var procedure = await store.FindProcedureAsync(segment.Material, segment.DiameterMm, breach);
        if (procedure is null) return ToolJson.Error($"No repair procedure for {segment.Material} DN{segment.DiameterMm} ({breach}).");

        // Safety rule: never dispatch someone without the certifications the procedure requires.
        var status = WorkforceAvailability.Evaluate(technician, start, procedure.RequiredCertifications, null);
        if (!status.HasAllRequiredCertifications)
            return ToolJson.Error($"{technician.Name} cannot do this job: missing {string.Join(", ", status.MissingCertifications)}. Choose another technician.",
                new { technicianId, missingCertifications = status.MissingCertifications });

        var reservations = await store.ListReservationsAsync(incidentId);
        var orders = await store.ListPurchaseOrdersAsync(incidentId);
        var warnings = await CheckPackageAsync(procedure, reservations, orders);
        if (!status.OnShift) warnings.Add($"{technician.Name} is not on shift at {start:u}.");
        if (status.BusyUntilUtc is { } busyUntil) warnings.Add($"{technician.Name} is busy until {busyUntil:u} ({status.BusyWith}).");

        var now = LeakOpsTime.Utc(start);
        var repair = repairHours ?? procedure.EstimatedRepairHours;
        var isolation = Math.Max(0, isolationMinutes ?? 0);
        var wait = Math.Max(0, partsWaitMinutes ?? 0);
        var arrival = now.AddMinutes(travelMinutes.Value);
        var completion = arrival.AddMinutes(isolation + wait).AddHours(repair);

        // One technician cannot be at two repairs: refuse when another incident's work order overlaps in time.
        var clash = WorkforceAvailability.FindConflict(await store.ListWorkOrdersAsync(), technician.Id, incidentId, now, completion);
        if (clash is not null)
            return ToolJson.Error($"{technician.Name} already has work order {clash.Id} ({clash.Street}) from {clash.ScheduledStartUtc:u} to {clash.ExpectedCompletionUtc:u}. Choose another technician.",
                new { technicianId, conflictingWorkOrderId = clash.Id });

        var workOrder = new WorkOrder(
            Id: $"WO-{incidentId}",
            IncidentId: incidentId,
            SegmentId: segment.Id,
            Street: segment.Street,
            ChainageM: chainageM.Value,
            Location: Geo.PointAt(segment, chainageM.Value),
            BreachType: breach,
            Severity: severity,
            Summary: summary,
            TechnicianId: technician.Id,
            TechnicianName: technician.Name,
            ProcedureId: procedure.Id,
            RequiredCertifications: procedure.RequiredCertifications,
            Lines: reservations.Select(r => new WorkOrderLine(r.Sku, r.Name, r.Kind, r.Quantity, r.WarehouseId)).ToList(),
            PurchaseOrderIds: orders.Select(o => o.Id).ToList(),
            ScheduledStartUtc: now,
            TravelMinutes: travelMinutes.Value,
            IsolationMinutes: isolation,
            PartsWaitMinutes: wait,
            RepairHours: repair,
            ExpectedArrivalUtc: arrival,
            ExpectedCompletionUtc: completion,
            Status: "scheduled",
            DispatchedToMobileUtc: now,
            Notes: notes,
            CreatedUtc: now);

        await store.UpsertWorkOrderAsync(workOrder);
        logger.LogInformation("create-work-order {WorkOrderId} -> {Technician}, completion {Completion}", workOrder.Id, technician.Id, completion);

        return ToolJson.Ok(new
        {
            workOrder,
            expectedArrivalUtc = arrival,
            expectedCompletionUtc = completion,
            mobileNotification = $"Sent to {technician.Name}'s phone: {workOrder.Id}, {segment.Street}, depart {now:HH:mm}Z, arrive {arrival:HH:mm}Z.",
            warnings,
        });
    }

    /// <summary>Compares what the procedure needs with what is actually reserved or on order, and lists gaps as warnings.</summary>
    private async Task<List<string>> CheckPackageAsync(RepairProcedure procedure, List<StockReservation> reservations, List<PurchaseOrder> orders)
    {
        var warnings = new List<string>();

        foreach (var part in procedure.Parts.Where(p => p.Condition == "always"))
        {
            var covered = reservations.Any(r => r.Sku == part.Sku) || orders.Any(o => o.Sku == part.Sku);
            if (!covered) warnings.Add($"Required part {part.Sku} x{part.Quantity} is neither reserved nor on order.");
        }

        foreach (var requirement in procedure.Equipment)
        {
            var covered = false;
            foreach (var line in reservations.Where(r => r.Kind == "equipment"))
            {
                var item = await store.GetInventoryItemAsync(line.Sku);
                if (item?.EquipmentType == requirement.EquipmentType && (requirement.MinCapacityTonnes is null || item.CapacityTonnes >= requirement.MinCapacityTonnes))
                    covered = true;
            }
            if (!covered) warnings.Add($"Required equipment '{requirement.EquipmentType}'{(requirement.MinCapacityTonnes is { } t ? $" (at least {t} t)" : "")} is not reserved.");
        }

        return warnings;
    }
}
