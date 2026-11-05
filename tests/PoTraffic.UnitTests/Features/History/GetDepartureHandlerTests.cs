using FluentAssertions;
using PoTraffic.API.Features.History;
using PoTraffic.API.Infrastructure.Storage;
using PoTraffic.UnitTests.Helpers;

namespace PoTraffic.UnitTests.Features.History;

/// <summary>
/// Tests for <see cref="GetDepartureQueryHandler"/>'s quickest-slot rule (FR-009) and its
/// ownership guard. The leave-by plan has its own tests in <see cref="DeparturePlanTests"/>.
/// </summary>
public sealed class GetDepartureHandlerTests
{
    private static int At(int h, int m) => h * 60 + m;

    [Fact]
    public async Task Handle_UnknownRoute_ReturnsNull()
    {
        TableStorageContext db = TestDoubles.CreateDb();
        var handler = new GetDepartureQueryHandler(db);

        var result = await handler.Handle(
            new GetDepartureQuery(RouteId.New(), UserId.Empty, "Wednesday"),
            CancellationToken.None);

        result.Should().BeNull("a route the caller does not own (or that does not exist) says nothing");
    }

    [Fact]
    public void BestSlot_PicksEarliestSlotWithinFivePercentOfQuickest()
    {
        (int, int)[] samples =
        [
            (At(7, 0), 40 * 60),   // 07:00 slow
            (At(7, 10), 30 * 60),  // 07:10 within 5% of the quickest — earliest such slot
            (At(7, 20), 29 * 60),  // 07:20 quickest
        ];

        var slot = GetDepartureQueryHandler.BestSlot(samples);

        slot.Should().NotBeNull();
        slot!.Bucket.Should().Be(At(7, 10));
        slot.MeanSeconds.Should().Be(29 * 60, "the prediction is the day's quickest mean");
    }

    [Fact]
    public void BestSlot_WithNoSamples_ReturnsNull() =>
        GetDepartureQueryHandler.BestSlot([]).Should().BeNull();
}
