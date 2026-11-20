using LeakOps.Contracts;
using LeakOps.Contracts.Logic;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace LeakOps.Mcp;

/// <summary>Tools for the Diagnostic agent: GIS (pipe segments, valves, positions) and repair knowledge.</summary>
public class DiagnosticTools(LeakOpsStore store, ILogger<DiagnosticTools> logger)
{
    [Function(nameof(GetPipeSegment))]
    public async Task<string> GetPipeSegment(
        [McpToolTrigger("get-pipe-segment", "Gets GIS data of a pipe segment: street, material, diameter, install year, depth, length, pressure class, start/end coordinates, customers served, critical customers, postal codes, burst history, surface and road type, plus the sensors and isolation valves on it.")]
        ToolInvocationContext context,
        [McpToolProperty("segmentId", "Pipe segment id, for example SEG-0412", isRequired: true)] string segmentId)
    {
        var segment = await store.GetSegmentAsync(segmentId);
        if (segment is null) return ToolJson.Error($"Unknown segment '{segmentId}'.");

        var sensors = await store.ListSensorsOnSegmentAsync(segment.DistrictId, segment.Id);
        var valves = await store.ListValvesAsync(segment.DistrictId, segment.Id);
        logger.LogInformation("get-pipe-segment {SegmentId}", segmentId);

        return ToolJson.Ok(new
        {
            segment,
            sensors = sensors.Select(s => new { s.Id, s.Metric, s.ChainageM, s.AlarmLow, s.AlarmHigh }),
            valves = valves.Select(v => new { v.Id, v.Type, v.DiameterMm, v.ChainageM, v.Status, v.TurnsToClose, v.LastOperatedUtc }),
        });
    }

    [Function(nameof(GetPointOnSegment))]
    public async Task<string> GetPointOnSegment(
        [McpToolTrigger("get-point-on-segment", "Converts a position along a pipe segment (distance in metres from the segment start) into coordinates, and returns the isolation valves with their distance along the pipe, the nearest sensors, and what is affected (customers, critical customers, postal codes).")]
        ToolInvocationContext context,
        [McpToolProperty("segmentId", "Pipe segment id, for example SEG-0412", isRequired: true)] string segmentId,
        [McpToolProperty("chainageM", "Distance in metres from the segment start (0 to the segment length)", isRequired: true)] double? chainageM)
    {
        if (chainageM is null) return ToolJson.Error("'chainageM' is required.");
        var segment = await store.GetSegmentAsync(segmentId);
        if (segment is null) return ToolJson.Error($"Unknown segment '{segmentId}'.");
        if (chainageM < 0 || chainageM > segment.LengthM)
            return ToolJson.Error($"'chainageM' must be between 0 and {segment.LengthM} for {segmentId}.");

        var at = chainageM.Value;
        var location = Geo.PointAt(segment, at);
        var sensors = await store.ListSensorsOnSegmentAsync(segment.DistrictId, segment.Id);
        var valves = await store.ListValvesAsync(segment.DistrictId, segment.Id);
        logger.LogInformation("get-point-on-segment {SegmentId} at {Chainage} m", segmentId, at);

        return ToolJson.Ok(new
        {
            segmentId,
            street = segment.Street,
            chainageM = at,
            location,
            material = segment.Material,
            diameterMm = segment.DiameterMm,
            depthM = segment.DepthM,
            surface = segment.Surface,
            roadType = segment.RoadType,
            isolationValves = valves
                .Select(v => new { v.Id, v.Type, v.Status, v.TurnsToClose, v.LastOperatedUtc, v.Location, distanceAlongPipeM = Math.Round(Math.Abs(v.ChainageM - at)) })
                .OrderBy(v => v.distanceAlongPipeM),
            nearestSensors = sensors
                .Select(s => new { s.Id, s.Metric, distanceAlongPipeM = Math.Round(Math.Abs(s.ChainageM - at)) })
                .OrderBy(s => s.distanceAlongPipeM),
            affected = new { segment.CustomersServed, segment.CriticalCustomers, segment.PostalCodes },
        });
    }

