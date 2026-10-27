using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Characters;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class SaoTowerStoreTests
{
    [Fact]
    public async Task Two_database_sessions_competing_for_the_same_floor_have_exactly_one_winner()
    {
        await using var first = new LearningFixture();
        await using var second = new LearningFixture(existing: first.Path);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Race(LearningFixture f)
        {
            await ready.Task;
            return await TestGameplay.RunAsync(f.Services, async () =>
            {
                await using var tx = f.Scope.Begin();
                var won = await f.Services.GetRequiredService<ISaoTowerDomainStore>()
                    .TryClearAsync(new("race"), Guid.NewGuid(), 100, default);
                await tx.CommitAsync(default);
                return won;
            });
        }
        var a = Task.Run(() => Race(first));
        var b = Task.Run(() => Race(second));
        ready.SetResult();
        Assert.Single(await Task.WhenAll(a, b), x => x);
        Assert.Equal(1, first.Scalar("SELECT COUNT(*) FROM sao_first_clears"));
        Assert.Equal(2, (await first.Services.GetRequiredService<ISaoTowerDomainStore>().ReadAsync("race", default)).NextFloor);
    }

    [Fact]
    public async Task Shared_clear_is_atomic_idempotent_and_survives_reopen()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture();
            var store = f.Services.GetRequiredService<ISaoTowerDomainStore>();
            var initial = await store.ReadAsync("season", default);
            var winner = Guid.NewGuid();
            await using (var tx = f.Scope.Begin())
            {
                Assert.True(await store.TryClearAsync(initial, winner, 100, default));
                await store.RecordRewardAsync(winner, new(71), 10004331, default);
                // An uncommitted clear and its reward receipt must disappear together.
            }
            Assert.Equal(initial, await store.ReadAsync("season", default));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM sao_first_clear_rewards"));
            await using (var tx = f.Scope.Begin())
            {
                Assert.True(await store.TryClearAsync(initial, winner, 100, default));
                await store.RecordRewardAsync(winner, new(71), 10004331, default);
                await tx.CommitAsync(default);
            }
            await using (var tx = f.Scope.Begin())
            {
                Assert.False(await store.TryClearAsync(initial, winner, 100, default));
                Assert.False(await store.TryClearAsync(initial, Guid.NewGuid(), 100, default));
                await tx.CommitAsync(default);
            }
            await using var reopened = new LearningFixture(existing: f.Path);
            Assert.Equal(new SaoTowerProgress("season", 1, 2, 1), await reopened.Services.GetRequiredService<ISaoTowerDomainStore>().ReadAsync("season", default));
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM sao_first_clear_rewards"));
        });
    }

    [Fact]
    public async Task Hundredth_clear_wraps_and_old_round_cannot_advance_the_new_round()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture();
            var store = f.Services.GetRequiredService<ISaoTowerDomainStore>();
            SaoTowerProgress old = new("season");
            for (var i = 1; i <= 100; i++)
            {
                old = await store.ReadAsync("season", default);
                Assert.Equal(i, old.NextFloor);
                await using var tx = f.Scope.Begin();
                Assert.True(await store.TryClearAsync(old, Guid.NewGuid(), 100, default));
                await tx.CommitAsync(default);
            }
            Assert.Equal(new SaoTowerProgress("season", 2, 1, 100), await store.ReadAsync("season", default));
            await using (var tx = f.Scope.Begin())
            {
                Assert.False(await store.TryClearAsync(old, Guid.NewGuid(), 100, default));
                Assert.False(await store.TryClearAsync(new("season"), Guid.NewGuid(), 100, default));
                await tx.CommitAsync(default);
            }
            Assert.Equal(new SaoTowerProgress("other"), await store.ReadAsync("other", default));
        });
    }
}
