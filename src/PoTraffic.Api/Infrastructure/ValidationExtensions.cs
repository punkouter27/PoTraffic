using FluentValidation;
using PoTraffic.Api.Features.Account;
using PoTraffic.Api.Features.Alerts;

namespace PoTraffic.Api.Infrastructure;

internal static class ValidationExtensions
{
    /// <summary>
    /// Every FluentValidation validator, registered BY TYPE (no assembly scanning, AGENTS.md
    /// Components): each handler takes its <see cref="IValidator{T}"/> from DI and calls
    /// <c>ValidateAndThrowAsync</c>; GlobalExceptionHandler maps the ValidationException to 422.
    /// A new validator is added here, next to its command.
    /// </summary>
    internal static IServiceCollection AddPoTrafficValidators(this IServiceCollection services)
    {
        services.AddScoped<IValidator<SetTimeZoneCommand>, SetTimeZoneValidator>();
        services.AddScoped<IValidator<UpdateProfileCommand>, UpdateProfileValidator>();
        services.AddScoped<IValidator<SetUserDailyQuotaCommand>, SetUserDailyQuotaValidator>();
        services.AddScoped<IValidator<UpdateSystemConfigurationCommand>, UpdateSystemConfigurationValidator>();
        services.AddScoped<IValidator<SavePushSubscriptionCommand>, SavePushSubscriptionValidator>();
        services.AddScoped<IValidator<CreateWindowCommand>, CreateWindowValidator>();
        services.AddScoped<IValidator<CreateRouteCommand>, CreateRouteValidator>();
        services.AddScoped<IValidator<CreateSampleRouteCommand>, CreateSampleRouteValidator>();
        services.AddScoped<IValidator<SetArriveByCommand>, SetArriveByValidator>();
        services.AddScoped<IValidator<SetRouteAddressesCommand>, SetRouteAddressesValidator>();
        services.AddScoped<IValidator<SetRouteNameCommand>, SetRouteNameValidator>();
        return services;
    }
}
