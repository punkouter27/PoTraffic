using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoTraffic.Api.Features.MonitoringWindows;
using PoTraffic.Api.Infrastructure.Storage;
using PoTraffic.Api.Infrastructure.Scheduling;

using PoTraffic.Shared.Enums;
using PoTraffic.Unit.Helpers;

namespace PoTraffic.Unit.Features.MonitoringWindows;

/// <summary>
/// Tests for <see cref="StopWindowCommandHandler"/> lifecycle transitions.
/// Verifies session transitions to Completed and job chain is cancelled.
/// </summary>
public sealed class WindowLifecycleTests
{

    private static async Task<(TableStorageContext Db, SessionId SessionId, RouteId RouteId, UserId UserId)> SeedActiveSessionAsync(
        string? jobChainId = "job-abc-123")
    {
        TableStorageContext db = TestDoubles.CreateDb();
        UserId userId = UserId.New();
        RouteId routeId = RouteId.New();
        SessionId sessionId = SessionId.New();

        var user = new User
        {
            Id = userId,
            Email = $"user-{userId}@test.com",
            Locale = "Europe/London"
        };
        db.Add(user);

        var route = new Route
        {
            Id = routeId,
            UserId = userId,
            User = user,
            OriginAddress = "A",
            OriginCoordinates = "1.0,1.0",
            DestinationAddress = "B",
            DestinationCoordinates = "2.0,2.0",
            Provider = (int)RouteProvider.GoogleMaps,
            MonitoringStatus = (int)MonitoringStatus.Active,
            JobChainId = jobChainId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Add(route);

        db.Add(new MonitoringSession
        {
            Id = sessionId,
            RouteId = routeId,
            Route = route,
            SessionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            State = (int)SessionState.Active
        });

        await db.SaveChangesAsync();
        return (db, sessionId, routeId, userId);
    }

    [Fact]
    public async Task StopWindow_CompletesTheSession_CancelsTheChain_AndClearsItFromTheRoute()
    {
        const string jobId = "job-42";
        (TableStorageContext db, SessionId sessionId, RouteId routeId, UserId userId) =
            await SeedActiveSessionAsync(jobId);
        IJobScheduler scheduler = Substitute.For<IJobScheduler>();
        var handler = new StopWindowCommandHandler(db, scheduler, NullLogger<StopWindowCommandHandler>.Instance);

        bool result = await handler.Handle(new StopWindowCommand(sessionId, userId), CancellationToken.None);

        result.Should().BeTrue();
        db.MonitoringSessions.Single(x => x.Id == sessionId).State.Should().Be((int)SessionState.Completed,
            "StopWindowCommand must transition session to Completed state");
        scheduler.Received(1).Cancel(jobId);
        db.Routes.Single(x => x.Id == routeId).JobChainId.Should().BeNull(
            "after stopping monitoring, JobChainId should be cleared to prevent orphaned chains");
    }

    [Fact]
    public async Task StopWindow_WithNoChainOrNoSession_CancelsNothing()
    {
        // A session whose route has no job chain still stops.
        (TableStorageContext db, SessionId sessionId, _, UserId userId) = await SeedActiveSessionAsync(jobChainId: null);
        IJobScheduler scheduler = Substitute.For<IJobScheduler>();
        var handler = new StopWindowCommandHandler(db, scheduler, NullLogger<StopWindowCommandHandler>.Instance);

        (await handler.Handle(new StopWindowCommand(sessionId, userId), CancellationToken.None))
            .Should().BeTrue("session with no job chain should still stop successfully");

        // An unknown session is reported, not stopped.
        (await handler.Handle(new StopWindowCommand(SessionId.New(), UserId.New()), CancellationToken.None))
            .Should().BeFalse();

        scheduler.DidNotReceive().Cancel(Arg.Any<string>());
    }
}
