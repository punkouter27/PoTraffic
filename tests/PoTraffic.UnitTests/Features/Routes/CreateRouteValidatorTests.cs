using FluentAssertions;
using FluentValidation.Results;
using PoTraffic.API.Features.Routes;
using PoTraffic.Shared.Constants;
using PoTraffic.Shared.Enums;

namespace PoTraffic.UnitTests.Features.Routes;

/// <summary>
/// Tests for <see cref="CreateRouteValidator"/> input validation rules.
/// FR-014: origin and destination must not resolve to identical coordinates.
/// </summary>
public sealed class CreateRouteValidatorTests
{
    private readonly CreateRouteValidator _validator = new();

    private static CreateRouteCommand Valid() => new(
        UserId: UserId.New(),
        OriginAddress: "Baker Street, London",
        DestinationAddress: "Waterloo Station, London",
        Provider: RouteProvider.GoogleMaps,
        StartTime: "07:30",
        EndTime: "09:00",
        DaysOfWeekMask: 0x1F); // Mon–Fri

    [Fact]
    public void Validator_RejectsEachInvalidField_OnTheFieldItConcerns()
    {
        (CreateRouteCommand Command, string Property, string Because)[] cases =
        [
            (Valid() with { OriginAddress = "" }, nameof(CreateRouteCommand.OriginAddress), "origin is required"),
            (Valid() with { DestinationAddress = "" }, nameof(CreateRouteCommand.DestinationAddress), "destination is required"),
            (Valid() with { OriginAddress = new string('A', ValidationConstants.AddressMaxLength + 1) },
                nameof(CreateRouteCommand.OriginAddress), "origin is capped at the max address length"),
            (Valid() with { Provider = (RouteProvider)99 }, nameof(CreateRouteCommand.Provider), "provider must be a defined enum value"),
            (Valid() with { StartTime = "7:00" }, nameof(CreateRouteCommand.StartTime), "times need a leading zero (HH:mm)"),
            (Valid() with { StartTime = "09:00", EndTime = "09:00" }, nameof(CreateRouteCommand.EndTime), "a zero-length window never fires"),
            (Valid() with { DaysOfWeekMask = 0 }, nameof(CreateRouteCommand.DaysOfWeekMask), "at least one day must be selected"),
        ];

        foreach ((CreateRouteCommand command, string property, string because) in cases)
        {
            ValidationResult result = _validator.Validate(command);
            result.Errors.Should().Contain(e => e.PropertyName == property, because);
        }
    }

    /// <summary>
    /// Times cross the wire as UTC, so an ordinary local window (15:00–21:00 EDT) arrives as
    /// 19:00–01:00 UTC. PollRouteJob.IsWithinWindow evaluates that correctly, so the validator
    /// must not reject it — the "start now, end six hours later" default depends on this.
    /// </summary>
    [Fact]
    public void Validator_AcceptsAValidSchedule_IncludingOneThatWrapsMidnightUtc()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
        _validator.Validate(Valid() with { StartTime = "19:00", EndTime = "01:00" }).IsValid
            .Should().BeTrue("a window that wraps midnight UTC is legitimate");
    }
}
