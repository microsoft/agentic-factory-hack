using LeakOps.Contracts;
using LeakOps.Contracts.Logic;
using LeakOps.Seeder;
using Xunit;

namespace LeakOps.Tests;

public class LogicTests
{
    private static readonly SeedData Data = SeedDataLoader.Load(SeedDataLoader.FindDataDirectory());
    private static readonly DateTimeOffset BurstAlarm = new(2026, 11, 14, 3, 10, 0, TimeSpan.Zero);

    [Fact]
    public void Timestamps_with_any_offset_become_the_same_utc_instant_and_text_without_offset_is_utc()
    {
        Assert.True(LeakOpsTime.TryParseUtc("2026-11-14T03:10:00Z", out var zulu));
        Assert.True(LeakOpsTime.TryParseUtc("2026-11-14T04:10:00+01:00", out var local));
        Assert.True(LeakOpsTime.TryParseUtc("2026-11-14T03:10:00", out var bare));

        Assert.Equal(zulu, local);
        Assert.Equal(zulu, bare);
        Assert.Equal(TimeSpan.Zero, local.Offset);   // Cosmos compares timestamps as text, so the offset must be +00:00
        Assert.False(LeakOpsTime.TryParseUtc("not a date", out _));
        Assert.False(LeakOpsTime.TryParseUtc(null, out _));
    }

    [Fact]
    public void Day_type_follows_local_time_not_utc()
    {
        // Friday 23:30 UTC is already Saturday 00:30 in the Netherlands.
        Assert.Equal("weekend", LeakOpsTime.DayType(new DateTimeOffset(2026, 11, 13, 23, 30, 0, TimeSpan.Zero)));
        Assert.Equal("weekday", LeakOpsTime.DayType(new DateTimeOffset(2026, 11, 13, 22, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Travel_is_slower_in_rush_hour_than_at_night_and_road_distance_exceeds_straight_line()
    {
        var from = new GeoPoint(51.8180, 4.3500);
        var to = new GeoPoint(51.8608, 4.3303);
        var night = TravelEstimator.Estimate(from, to, BurstAlarm);
        var rush = TravelEstimator.Estimate(from, to, new DateTimeOffset(2026, 11, 13, 7, 30, 0, TimeSpan.Zero));   // 08:30 local, Friday

        Assert.True(rush.Minutes > night.Minutes);
        Assert.Equal("rush hour", rush.Traffic);
        Assert.Contains("night", night.Traffic);
        Assert.True(night.DistanceKm > Geo.Metres(from, to) / 1000.0);
    }

    [Fact]
    public void Roster_at_the_burst_alarm_shows_who_is_available_busy_off_shift_or_uncertified()
    {
        var required = Data.RepairProcedures.Single(p => p.Id == "RP-CI-300-400-BURST").RequiredCertifications;
        var site = Geo.PointAt(Data.PipeSegments.Single(s => s.Id == "SEG-0412"), 160);
        TechnicianStatus Status(string id) =>
            WorkforceAvailability.Evaluate(Data.Technicians.Single(t => t.Id == id), BurstAlarm, required, site);

        var qualified = Status("T-02");
        Assert.True(qualified.Available && qualified.HasAllRequiredCertifications);

        var nearestButUncertified = Status("T-01");
        Assert.True(nearestButUncertified.Available);
        Assert.Contains("CI-REPAIR-L3", nearestButUncertified.MissingCertifications);
        Assert.True(nearestButUncertified.EstimatedTravelMinutes < qualified.EstimatedTravelMinutes);

        var busy = Status("T-03");
        Assert.True(busy.OnShift);
        Assert.False(busy.Available);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 4, 30, 0, TimeSpan.Zero), busy.BusyUntilUtc);

        var offShift = Status("T-04");
        Assert.False(offShift.OnShift);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 5, 0, 0, TimeSpan.Zero), offShift.NextShiftStartsUtc);
    }

    [Fact]
    public void Local_time_is_amsterdam_time_including_daylight_saving()
    {
        var winter = LeakOpsTime.ToLocal(new DateTimeOffset(2026, 11, 14, 3, 10, 0, TimeSpan.Zero));
        var summer = LeakOpsTime.ToLocal(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal((4, 1), (winter.Hour, winter.Offset.Hours));    // CET, UTC+1
        Assert.Equal((14, 2), (summer.Hour, summer.Offset.Hours));   // CEST, UTC+2
        Assert.Equal("weekend", LeakOpsTime.DayType(new DateTimeOffset(2026, 7, 4, 22, 30, 0, TimeSpan.Zero)));   // 00:30 on Sunday 5 July in Amsterdam
    }

    [Fact]
    public void Stock_availability_accounts_for_reservations_and_equipment_that_is_not_back_yet()
    {
        var warehouses = Data.Warehouses.ToDictionary(w => w.Id);
        var suppliers = Data.Suppliers.ToDictionary(s => s.Id);
        var clamp = Data.Inventory.Single(i => i.Id == "CLP-CI-300");
        var bigExcavator = Data.Inventory.Single(i => i.Id == "EQP-EXC-14T");

        var before = StockViews.Create(clamp, BurstAlarm, warehouses, suppliers);
        var afterReserving = StockViews.Create(clamp with { QuantityReserved = 1 }, BurstAlarm, warehouses, suppliers);
        Assert.Equal(2, before.QuantityAvailable);
        Assert.Equal(1, afterReserving.QuantityAvailable);
        Assert.True(afterReserving.BelowReorderLevel);

        Assert.Equal(0, StockViews.Create(bigExcavator, BurstAlarm, warehouses, suppliers).QuantityAvailable);
        Assert.Equal(1, StockViews.Create(bigExcavator, new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero), warehouses, suppliers).QuantityAvailable);
    }

    [Fact]
    public void Express_orders_cost_more_arrive_sooner_and_large_totals_need_approval()
    {
        var valve = Data.Inventory.Single(i => i.Id == "VLV-GATE-300");
        var supplier = Data.Suppliers.Single(s => s.Id == valve.SupplierId);

        var standard = Procurement.BuildOrder(valve, supplier, "INC-1", 1, express: false, "isolation valve failed", BurstAlarm);
        var express = Procurement.BuildOrder(valve, supplier, "INC-1", 1, express: true, "isolation valve failed", BurstAlarm);

        Assert.Equal(2150.00, standard.TotalEur);
        Assert.Equal("ordered", standard.Status);
        Assert.Equal(BurstAlarm.AddDays(10), standard.ExpectedDeliveryUtc);

        Assert.Equal(3010.00, express.TotalEur);   // 40% express surcharge
        Assert.Equal("pending_approval", express.Status);
        Assert.Equal(BurstAlarm.AddHours(72), express.ExpectedDeliveryUtc);
        Assert.Equal("PO-INC-1-VLV-GATE-300", express.Id);
    }
}
