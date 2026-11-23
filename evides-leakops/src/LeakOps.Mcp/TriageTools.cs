using LeakOps.Contracts.Logic;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace LeakOps.Mcp;

/// <summary>Tools for the Triage agent: SCADA readings, planned works, weather and normal usage.</summary>
// C# note: "(LeakOpsStore store, ILogger<TriageTools> logger)" is a primary constructor; both values are injected by DI.
public class TriageTools(LeakOpsStore store, ILogger<TriageTools> logger)
{
    [Function(nameof(ListSensors))]
    public async Task<string> ListSensors(
        [McpToolTrigger("list-sensors", "Lists the SCADA/IoT sensors of a district (pressure loggers and flow meters) with their pipe segment, position and alarm limits.")]
        ToolInvocationContext context,
        [McpToolProperty("districtId", "District id, for example D-01", isRequired: true)] string districtId)
    {
        var sensors = await store.ListSensorsAsync(districtId);
        if (sensors.Count == 0) return ToolJson.Error($"No sensors found for district '{districtId}'.");

        var segments = (await store.ListSegmentsAsync(districtId)).ToDictionary(s => s.Id);
        logger.LogInformation("list-sensors {DistrictId} -> {Count}", districtId, sensors.Count);

        return ToolJson.Ok(new
        {
            districtId,
            sensors = sensors.Select(s => new
            {
                s.Id, s.Name, s.Metric, s.Unit, s.Status, s.SegmentId, s.ChainageM, s.AlarmLow, s.AlarmHigh,
                street = segments.GetValueOrDefault(s.SegmentId)?.Street,
                material = segments.GetValueOrDefault(s.SegmentId)?.Material,
                diameterMm = segments.GetValueOrDefault(s.SegmentId)?.DiameterMm,
            }),
        });
    }

    [Function(nameof(GetSensorReadings))]
    public async Task<string> GetSensorReadings(
        [McpToolTrigger("get-sensor-readings", "Gets the readings of one sensor in a time window ending at 'untilUtc' (10-minute interval, oldest first), with a short summary and the sensor's alarm limits. Pressure is in bar, flow in m3/h.")]
        ToolInvocationContext context,
        [McpToolProperty("sensorId", "Sensor id, for example PS-101", isRequired: true)] string sensorId,
        [McpToolProperty("untilUtc", "End of the window, ISO 8601 UTC. Normally the alarm time.", isRequired: true)] string untilUtc,
        [McpToolProperty("hours", "Window length in hours. Default 3, maximum 48.", isRequired: false)] int? hours)
    {
        if (!ToolJson.TryTime(untilUtc, "untilUtc", out var until, out var error)) return error;
        var window = Math.Clamp(hours ?? 3, 1, 48);

        var sensor = await store.GetSensorAsync(sensorId);
        if (sensor is null) return ToolJson.Error($"Unknown sensor '{sensorId}'. Use list-sensors to see valid ids.");

        var from = until.AddHours(-window);
        var readings = await store.GetReadingsAsync(sensorId, from, until);
        logger.LogInformation("get-sensor-readings {SensorId} {From}..{Until} -> {Count}", sensorId, from, until, readings.Count);
        if (readings.Count == 0) return ToolJson.Error("No readings in this window.", new { sensorId, from, until });

        var first = readings[0];
        var last = readings[^1];
        return ToolJson.Ok(new
        {
            sensor = new { sensor.Id, sensor.Name, sensor.Metric, sensor.Unit, sensor.SegmentId, sensor.ChainageM, sensor.AlarmLow, sensor.AlarmHigh },
            window = new { from, until },
            summary = new
            {
                count = readings.Count,
                first = new { t = first.TimestampUtc, v = first.Value },
                last = new { t = last.TimestampUtc, v = last.Value },
                min = readings.Min(r => r.Value),
                max = readings.Max(r => r.Value),
                changeFromFirst = Math.Round(last.Value - first.Value, 2),
            },
            readings = readings.Select(r => new { t = r.TimestampUtc, v = r.Value }),
        });
    }

