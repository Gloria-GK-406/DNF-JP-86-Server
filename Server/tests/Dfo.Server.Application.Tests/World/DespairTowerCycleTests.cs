using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Application.Tests.Characters;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class DespairTowerCycleTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 100)]
    [InlineData(100, 1)]
    public void Next_floor_wraps_at_the_catalog_end(int cleared, int next)
    {
        var rules = new DespairTowerRuleService(new Floors(), null!, null!);
        Assert.Equal(next, Assert.IsType<DespairTowerFloor>(rules.NextFloor(new(cleared))).Floor);
    }

    [Fact]
    public async Task Completed_cycle_advances_to_one_once_and_survives_reopen()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture();
            await f.SeedAsync();
            f.Sql("INSERT INTO character_despair_tower(character_id,highest_cleared_floor,last_clear_run) VALUES(71,100,'legacy-cycle')");
            var store = f.Services.GetRequiredService<IDespairTowerDomainStore>();
            var run = Guid.NewGuid();
            await using (var tx = f.Scope.Begin())
            {
                Assert.True(await store.TryAdvanceAsync(new(71), 100, run, default));
                await tx.CommitAsync(default);
            }
            await using (var tx = f.Scope.Begin())
            {
                Assert.False(await store.TryAdvanceAsync(new(71), 100, run, default));
                await tx.CommitAsync(default);
            }
            await using var reopened = new LearningFixture(existing: f.Path);
            Assert.Equal(1, (await reopened.Services.GetRequiredService<IDespairTowerDomainStore>().ReadAsync(new(71), default)).HighestClearedFloor);
        });
    }

    private sealed class Floors : IDespairTowerContentSource
    {
        public DespairTowerCatalog Load() => new(70, 3,
            Enumerable.Range(1, 100).Select(x => new DespairTowerFloor(x, 11007 + x, x, [])).ToArray());
    }
}
