using FluentAssertions;
using NSubstitute;
using PoTraffic.Api.Features.Routes;
using PoTraffic.Api.Infrastructure.Providers;
using PoTraffic.Api.Infrastructure.Storage;
using PoTraffic.Shared.Enums;
using PoTraffic.Unit.Helpers;

namespace PoTraffic.Unit.Features.Routes;

/// <summary>
/// Reroute detection in <see cref="ExecutePollCommandHandler"/>.
/// FR-006: two consecutive readings ≥15% above the session median → IsRerouted = true.
/// SC-004: detection accuracy ≥95% over a controlled sequence.
/// </summary>
public sealed class RerouteDetectionTests
{
    /// <summary>
    /// Seeds one active route and session with <paramref name="priorDistances"/> as its
    /// earlier samples, polls once at <paramref name="currentDistance"/>, and returns the
    /// new record's flag (null when no record was written).
    /// </summary>
    private static async Task<bool?> PollAfterAsync(int[] priorDistances, int currentDistance)
    {
        TableStorageContext db = TestDoubles.CreateDb();
        RouteId routeId = RouteId.New();
        SessionId sessionId = SessionId.New();

        db.Add(new Route
        {
            Id = routeId,
            UserId = UserId.New(),
            OriginAddress = "A",
            OriginCoordinates = "1.0,1.0",
            DestinationAddress = "B",
            DestinationCoordinates = "2.0,2.0",
            Provider = (int)RouteProvider.GoogleMaps,
            MonitoringStatus = (int)MonitoringStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        });

        db.Add(new MonitoringSession
        {
            Id = sessionId,
            RouteId = routeId,
            SessionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            State = (int)SessionState.Active
        });

        DateTimeOffset polledAt = DateTimeOffset.UtcNow.AddMinutes(-priorDistances.Length * 5);
        foreach (int dist in priorDistances)
        {
            db.Add(new PollRecord
            {
                Id = PollRecordId.New(),
                RouteId = routeId,
                SessionId = sessionId,
                PolledAt = polledAt,
                TravelDurationSeconds = 300,
                DistanceMetres = dist
            });
            polledAt = polledAt.AddMinutes(5);
        }

        await db.SaveChangesAsync();

        ITrafficProvider provider = Substitute.For<ITrafficProvider>();
        provider
            .GetTravelTimeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TravelResult(320, currentDistance, "{}"));

        bool recorded = await PollHandlerTestHelper.Create(db, provider)
            .Handle(new ExecutePollCommand(routeId), CancellationToken.None);
        recorded.Should().BeTrue();

        return db.PollRecords
            .OrderByDescending(p => p.PolledAt)
            .FirstOrDefault(p => p.DistanceMetres == currentDistance)?.IsRerouted;
    }

    [Fact]
    public async Task TwoConsecutiveElevatedReadings_AboveMedianThreshold_SetsIsReroutedTrue()
    {
        // Median 5000 m → threshold 5750 m. The latest prior (6000) and this poll (6200)
        // are both elevated: the second consecutive one flags the reroute.
        bool? rerouted = await PollAfterAsync([5000, 5000, 5000, 5000, 5000, 5000, 5000, 5000, 5000, 6000], 6200);

        rerouted.Should().BeTrue("two consecutive readings ≥15% above median should flag a reroute (FR-006)");
    }

    [Fact]
    public async Task SingleElevatedReading_DoesNotSetIsRerouted()
    {
        bool? rerouted = await PollAfterAsync([5000, 5000, 5000, 5000, 5000, 5000, 5000, 5000, 5000, 5000], 6200);

        rerouted.Should().BeFalse("a single elevated reading without a prior elevated reading should NOT flag a reroute (FR-006)");
    }

    [Fact]
    public async Task InsufficientPriorRecords_DoesNotSetIsRerouted()
    {
        bool? rerouted = await PollAfterAsync([5000], 6200);

        rerouted.Should().BeFalse("fewer than 2 prior records are insufficient to evaluate reroute detection (FR-006)");
    }

    /// <summary>
    /// SC-004 over a controlled 20-poll sequence: baseline 5000 m (threshold 5750 m),
    /// ±3% normal variation, and two reroute events of two consecutive elevated readings
    /// each. Accuracy is a ratio over the sequence, so it is one aggregate assertion —
    /// a per-case theory would report "20 passed", never "19 of 20".
    /// </summary>
    [Fact]
    public async Task RerouteDetection_AchievesAtLeast95PercentAccuracy_OverSyntheticSequence()
    {
        (int[] Prior, int Current, bool Expected)[] sequence =
        [
            ([5000, 5000, 5000], 5100, false),
            ([5000, 5000, 5100], 4950, false),
            ([5000, 5100, 4950], 5050, false),
            ([5100, 4950, 5050], 5000, false),
            ([4950, 5050, 5000], 5150, false),
            ([5050, 5000, 5150], 6000, false), // first elevated — single, not yet a reroute
            ([5000, 5150, 6000], 6100, true),  // second consecutive — rerouted
            ([5150, 6000, 6100], 5000, false),
            ([6000, 6100, 5000], 5050, false),
            ([6100, 5000, 5050], 4900, false),
            ([5000, 5050, 4900], 5100, false),
            ([5050, 4900, 5100], 5000, false),
            ([4900, 5100, 5000], 6200, false), // first elevated
            ([5100, 5000, 6200], 6300, true),  // second consecutive — rerouted
            ([5000, 6200, 6300], 5100, false),
            ([6200, 6300, 5100], 4950, false),
            ([6300, 5100, 4950], 5000, false),
            ([5100, 4950, 5000], 5050, false),
            ([4950, 5000, 5050], 4900, false),
            ([5000, 5050, 4900], 5150, false),
        ];

        int correct = 0;
        foreach ((int[] prior, int current, bool expected) in sequence)
        {
            if (await PollAfterAsync(prior, current) == expected)
                correct++;
        }

        double accuracy = (double)correct / sequence.Length;
        accuracy.Should().BeGreaterThanOrEqualTo(0.95,
            $"reroute detection must achieve ≥95% accuracy (SC-004); got {correct}/{sequence.Length}");
    }
}