    [Function(nameof(GetNearbyValves))]
    public async Task<string> GetNearbyValves(
        [McpToolTrigger("get-nearby-valves", "Finds valves within a radius of a coordinate, nearest first, with their pipe segment and diameter. Useful to find which valves to close around a leak.")]
        ToolInvocationContext context,
        [McpToolProperty("lat", "Latitude in degrees", isRequired: true)] double? lat,
        [McpToolProperty("lon", "Longitude in degrees", isRequired: true)] double? lon,
        [McpToolProperty("radiusM", "Search radius in metres. Default 500, maximum 5000.", isRequired: false)] double? radiusM)
    {
        if (lat is null || lon is null) return ToolJson.Error("'lat' and 'lon' are required.");
        var radius = Math.Clamp(radiusM ?? 500, 10, 5000);
        var here = new GeoPoint(lat.Value, lon.Value);

        var valves = (await store.ListAllValvesAsync())
            .Select(v => new { Valve = v, Distance = Geo.Metres(here, v.Location) })
            .Where(x => x.Distance <= radius)
            .OrderBy(x => x.Distance)
            .Select(x => new
            {
                x.Valve.Id, x.Valve.SegmentId, x.Valve.DiameterMm, x.Valve.Type, x.Valve.Status, x.Valve.TurnsToClose, x.Valve.Location,
                distanceM = Math.Round(x.Distance),
            })
            .ToList();
        logger.LogInformation("get-nearby-valves {Lat},{Lon} r={Radius} -> {Count}", lat, lon, radius, valves.Count);

        return ToolJson.Ok(new { center = here, radiusM = radius, count = valves.Count, valves });
    }

    [Function(nameof(EstimateLeakPosition))]
    public async Task<string> EstimateLeakPosition(
        [McpToolTrigger("estimate-leak-position", "Estimates where along a pipe segment a leak is (distance in metres from the segment start) from the pressure drops at the sensors on that segment, using the pressure-gradient method, with an uncertainty in metres. Also returns the district inlet flow compared with what is normal for that hour, which helps tell a burst from a small leak.")]
        ToolInvocationContext context,
        [McpToolProperty("segmentId", "Pipe segment id, for example SEG-0412", isRequired: true)] string segmentId,
        [McpToolProperty("untilUtc", "The moment to look at, ISO 8601 UTC. Normally the alarm time.", isRequired: true)] string untilUtc,
        [McpToolProperty("lookbackMinutes", "Compare the pressure at that moment with this many minutes earlier. Default 30, between 10 and 180.", isRequired: false)] int? lookbackMinutes)
    {
        if (!ToolJson.TryTime(untilUtc, "untilUtc", out var until, out var error)) return error;
        var segment = await store.GetSegmentAsync(segmentId);
        if (segment is null) return ToolJson.Error($"Unknown segment '{segmentId}'.");
        var lookback = Math.Clamp(lookbackMinutes ?? 30, 10, 180);
        var from = until.AddMinutes(-lookback);

        var drops = new List<SensorDrop>();
        var sensors = (await store.ListSensorsOnSegmentAsync(segment.DistrictId, segment.Id)).Where(s => s.Metric == "pressure");
        foreach (var sensor in sensors)
        {
            var readings = await store.GetReadingsAsync(sensor.Id, from, until);
            if (readings.Count >= 2) drops.Add(new SensorDrop(sensor.Id, sensor.ChainageM, readings[0].Value, readings[^1].Value));
        }
        if (drops.Count == 0) return ToolJson.Error($"No pressure sensor on {segmentId} has readings in this window.");

        var inlet = await InletFlowAsync(segment.DistrictId, from, until);
        var sensorsUsed = drops.Select(d => new { d.SensorId, d.ChainageM, fromBar = d.FromBar, toBar = d.ToBar, dropBar = Math.Round(d.DropBar, 2) });
        logger.LogInformation("estimate-leak-position {SegmentId} until {Until}", segmentId, until);

        var estimate = LeakLocator.Estimate(drops, segment.LengthM);
        if (estimate is null)
            return ToolJson.Ok(new
            {
                segmentId, found = false, lookbackMinutes = lookback, sensors = sensorsUsed, inletFlow = inlet,
                message = $"No pressure sensor on {segmentId} fell by at least {LeakLocator.MinimumDropBar} bar, so no leak position can be estimated here.",
            });

        return ToolJson.Ok(new
        {
            segmentId,
            found = true,
            lookbackMinutes = lookback,
            method = estimate.Method,
            estimatedChainageM = estimate.ChainageM,
            uncertaintyM = estimate.UncertaintyM,
            location = Geo.PointAt(segment, estimate.ChainageM),
            sensors = sensorsUsed,
            inletFlow = inlet,
            note = "A search area for the crew, not an exact point: the real break is within uncertaintyM of the estimate.",
        });
    }

