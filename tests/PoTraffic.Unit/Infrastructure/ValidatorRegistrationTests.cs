using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PoTraffic.Api.Infrastructure;

namespace PoTraffic.Unit.Infrastructure;

/// <summary>
/// Validators are registered BY TYPE (AddScoped&lt;IValidator&lt;X&gt;, XValidator&gt;), never by assembly
/// scanning, and handlers take them from DI. This keeps the list complete: every validator in the
/// API assembly is registered, scoped, under its own IValidator&lt;T&gt;.
/// </summary>
public sealed class ValidatorRegistrationTests
{
    [Fact]
    public void Every_api_validator_is_registered_scoped_by_its_own_type()
    {
        ServiceCollection services = new();
        services.AddPoTrafficValidators();

        Type[] validators = [.. typeof(CreateRouteValidator).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.BaseType is { IsGenericType: true } b
                && b.GetGenericTypeDefinition() == typeof(AbstractValidator<>))];

        validators.Should().NotBeEmpty();
        foreach (Type validator in validators)
        {
            Type model = validator.BaseType!.GetGenericArguments()[0];
            services.Should().ContainSingle(d => d.ServiceType == typeof(IValidator<>).MakeGenericType(model)
                && d.ImplementationType == validator && d.Lifetime == ServiceLifetime.Scoped,
                $"{validator.Name} must be registered as AddScoped<IValidator<{model.Name}>, {validator.Name}>()");
        }

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IValidator<CreateRouteCommand>>().Should().BeOfType<CreateRouteValidator>();
    }
}
