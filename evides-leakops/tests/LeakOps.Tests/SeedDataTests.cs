using LeakOps.Contracts;
using LeakOps.Seeder;
using Xunit;
using static LeakOps.Contracts.Logic.Geo;

namespace LeakOps.Tests;

public class SeedDataTests
{
    private static readonly SeedData Data = SeedDataLoader.Load(SeedDataLoader.FindDataDirectory());

    private static Scenario Burst => Data.Scenarios.Single(s => s.Id == "scn-burst-cast-iron");
    private static Scenario FalseAlarm => Data.Scenarios.Single(s => s.Id == "scn-false-alarm-planned-works");

    [Fact]
    public void References_between_files_resolve()
    {
        var segments = Data.PipeSegments.ToDictionary(s => s.Id);
        var valves = Data.Valves.ToDictionary(v => v.Id);
        var warehouses = Data.Warehouses.Select(w => w.Id).ToHashSet();
        var suppliers = Data.Suppliers.Select(s => s.Id).ToHashSet();

        Assert.All(Data.Sensors, s => Assert.Contains(s.SegmentId, segments.Keys));
        Assert.All(Data.Valves, v => Assert.Contains(v.SegmentId, segments.Keys));
        Assert.All(Data.PlannedWorks, w => Assert.All(w.SegmentIds, id => Assert.Contains(id, segments.Keys)));
        Assert.All(Data.PipeSegments, seg => Assert.All(seg.IsolationValveIds, id =>
        {
            Assert.True(valves.ContainsKey(id), $"{seg.Id} references unknown valve {id}");
            Assert.Equal(seg.Id, valves[id].SegmentId);
        }));
        Assert.All(Data.Inventory, i => Assert.Contains(i.WarehouseId, warehouses));
        Assert.All(Data.Inventory, i => Assert.Contains(i.SupplierId, suppliers));
    }

    [Fact]
    public void Repair_procedures_only_use_known_certifications_parts_and_equipment()
    {
        var skus = Data.Inventory.Select(i => i.Id).ToHashSet();
        var equipmentTypes = Data.Inventory.Where(i => i.Kind == "equipment").Select(i => i.EquipmentType).ToHashSet();

        foreach (var procedure in Data.RepairProcedures)
        {
            Assert.All(procedure.RequiredCertifications, c =>
                Assert.True(Data.Technicians.Any(t => t.Certifications.Contains(c)), $"{procedure.Id}: nobody holds {c}"));
            Assert.All(procedure.Parts, p => Assert.Contains(p.Sku, skus));
            Assert.All(procedure.Equipment, e => Assert.Contains(e.EquipmentType, equipmentTypes));
        }
    }

    [Fact]
    public void Geometry_is_consistent_with_chainage()
    {
        var segments = Data.PipeSegments.ToDictionary(s => s.Id);

        foreach (var segment in segments.Values)
        {
            var straight = Metres(segment.Start, segment.End);
            Assert.InRange(segment.LengthM, straight * 0.9, straight * 1.1);
        }
        foreach (var sensor in Data.Sensors)
            Assert.True(Metres(sensor.Location, PointAt(segments[sensor.SegmentId], sensor.ChainageM)) < 25, $"{sensor.Id} is not on its segment");
        foreach (var valve in Data.Valves)
            Assert.True(Metres(valve.Location, PointAt(segments[valve.SegmentId], valve.ChainageM)) < 25, $"{valve.Id} is not on its segment");
    }

    [Fact]
    public void Timestamps_are_utc_and_generation_is_repeatable()
    {
        Assert.All(Data.SensorReadings, r => Assert.Equal(TimeSpan.Zero, r.TimestampUtc.Offset));
        Assert.All(Data.PlannedWorks, w => Assert.Equal(TimeSpan.Zero, w.StartUtc.Offset));
        Assert.All(Data.Technicians, t => Assert.All(t.Shifts, s => Assert.Equal(TimeSpan.Zero, s.StartUtc.Offset)));

        var again = SimulatedDataGenerator.GenerateReadings(Data.Sensors, Data.Simulation);
        Assert.True(again.SequenceEqual(Data.SensorReadings));
    }