    [Function(nameof(RankSegmentsByPressureDrop))]
    public async Task<string> RankSegmentsByPressureDrop(
        [McpToolTrigger("rank-segments-by-pressure-drop", "Ranks the pipe segments of a district by how far their pressure sensors fell between a moment and some minutes earlier, largest drop first. The leak is normally on the segment at the top. Use it to check whether a suspected segment is really where the pressure fell most.")]
        ToolInvocationContext context,
        [McpToolProperty("districtId", "District id, for example D-01", isRequired: true)] string districtId,
        [McpToolProperty("untilUtc", "The moment to look at, ISO 8601 UTC. Normally the alarm time.", isRequired: true)] string untilUtc,
        [McpToolProperty("lookbackMinutes", "Compare with this many minutes earlier. Default 30, between 10 and 180.", isRequired: false)] int? lookbackMinutes)
    {
        if (!ToolJson.TryTime(untilUtc, "untilUtc", out var until, out var error)) return error;
        var lookback = Math.Clamp(lookbackMinutes ?? 30, 10, 180);

        var sensors = (await store.ListSensorsAsync(districtId)).Where(s => s.Metric == "pressure").ToList();
        if (sensors.Count == 0) return ToolJson.Error($"No pressure sensors found for district '{districtId}'.");
        var segments = (await store.ListSegmentsAsync(districtId)).ToDictionary(s => s.Id);

        var drops = new List<(Sensor Sensor, double DropBar)>();
        foreach (var sensor in sensors)
        {
            var readings = await store.GetReadingsAsync(sensor.Id, until.AddMinutes(-lookback), until);
            if (readings.Count >= 2) drops.Add((sensor, Math.Round(readings[0].Value - readings[^1].Value, 2)));
        }

        var ranking = drops
            .GroupBy(d => d.Sensor.SegmentId)
            .Select(g => new
            {
                segmentId = g.Key,
                street = segments.GetValueOrDefault(g.Key)?.Street,
                material = segments.GetValueOrDefault(g.Key)?.Material,
                diameterMm = segments.GetValueOrDefault(g.Key)?.DiameterMm,
                maxDropBar = g.Max(d => d.DropBar),
                sensors = g.Select(d => new { id = d.Sensor.Id, chainageM = d.Sensor.ChainageM, dropBar = d.DropBar }),
            })
            .OrderByDescending(r => r.maxDropBar)
            .ToList();
        logger.LogInformation("rank-segments-by-pressure-drop {DistrictId} until {Until} -> top {Top}", districtId, until, ranking.FirstOrDefault()?.segmentId);

        return ToolJson.Ok(new { districtId, untilUtc = until, lookbackMinutes = lookback, ranking });
    }

