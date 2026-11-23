using FluentValidation;
using PoTraffic.Shared.Constants;

namespace PoTraffic.Client.Features.Routes;

/// <summary>The fields of the New commute form (<c>CreateRoutePage</c>).</summary>
public sealed class CreateRouteForm
{
    public string OriginAddress { get; set; } = string.Empty;
    public string DestinationAddress { get; set; } = string.Empty;
    public byte DayMask { get; set; } = 0x1F;
}

/// <summary>
/// Inline rules for <see cref="CreateRouteForm"/>. Registered by type in Program.cs
/// (<c>AddScoped&lt;IValidator&lt;CreateRouteForm&gt;, CreateRouteFormValidator&gt;</c>) and injected
/// into the page, never created with <c>new()</c> there.
/// </summary>
public sealed class CreateRouteFormValidator : AbstractValidator<CreateRouteForm>
{
    public CreateRouteFormValidator()
    {
        RuleFor(x => x.OriginAddress)
            .NotEmpty().WithMessage("Enter an origin address.")
            .MaximumLength(ValidationConstants.AddressMaxLength);
        RuleFor(x => x.DestinationAddress)
            .NotEmpty().WithMessage("Enter a destination address.")
            .MaximumLength(ValidationConstants.AddressMaxLength);
        RuleFor(x => x.DayMask)
            .Must(m => m != 0).WithMessage("Select at least one day to probe.");
    }
}