    [Fact]
    public void Anomalies_start_from_the_normal_value_and_data_ends_at_the_alarm()
    {
        foreach (var sim in Data.Simulation.Sensors)
        {
            var first = sim.Anomaly!.OrderBy(p => p.TimeUtc).First();
            var normal = SimulatedDataGenerator.NormalValue(sim, first.TimeUtc);
            Assert.True(Math.Abs(first.Value - normal) <= 4 * sim.NoiseSd + 0.05, $"{sim.SensorId}: anomaly starts at {first.Value}, normal is {normal:F2}");
        }

        foreach (var scenario in Data.Scenarios)
        {
            var alarm = scenario.Alarm;
            var readings = Data.SensorReadings.Where(r => r.SensorId == alarm.SensorId).OrderBy(r => r.TimestampUtc).ToList();
            var sensor = Data.Sensors.Single(s => s.Id == alarm.SensorId);

            Assert.Equal(alarm.TriggeredAtUtc, readings[^1].TimestampUtc);   // no data from the future
            Assert.Equal(alarm.ObservedValue, readings[^1].Value, 0.001);
            Assert.True(readings[^1].Value < sensor.AlarmLow);
            Assert.Equal(sensor.AlarmLow, alarm.ThresholdLow);
            Assert.True(readings.SkipLast(1).All(r => r.Value >= sensor.AlarmLow), "alarm limit crossed before the alarm fired");
        }
    }

    [Fact]
    public void Burst_scenario_looks_like_a_real_leak()
    {
        var at = Burst.Alarm.TriggeredAtUtc;
        var before = at.AddMinutes(-30);
        double Value(string sensor, DateTimeOffset t) => Data.SensorReadings.Single(r => r.SensorId == sensor && r.TimestampUtc == t).Value;

        var dropWest = Value("PS-101", before) - Value("PS-101", at);
        var dropEast = Value("PS-103", before) - Value("PS-103", at);
        Assert.True(dropWest > dropEast && dropEast > 0.5, "the sensor nearest the break should drop most");

        var normalNightFlow = Data.UsageProfiles.Single(p => p.DistrictId == "D-01" && p.DayType == "weekend" && p.LocalHour == 4).AvgFlowM3h;
        Assert.True(Value("FS-101", at) > 2.5 * normalNightFlow);

        Assert.DoesNotContain(Data.PlannedWorks, w => w.DistrictId == "D-01" && w.StartUtc <= at && w.EndUtc >= at);
        Assert.True(Data.WeatherObservations.Where(w => w.DistrictId == "D-01").OrderBy(w => w.TimestampUtc).Last().Frost);
    }

    [Fact]
    public void False_alarm_scenario_is_explained_by_planned_works()
    {
        var at = FalseAlarm.Alarm.TriggeredAtUtc;
        var sensor = Data.Sensors.Single(s => s.Id == FalseAlarm.Alarm.SensorId);
        double Flow(DateTimeOffset t) => Data.SensorReadings.Single(r => r.SensorId == "FS-201" && r.TimestampUtc == t).Value;

        Assert.Contains(Data.PlannedWorks, w => w.Status == "in_progress" && w.StartUtc <= at && w.EndUtc >= at && w.SegmentIds.Contains(sensor.SegmentId));
        Assert.True(Flow(at) < Flow(at.AddMinutes(-30)), "a closure reduces inflow; a leak would raise it");
        Assert.False(Data.WeatherObservations.Where(w => w.DistrictId == "D-02").OrderBy(w => w.TimestampUtc).Last().Frost);
    }

    [Fact]
    public void Nearest_available_technician_lacks_the_certification_for_the_burst()
    {
        var at = Burst.Alarm.TriggeredAtUtc;
        var segment = Data.PipeSegments.Single(s => s.Id == Burst.GroundTruth.SegmentId);
        var procedure = Data.RepairProcedures.Single(p =>
            p.Material == segment.Material && segment.DiameterMm >= p.MinDiameterMm && segment.DiameterMm <= p.MaxDiameterMm && p.BreachType == Burst.GroundTruth.BreachType);
        var site = PointAt(segment, Burst.GroundTruth.ChainageM!.Value);

        var available = Data.Technicians
            .Where(t => t.Shifts.Any(s => s.StartUtc <= at && s.EndUtc > at) && !t.BusyBlocks.Any(b => b.StartUtc <= at && b.EndUtc > at))
            .OrderBy(t => Metres(t.Location, site))
            .ToList();
        var qualified = available.Where(t => procedure.RequiredCertifications.All(t.Certifications.Contains)).ToList();

        Assert.NotEmpty(qualified);
        Assert.NotEqual(available[0].Id, qualified[0].Id);
        Assert.Equal("T-02", qualified[0].Id);
    }
}