    [Function(nameof(ClassifySeverity))]
    public async Task<string> ClassifySeverity(
        [McpToolTrigger("classify-severity", "Classifies how serious a breach on a pipe segment is, S1 (critical) to S4 (minor), with the reasons, using the fixed rubric: breach type, customers served, critical customers (care homes, hospitals) and pipe diameter.")]
        ToolInvocationContext context,
        [McpToolProperty("segmentId", "Pipe segment id, for example SEG-0412", isRequired: true)] string segmentId,
        [McpToolProperty("breachType", "burst or leak", isRequired: true)] string breachType)
    {
        var breach = breachType?.Trim().ToLowerInvariant();
        if (breach is not ("burst" or "leak")) return ToolJson.Error("'breachType' must be 'burst' or 'leak'.");
        var segment = await store.GetSegmentAsync(segmentId);
        if (segment is null) return ToolJson.Error($"Unknown segment '{segmentId}'.");

        var assessment = SeverityRubric.Classify(breach, segment.CustomersServed, segment.CriticalCustomers, segment.DiameterMm);
        logger.LogInformation("classify-severity {SegmentId} {Breach} -> {Severity}", segmentId, breach, assessment.Severity);
        return ToolJson.Ok(new { segmentId, breachType = breach, assessment.Severity, assessment.Reasons });
    }

    /// <summary>The district inlet flow now, and what is normal for that hour (from the usage profile).</summary>
    private async Task<object?> InletFlowAsync(string districtId, DateTimeOffset from, DateTimeOffset until)
    {
        var flowSensor = (await store.ListSensorsAsync(districtId)).FirstOrDefault(s => s.Metric == "flow");
        if (flowSensor is null) return null;
        var readings = await store.GetReadingsAsync(flowSensor.Id, from, until);
        if (readings.Count == 0) return null;

        var profile = (await store.GetUsageProfilesAsync(districtId, LeakOpsTime.DayType(until)))
            .FirstOrDefault(p => p.LocalHour == LeakOpsTime.ToLocal(until).Hour);
        var observed = readings[^1].Value;
        return new
        {
            sensorId = flowSensor.Id,
            observedM3h = observed,
            earlierM3h = readings[0].Value,
            expectedAvgM3h = profile?.AvgFlowM3h,
            p95M3h = profile?.P95FlowM3h,
            excessOverExpectedM3h = profile is null ? (double?)null : Math.Round(observed - profile.AvgFlowM3h, 1),
        };
    }

    [Function(nameof(GetRepairProcedure))]
    public async Task<string> GetRepairProcedure(
        [McpToolTrigger("get-repair-procedure", "Gets the standard repair procedure for a pipe material, diameter and breach type: required technician certifications, parts (with conditions), equipment, estimated repair hours, and notes.")]
        ToolInvocationContext context,
        [McpToolProperty("material", "Pipe material: cast_iron, ductile_iron, pvc, pe, asbestos_cement or steel", isRequired: true)] string material,
        [McpToolProperty("diameterMm", "Nominal diameter in mm, for example 300", isRequired: true)] int? diameterMm,
        [McpToolProperty("breachType", "burst or leak. Default burst.", isRequired: false)] string? breachType)
    {
        if (diameterMm is null) return ToolJson.Error("'diameterMm' is required.");
        var breach = string.IsNullOrWhiteSpace(breachType) ? "burst" : breachType.Trim().ToLowerInvariant();

        var procedure = await store.FindProcedureAsync(material.Trim().ToLowerInvariant(), diameterMm.Value, breach);
        logger.LogInformation("get-repair-procedure {Material} DN{Diameter} {Breach} -> {Found}", material, diameterMm, breach, procedure?.Id);
        if (procedure is not null) return ToolJson.Ok(procedure);

        var known = (await store.ListProceduresAsync(material.Trim().ToLowerInvariant()))
            .Select(p => new { p.Id, p.BreachType, p.MinDiameterMm, p.MaxDiameterMm });
        return ToolJson.Error($"No procedure for {material} DN{diameterMm} ({breach}).", new { proceduresForThisMaterial = known });
    }
}
