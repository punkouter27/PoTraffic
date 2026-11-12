using System;
using System.Reflection;
using FluentAssertions;
using PoTraffic.Api.Features.MonitoringWindows;
using PoTraffic.Api.Features.Routes;
using Xunit;

namespace PoTraffic.Unit.Features.Routes;

/// <summary>
/// Boundary tests for the static window predicates on <see cref="PollRouteJob"/>.
/// Ensures the self-scheduling chain (a) samples during the window, (b) sleeps
/// until the next window start when outside it, and (c) stops if no day is enabled.
/// </summary>
public sealed class PollRouteJobWindowBoundaryTests
{
    private const byte Mon = 0x01;
    private const byte Tue = 0x02;
    private const byte Wed = 0x04;
    private const byte Thu = 0x08;
    private const byte Fri = 0x10;
    private const byte Sat = 0x20;
    private const byte Sun = 0x40;
    private const byte AllDays = 0x7F;
    private const byte Weekdays = 0x1F;

    // Use reflection so we exercise the private static methods without making
    // them part of the public API surface.
    private static readonly MethodInfo IsWithinWindowMethod =
        typeof(PollRouteJob).GetMethod("IsWithinWindow", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly MethodInfo NextWindowStartMethod =
        typeof(PollRouteJob).GetMethod("NextWindowStart", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static bool IsWithin(MonitoringWindow w, DateTimeOffset now) =>
        (bool)IsWithinWindowMethod.Invoke(null, [w, now])!;

    private static DateTimeOffset? NextStart(MonitoringWindow w, DateTimeOffset now) =>
        (DateTimeOffset?)NextWindowStartMethod.Invoke(null, [w, now])!;

    private static MonitoringWindow Window(byte days, TimeOnly start, TimeOnly end) => new()
    {
        Id = WindowId.New(),
        RouteId = RouteId.New(),
        DaysOfWeekMask = days,
        StartTime = start,
        EndTime = end,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// Start inclusive, end exclusive — for a plain window and for one that wraps midnight UTC.
    /// Regression for the wrap: an Eastern-time user picking 09:21–21:21 local submits a window
    /// stored as 13:21–01:21 UTC. Pre-fix the validator rejected it and the polling chain never
    /// fired; IsWithinWindow must include [start, 24:00) ∪ [00:00, end) on the same day.
    /// </summary>
    [Fact]
    public void IsWithinWindow_IsClosedOpen_ForPlainAndMidnightWrappingWindows()
    {
        MonitoringWindow plain = Window(AllDays, new TimeOnly(8, 0), new TimeOnly(9, 0));
        MonitoringWindow wrap = Window(AllDays, new TimeOnly(13, 21), new TimeOnly(1, 21));

        (MonitoringWindow Window, string NowIso, bool Expected, string Because)[] cases =
        [
            (plain, "2026-07-01T08:00:00Z", true, "08:00 exactly is inside"),
            (plain, "2026-07-01T09:00:00Z", false, "09:00 exactly is OUT (closed-open)"),
            (plain, "2026-07-01T07:59:59Z", false, "before window start"),
            (wrap, "2026-07-01T13:21:00Z", true, "13:21 exactly — start of the wrap"),
            (wrap, "2026-07-01T13:20:59Z", false, "one second before start — outside the wrap"),
            (wrap, "2026-07-01T01:20:59Z", true, "just before the wrap's end"),
            (wrap, "2026-07-01T01:21:00Z", false, "01:21 exactly is OUT (closed-open end)"),
        ];

        foreach ((MonitoringWindow w, string nowIso, bool expected, string because) in cases)
            IsWithin(w, DateTimeOffset.Parse(nowIso, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(expected, because);
    }

    /// <summary>Polling happens only on the days the user ticked — wrap-around windows included.</summary>
    [Fact]
    public void IsWithinWindow_RespectsTheDayMask_EvenAcrossAWrap()
    {
        MonitoringWindow wedThu = Window(Wed | Thu, new TimeOnly(0, 0), new TimeOnly(23, 59));
        IsWithin(wedThu, new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero))
            .Should().BeFalse("Tuesday is not in the Wednesday+Thursday mask");

        MonitoringWindow wrap = Window(Mon | Wed | Fri, new TimeOnly(22, 0), new TimeOnly(2, 0));
        IsWithin(wrap, new DateTimeOffset(2026, 7, 7, 23, 0, 0, TimeSpan.Zero))
            .Should().BeFalse("Tuesday is not in Mon|Wed|Fri mask");
        IsWithin(wrap, new DateTimeOffset(2026, 7, 8, 23, 0, 0, TimeSpan.Zero))
            .Should().BeTrue("Wednesday is enabled and 23:00 is within the wrap");
        IsWithin(wrap, new DateTimeOffset(2026, 7, 8, 1, 0, 0, TimeSpan.Zero))
            .Should().BeTrue("Wednesday is enabled and 01:00 is in the morning leg");
    }

    /// <summary>
    /// NextWindowStart returns the first strictly-future slot start on an enabled day, or null
    /// when no day is enabled so the chain stops. (Inside the window the chain uses its own
    /// "next poll" delay; NextWindowStart is consulted only once fully outside it.)
    /// </summary>
    [Fact]
    public void NextWindowStart_IsTheFirstFutureStartOnAnEnabledDay_OrNullWhenNoneIs()
    {
        // Wednesday 09:30, inside an every-day 08:00–10:00 window: today's start has passed.
        DateTimeOffset? next = NextStart(Window(AllDays, new TimeOnly(8, 0), new TimeOnly(10, 0)),
            new DateTimeOffset(2026, 7, 1, 9, 30, 0, TimeSpan.Zero));
        next.Should().NotBeNull();
        next!.Value.Date.Should().Be(new DateTime(2026, 7, 2), "the next strictly-future start is tomorrow");
        next.Value.Hour.Should().Be(8);

        // Saturday 12:00 with a Mon–Fri window rolls to Monday.
        next = NextStart(Window(Weekdays, new TimeOnly(8, 0), new TimeOnly(10, 0)),
            new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero));
        next.Should().NotBeNull();
        next!.Value.DayOfWeek.Should().Be(DayOfWeek.Monday);
        next.Value.Date.Should().Be(new DateTime(2026, 7, 6));

        NextStart(Window(0, new TimeOnly(8, 0), new TimeOnly(10, 0)), new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero))
            .Should().BeNull("no enabled days → polling chain must stop");
    }

    /// <summary>
    /// The old create-route default was 03:00 → midnight local, stored with End = 00:00. For an
    /// Eastern user its evening crosses UTC midnight; it must still be one local day — open
    /// until local midnight, gated by the local weekday, and keyed to the local date so it
    /// costs one session rather than two.
    /// </summary>
    [Fact]
    public void MidnightEndWindow_RunsUntilLocalMidnight_OnOneLocalDay()
    {
        var window = new MonitoringWindow
        {
            Id = WindowId.New(),
            RouteId = RouteId.New(),
            DaysOfWeekMask = Tue,
            StartTime = new TimeOnly(3, 0),
            EndTime = new TimeOnly(0, 0),
            TimeZoneId = "America/New_York",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        DateTimeOffset tue0300 = DateTimeOffset.Parse("2026-06-30T07:00:00Z", System.Globalization.CultureInfo.InvariantCulture);  // Tue 03:00 EDT
        DateTimeOffset tue2330 = DateTimeOffset.Parse("2026-07-01T03:30:00Z", System.Globalization.CultureInfo.InvariantCulture);  // Tue 23:30 EDT = Wed UTC
        DateTimeOffset wed0030 = DateTimeOffset.Parse("2026-07-01T04:30:00Z", System.Globalization.CultureInfo.InvariantCulture);  // Wed 00:30 EDT

        IsWithin(window, tue0300).Should().BeTrue("03:00 local is the window start");
        IsWithin(window, tue2330).Should().BeTrue("End 00:00 means until local midnight, and it is still Tuesday locally");
        IsWithin(window, wed0030).Should().BeFalse("the window closed at local midnight and Wednesday is not enabled");

        window.LocalDateAt(tue2330).Should().Be(window.LocalDateAt(tue0300),
            "both ends of one local day's window share a session date, though the UTC dates differ");
        window.LocalDateAt(tue2330).Should().Be(new DateOnly(2026, 6, 30));
    }

    private static MonitoringWindow PacificWeekdayEvening() => new()
    {
        Id = WindowId.New(),
        RouteId = RouteId.New(),
        DaysOfWeekMask = Weekdays,
        StartTime = new TimeOnly(16, 0),
        EndTime = new TimeOnly(19, 0),
        TimeZoneId = "America/Los_Angeles",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public void IsWithinWindow_WithZone_UsesTheUsersWeekdayAndWallClock()
    {
        (string NowIso, bool Expected, string Because)[] cases =
        [
            // Friday 17:00 PDT is Saturday 00:00 UTC — still Friday on the user's clock.
            ("2026-07-11T00:00:00Z", true, "Friday 17:00 PDT is inside a Mon–Fri window"),
            // Sunday 17:00 PDT is Monday 00:00 UTC — still Sunday on the user's clock.
            ("2026-07-13T00:00:00Z", false, "Sunday 17:00 PDT is outside a Mon–Fri window"),
            // Winter: 16:30 PST is 00:30 UTC; the window must not drift an hour with DST.
            ("2026-12-02T00:30:00Z", true, "Tuesday 16:30 PST is inside"),
        ];

        foreach ((string nowIso, bool expected, string because) in cases)
            IsWithin(PacificWeekdayEvening(), DateTimeOffset.Parse(nowIso, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(expected, because);
    }

    [Fact]
    public void NextWindowStart_WithZone_StaysAtLocalStartAcrossDst()
    {
        // Friday 30 Oct 2026, after the window; US clocks fall back Sunday 1 Nov.
        DateTimeOffset now = new(2026, 10, 31, 3, 0, 0, TimeSpan.Zero);
        DateTimeOffset? next = NextStart(PacificWeekdayEvening(), now);

        // Monday 2 Nov 16:00 PST = 00:00 UTC Tuesday (it was 23:00 UTC under PDT).
        next.Should().Be(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero));
    }
}