    [Function(nameof(GetPlannedWorks))]
    public async Task<string> GetPlannedWorks(
        [McpToolTrigger("get-planned-works", "Gets planned maintenance in a district around a moment in time (valve work, flushing, renewals) that can legitimately change pressure or flow. 'activeAtTime' tells whether the work is running at that moment.")]
        ToolInvocationContext context,
        [McpToolProperty("districtId", "District id, for example D-01", isRequired: true)] string districtId,
        [McpToolProperty("atUtc", "The moment of interest, ISO 8601 UTC. Normally the alarm time.", isRequired: true)] string atUtc,
        [McpToolProperty("windowHours", "Look this many hours before and after. Default 24, maximum 168.", isRequired: false)] int? windowHours)
    {
        if (!ToolJson.TryTime(atUtc, "atUtc", out var at, out var error)) return error;
        var window = Math.Clamp(windowHours ?? 24, 1, 168);

        var works = await store.GetPlannedWorksAsync(districtId, at.AddHours(-window), at.AddHours(window));
        logger.LogInformation("get-planned-works {DistrictId} at {At} -> {Count}", districtId, at, works.Count);

        return ToolJson.Ok(new
        {
            districtId,
            atUtc = at,
            windowHours = window,
            count = works.Count,
            works = works.Select(w => new
            {
                w.Id, w.Title, w.WorkType, w.Status, w.StartUtc, w.EndUtc, w.SegmentIds,
                w.ExpectedPressureImpactBar, w.ExpectedFlowImpact, w.Contractor, w.CustomersNotified,
                activeAtTime = w.StartUtc <= at && w.EndUtc >= at,
            }),
        });
    }

    [Function(nameof(GetWeather))]
    public async Task<string> GetWeather(
        [McpToolTrigger("get-weather", "Gets hourly weather observations for a district (temperature, precipitation, wind, frost) in a window ending at 'untilUtc', with a summary. Frost is relevant for pipe bursts.")]
        ToolInvocationContext context,
        [McpToolProperty("districtId", "District id, for example D-01", isRequired: true)] string districtId,
        [McpToolProperty("untilUtc", "End of the window, ISO 8601 UTC. Normally the alarm time.", isRequired: true)] string untilUtc,
        [McpToolProperty("hours", "Window length in hours. Default 24, maximum 72.", isRequired: false)] int? hours)
    {
        if (!ToolJson.TryTime(untilUtc, "untilUtc", out var until, out var error)) return error;
        var window = Math.Clamp(hours ?? 24, 1, 72);

        var observations = await store.GetWeatherAsync(districtId, until.AddHours(-window), until);
        logger.LogInformation("get-weather {DistrictId} until {Until} -> {Count}", districtId, until, observations.Count);
        if (observations.Count == 0) return ToolJson.Error($"No weather observations for district '{districtId}' in this window.");

        var latest = observations[^1];
        return ToolJson.Ok(new
        {
            districtId,
            summary = new
            {
                latest = new { t = latest.TimestampUtc, temperatureC = latest.TemperatureC, frost = latest.Frost },
                minTemperatureC = observations.Min(o => o.TemperatureC),
                maxTemperatureC = observations.Max(o => o.TemperatureC),
                frostHours = observations.Count(o => o.Frost),
                totalPrecipitationMm = Math.Round(observations.Sum(o => o.PrecipitationMm), 1),
            },
            observations = observations.Select(o => new { t = o.TimestampUtc, tempC = o.TemperatureC, rainMm = o.PrecipitationMm, windKmh = o.WindKmh, o.Frost }),
        });
    }

    [Function(nameof(GetUsageHistory))]
    public async Task<string> GetUsageHistory(
        [McpToolTrigger("get-usage-history", "Gets what normal looks like for a district: expected flow and pressure for the local hour and day type (weekday/weekend) of a moment in time, the full 24-hour profile, and known historical deviations (heatwaves, events, flushing) that were not leaks.")]
        ToolInvocationContext context,
        [McpToolProperty("districtId", "District id, for example D-01", isRequired: true)] string districtId,
        [McpToolProperty("atUtc", "The moment of interest, ISO 8601 UTC. Normally the alarm time.", isRequired: true)] string atUtc)
    {
        if (!ToolJson.TryTime(atUtc, "atUtc", out var at, out var error)) return error;

        var dayType = LeakOpsTime.DayType(at);
        var localHour = LeakOpsTime.ToLocal(at).Hour;
        var profile = await store.GetUsageProfilesAsync(districtId, dayType);
        if (profile.Count == 0) return ToolJson.Error($"No usage profile for district '{districtId}'.");

        var events = await store.GetUsageEventsAsync(districtId);
        var expected = profile.Single(p => p.LocalHour == localHour);
        logger.LogInformation("get-usage-history {DistrictId} at {At} -> {DayType} hour {Hour}", districtId, at, dayType, localHour);

        return ToolJson.Ok(new
        {
            districtId,
            atUtc = at,
            dayType,
            localHour,
            expectedNow = new { expected.AvgFlowM3h, expected.P95FlowM3h, expected.AvgPressureBar },
            profile = profile.Select(p => new { hour = p.LocalHour, avgFlowM3h = p.AvgFlowM3h, p95FlowM3h = p.P95FlowM3h, avgPressureBar = p.AvgPressureBar }),
            knownDeviations = events.Select(e => new { e.Id, e.StartUtc, e.EndUtc, e.Description, e.FlowChangePercent, e.PressureChangeBar }),
        });
    }
}
