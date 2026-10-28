using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoTraffic.API.Infrastructure.Providers;
using PoTraffic.API.Infrastructure.Scheduling;
using PoTraffic.API.Infrastructure.Storage;
using PoTraffic.Shared.Constants;
using PoTraffic.Shared.Enums;
using PoTraffic.UnitTests.Helpers;

namespace PoTraffic.UnitTests.Features.Routes;

/// <summary>
/// The route page's Schedule-tab settings: replacing a schedule, pausing and resuming,
/// and editing addresses — plus the sample route staying out of spend.
/// </summary>
public sealed class RouteSettingsHandlerTests
{
    private static async Task<(TableStorageContext Db, EntityRoute Route, MonitoringWindow Window)> SeedAsync(
        string? jobChainId = null)
    {
        TableStorageContext db = TestDoubles.CreateDb();
        var user = new User { Id = UserId.New(), Email = "settings@test.com", Locale = "en-US" };
        db.Add(user);

        var route = new EntityRoute
        {
            Id = RouteId.New(),
            UserId = user.Id,
            User = user,
            OriginAddress = "1 Main St",
            OriginCoordinates = "1.0,1.0",
            DestinationAddress = "2 Main St",
            DestinationCoordinates = "2.0,2.0",
            Provider = (int)RouteProvider.GoogleMaps,
            MonitoringStatus = (int)MonitoringStatus.Active,
            JobChainId = jobChainId,
            PathPolyline = "old-shape",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var window = new MonitoringWindow
        {
            Id = WindowId.New(),
            RouteId = route.Id,
            StartTime = new TimeOnly(7, 0),
            EndTime = new TimeOnly(9, 0),
            DaysOfWeekMask = 0x1F,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        route.Windows.Add(window);
        db.Add(route);
        await db.SaveChangesAsync();
        return (db, route, window);
    }

    // ── Replacing a schedule ────────────────────────────────────────────

    [Fact]
    public async Task CreateWindow_ReplacingTheActiveWindow_SwapsItInOneSave()
    {
        (TableStorageContext db, EntityRoute route, MonitoringWindow old) = await SeedAsync();
        var handler = new CreateWindowCommandHandler(db, NullLogger<CreateWindowCommandHandler>.Instance);

        CreateWindowResult result = await handler.Handle(new CreateWindowCommand(
            route.Id, route.UserId, new TimeOnly(3, 0), TimeOnly.MinValue, 0x7F, null, old.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        old.IsActive.Should().BeFalse();
        db.MonitoringWindows.Where(w => w.RouteId == route.Id && w.IsActive)
            .Should().ContainSingle(w => w.Id == result.WindowId);
    }

    [Fact]
    public async Task CreateWindow_RejectedReplacement_LeavesTheOldScheduleActive()
    {
        (TableStorageContext db, EntityRoute route, MonitoringWindow old) = await SeedAsync();
        var handler = new CreateWindowCommandHandler(db, NullLogger<CreateWindowCommandHandler>.Instance);

        // Start == End is rejected by the validator — before this fix the client had
        // already deleted the old window by then, leaving the route with no schedule.
        Func<Task> act = () => handler.Handle(new CreateWindowCommand(
            route.Id, route.UserId, new TimeOnly(8, 0), new TimeOnly(8, 0), 0x7F, null, old.Id), CancellationToken.None);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        old.IsActive.Should().BeTrue();
    }

    // ── Pause / resume ──────────────────────────────────────────────────

    [Fact]
    public async Task Pause_StopsTheChain_AndResume_RearmsIt()
    {
        (TableStorageContext db, EntityRoute route, _) = await SeedAsync(jobChainId: "job-old");
        IJobScheduler scheduler = Substitute.For<IJobScheduler>();
        scheduler.Enqueue(Arg.Any<Expression<Func<Task>>>()).Returns("job-new");
        var handler = new SetRoutePausedHandler(db, scheduler);

        (await handler.Handle(new SetRoutePausedCommand(route.Id, route.UserId, true), CancellationToken.None)).Should().BeTrue();
        route.MonitoringStatus.Should().Be((int)MonitoringStatus.Paused);
        route.JobChainId.Should().BeNull();
        scheduler.Received(1).Cancel("job-old");

        (await handler.Handle(new SetRoutePausedCommand(route.Id, route.UserId, false), CancellationToken.None)).Should().BeTrue();
        route.MonitoringStatus.Should().Be((int)MonitoringStatus.Active);
        route.JobChainId.Should().Be("job-new");
    }

    [Fact]
    public async Task Pause_TheSampleRoute_IsRefused()
    {
        (TableStorageContext db, EntityRoute route, _) = await SeedAsync();
        route.IsSample = true;
        var handler = new SetRoutePausedHandler(db, Substitute.For<IJobScheduler>());

        (await handler.Handle(new SetRoutePausedCommand(route.Id, route.UserId, true), CancellationToken.None)).Should().BeFalse();
    }

    // ── Editing addresses ───────────────────────────────────────────────

    [Fact]
    public async Task SetAddresses_GeocodesOnlyTheChangedOne_AndDropsTheCachedShape()
    {
        (TableStorageContext db, EntityRoute route, _) = await SeedAsync();
        ITrafficProvider provider = Substitute.For<ITrafficProvider>();
        provider.GeocodeAsync("9 Oak Ave", Arg.Any<CancellationToken>()).Returns("3.0,3.0");
        var handler = new SetRouteAddressesHandler(db, provider, NullLogger<SetRouteAddressesHandler>.Instance);

        SetRouteAddressesResult result = await handler.Handle(
            new SetRouteAddressesCommand(route.Id, route.UserId, "1 Main St", "9 Oak Ave"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await provider.Received(1).GeocodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        route.DestinationAddress.Should().Be("9 Oak Ave");
        route.DestinationCoordinates.Should().Be("3.0,3.0");
        route.OriginCoordinates.Should().Be("1.0,1.0");
        route.PathPolyline.Should().BeNull();
    }

    [Fact]
    public async Task SetAddresses_ToTheSamePlace_IsRejected_AndNothingChanges()
    {
        (TableStorageContext db, EntityRoute route, _) = await SeedAsync();
        ITrafficProvider provider = Substitute.For<ITrafficProvider>();
        provider.GeocodeAsync("1 Main Street", Arg.Any<CancellationToken>()).Returns("2.0,2.0");
        var handler = new SetRouteAddressesHandler(db, provider, NullLogger<SetRouteAddressesHandler>.Instance);

        SetRouteAddressesResult result = await handler.Handle(
            new SetRouteAddressesCommand(route.Id, route.UserId, "1 Main Street", "2 Main St"), CancellationToken.None);

        result.ErrorCode.Should().Be(RouteErrorCodes.SameCoordinates);
        route.OriginAddress.Should().Be("1 Main St");
    }

    // ── Spend ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SpendingCap_LeavesTheSampleRoutesGeneratedHistoryOut()
    {
        (TableStorageContext db, EntityRoute real, _) = await SeedAsync();
        var sample = new EntityRoute
        {
            Id = RouteId.New(),
            UserId = real.UserId,
            OriginAddress = "Pier",
            DestinationAddress = "Grand",
            IsSample = true,
            MonitoringStatus = (int)MonitoringStatus.Paused,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Add(sample);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        db.Add(new PollRecord { Id = PollRecordId.New(), RouteId = real.Id, PolledAt = now, TravelDurationSeconds = 600 });
        for (int i = 0; i < 5; i++)
            db.Add(new PollRecord { Id = PollRecordId.New(), RouteId = sample.Id, PolledAt = now, TravelDurationSeconds = 600 });
        await db.SaveChangesAsync();

        SpendingCap.PollsThisMonth(db, now).Should().Be(1);
    }
}
