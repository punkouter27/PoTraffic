using FluentAssertions;
using FluentValidation;
using PoTraffic.API.Features.Admin;
using PoTraffic.API.Features.Config;
using PoTraffic.API.Infrastructure.Storage;
using PoTraffic.UnitTests.Helpers;

namespace PoTraffic.UnitTests.Features.Config;

/// <summary>
/// Cost rates, limits and feature flags live in SystemConfiguration rows, so an administrator
/// changes them at runtime through <see cref="UpdateSystemConfigurationHandler"/> and every
/// reader sees the new value on its next use.
/// </summary>
public sealed class SystemSettingsTests
{
    private static TableStorageContext SeededDb()
    {
        TableStorageContext db = TestDoubles.CreateDb();
        db.SeedDefaultConfigurationsIfMissing();
        return db;
    }

    [Fact]
    public async Task AdminEdits_TakeEffectOnTheNextRead_WithoutARestart()
    {
        TableStorageContext db = SeededDb();
        var flags = new FeatureFlags(db, mockProvidersForcedByHost: false);
        var update = new UpdateSystemConfigurationHandler(db);

        flags.UseMockProviders.Should().BeFalse("live providers are the seeded default");
        flags.EnableWeather.Should().BeTrue("weather capture is on by default");
        PollCostRates.PerPoll(db).Should().Be(0.005m);

        (await update.Handle(new(SystemSettings.UseMockProvidersKey, "true"), CancellationToken.None)).Should().BeTrue();
        (await update.Handle(new(SystemSettings.EnableWeatherKey, " false "), CancellationToken.None)).Should().BeTrue();
        (await update.Handle(new(SystemSettings.PerPollKey, "0.01"), CancellationToken.None)).Should().BeTrue();

        flags.UseMockProviders.Should().BeTrue();
        flags.EnableWeather.Should().BeFalse("the stored value is trimmed before it is saved");
        PollCostRates.PerPoll(db).Should().Be(0.01m);
        SpendingCap.RateFor(db, ProviderCallKind.CheckNow).Should().Be(0.01m, "Check now is priced as a poll");

        // A host that forces mocks (Testing) ignores the stored value entirely.
        await update.Handle(new(SystemSettings.UseMockProvidersKey, "false"), CancellationToken.None);
        new FeatureFlags(db, mockProvidersForcedByHost: true).UseMockProviders.Should().BeTrue();
        flags.UseMockProviders.Should().BeFalse();
    }

    [Fact]
    public async Task Seeding_CreatesEveryCataloguedRowOnce_AndUpdatesAcceptOnlyValidValuesForThem()
    {
        TableStorageContext db = SeededDb();
        var update = new UpdateSystemConfigurationHandler(db);
        await update.Handle(new(SystemSettings.DailyQuotaKey, "25"), CancellationToken.None);

        db.SeedDefaultConfigurationsIfMissing();

        db.SystemConfigurations.Select(c => c.Key).Should().BeEquivalentTo(SystemSettings.All.Select(s => s.Key),
            "every catalogued setting is seeded exactly once");
        db.SystemConfigurations.Single(c => c.Key == SystemSettings.DailyQuotaKey).Value
            .Should().Be("25", "re-seeding never overwrites an administrator's change");

        (string Key, string Value)[] rejected =
        [
            ("not.a.setting", "1"),
            (SystemSettings.EnableWeatherKey, "maybe"),
            (SystemSettings.PerGeocodeKey, "-0.01"),
            (SystemSettings.DailyQuotaKey, "0"),
        ];
        foreach ((string key, string value) in rejected)
        {
            Func<Task> act = () => update.Handle(new(key, value), CancellationToken.None);
            await act.Should().ThrowAsync<ValidationException>($"{key}={value} must be refused");
        }
    }
}
