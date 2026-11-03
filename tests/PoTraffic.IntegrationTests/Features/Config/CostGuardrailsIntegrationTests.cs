using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoTraffic.API.Infrastructure.Providers;
using PoTraffic.API.Infrastructure.Storage;
using PoTraffic.API.Infrastructure.Testing;

namespace PoTraffic.IntegrationTests.Features.Config;

public sealed class CostGuardrailsIntegrationTests : BaseIntegrationTest
{
    [SkipUnlessAzuriteAvailable]
    public void TestingConfiguration_NeverResolvesTheBilledProvider()
    {
        IServiceProvider services = GetServices();

        services.GetRequiredService<IConfiguration>().GetValue<bool>("Features:UseMockProviders").Should().BeTrue();

        using IServiceScope scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITrafficProvider>()
            .Should().NotBeOfType<GoogleMapsTrafficProvider>();
    }

    /// <summary>
    /// Outside Testing the mock switch is the admin-editable <c>feature.mockproviders.enabled</c>
    /// row: flipping it changes which provider the next scope resolves, with no restart. Built from
    /// the real <see cref="ProviderExtensions.AddTrafficProviders"/> registration over a
    /// memory-only working set, so nothing is billed and nothing is written.
    /// </summary>
    [SkipUnlessAzuriteAvailable]
    public void LiveHost_SwitchesBetweenLiveAndMockProviders_WhenTheSettingChanges()
    {
        IConfiguration config = new ConfigurationBuilder().Build();
        IWebHostEnvironment env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns("Development");
        var db = new TableStorageContext();
        db.SeedDefaultConfigurationsIfMissing();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(config);
        services.AddSingleton(db);
        services.AddTrafficProviders(config, env);
        using ServiceProvider provider = services.BuildServiceProvider();

        ITrafficProvider Resolve()
        {
            using IServiceScope scope = provider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ITrafficProvider>();
        }

        Resolve().Should().BeOfType<GoogleMapsTrafficProvider>("live providers are the seeded default");

        db.SystemConfigurations.Single(c => c.Key == SystemSettings.UseMockProvidersKey).Value = "true";
        Resolve().Should().BeOfType<MockTrafficProvider>();
        provider.GetRequiredService<FeatureFlags>().MockProvidersForcedByHost.Should().BeFalse();
    }
}
