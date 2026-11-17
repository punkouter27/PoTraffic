using Azure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoTraffic.Api.Infrastructure;
using PoTraffic.Api.Infrastructure.Storage;

namespace PoTraffic.Unit.Infrastructure;

/// <summary>
/// The storage check's description is served anonymously on /health/json, so an RBAC denial
/// must not name the account there; the fix-it text is kept for administrators only.
/// </summary>
public sealed class StorageHealthCheckTests
{
    [Fact]
    public async Task Authorization_denial_keeps_the_account_name_out_of_the_public_description()
    {
        ITableStore store = Substitute.For<ITableStore>();
        store.PingAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new RequestFailedException(403, "denied", "AuthorizationPermissionMismatch", null)));
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AzureTable:AccountName"] = "sentinelacct42" })
            .Build();
        var sut = new StorageHealthCheck(new TableStorageContext(store), config, NullLogger<StorageHealthCheck>.Instance);

        HealthCheckResult result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().NotContain("sentinelacct42").And.NotContain("/subscriptions/");
        result.Data[StorageHealthCheck.AdminDetailKey].Should().BeOfType<string>()
            .Which.Should().Contain("sentinelacct42", "administrators still get the actionable detail");
    }
}
