using FluentAssertions;
using NSubstitute;
using PoTraffic.Api.Infrastructure.Storage;

namespace PoTraffic.Unit.Infrastructure;

/// <summary>
/// A query over an entity set must not see writes that land while it enumerates: each set is a
/// snapshot (GET /api/admin/users answered 500 "Collection was modified" during parallel sign-ins).
/// </summary>
public sealed class TableStorageContextConcurrencyTests
{
    [Fact]
    public async Task Enumerating_a_set_while_another_thread_adds_never_throws()
    {
        var db = new TableStorageContext(Substitute.For<ITableStore>());
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        Task writer = Task.Run(() =>
        {
            int i = 0;
            while (!stop.IsCancellationRequested)
                db.Add(new User { Id = UserId.New(), Email = $"u{i++}@test.invalid" });
        });
        Task reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
                _ = db.Users.Where(u => u.Email.Length > 0).GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.Count());
        });

        Func<Task> both = () => Task.WhenAll(writer, reader);
        await both.Should().NotThrowAsync();
        db.Users.Should().NotBeEmpty();
    }
}
