using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoTraffic.Api.Features.Routes;
using PoTraffic.Api.Infrastructure.Storage;
using PoTraffic.Api.Infrastructure.Scheduling;

using PoTraffic.Shared.Enums;
using PoTraffic.Unit.Helpers;


namespace PoTraffic.Unit.Features.Routes;

/// <summary>
/// Unit tests for <see cref="DeleteRouteCommandHandler"/>.
/// Verifies soft-delete semantics, job cancellation, and ownership enforcement.
/// </summary>
public sealed class DeleteRouteHandlerTests
{

    private static async Task<(TableStorageContext Db, RouteId RouteId, UserId Owner)> SeedRouteAsync(string? jobChainId)
    {
        TableStorageContext db = TestDoubles.CreateDb();
        RouteId routeId = RouteId.New();
        UserId owner = UserId.New();
        db.Add(new EntityRoute
        {
            Id = routeId,
            UserId = owner,
            OriginAddress = "A",
            DestinationAddress = "B",
            Provider = (int)RouteProvider.GoogleMaps,
            MonitoringStatus = (int)MonitoringStatus.Active,
            JobChainId = jobChainId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return (db, routeId, owner);
    }

    [Fact]
    public async Task DeleteRoute_SoftDeletes_AndCancelsItsJobChain()
    {
        const string jobId = "job-42";
        (TableStorageContext db, RouteId routeId, UserId owner) = await SeedRouteAsync(jobId);
        IJobScheduler scheduler = Substitute.For<IJobScheduler>();
        var handler = new DeleteRouteCommandHandler(db, scheduler, NullLogger<DeleteRouteCommandHandler>.Instance);

        bool result = await handler.Handle(new DeleteRouteCommand(routeId, owner), CancellationToken.None);

        result.Should().BeTrue();
        EntityRoute route = db.Routes.Single(x => x.Id == routeId);
        route.MonitoringStatus.Should().Be((int)MonitoringStatus.Deleted, "route must be soft-deleted");
        route.JobChainId.Should().BeNull("JobChainId must be cleared on soft-delete");
        scheduler.Received(1).Cancel(jobId);
    }

    [Fact]
    public async Task DeleteRoute_ForAnUnknownRouteOrAnotherUsersRoute_ReturnsFalse_AndChangesNothing()
    {
        (TableStorageContext db, RouteId routeId, _) = await SeedRouteAsync(jobChainId: null);
        var handler = new DeleteRouteCommandHandler(db, Substitute.For<IJobScheduler>(), NullLogger<DeleteRouteCommandHandler>.Instance);

        (await handler.Handle(new DeleteRouteCommand(RouteId.New(), UserId.New()), CancellationToken.None))
            .Should().BeFalse("attempting to delete a non-existent route must return false");
        (await handler.Handle(new DeleteRouteCommand(routeId, UserId.New()), CancellationToken.None))
            .Should().BeFalse("a user must not be able to delete another user's route");

        db.Routes.Single(x => x.Id == routeId).MonitoringStatus
            .Should().Be((int)MonitoringStatus.Active, "route must remain intact");
    }
}
