using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.World;
using Dfo.Server.Application.World.BossTower;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class BossTowerFoundationTests
{
    [Fact]
    public async Task Entry_projection_is_permanent_by_default_and_explicit_disable_closes_it()
    {
        await using var f = new LearningFixture();
        await f.SeedAsync();
        var projection = f.Services.GetRequiredService<IEventPlayerProjectionDomainService>();
        Assert.True(Assert.Single((await projection.ReadNowAsync(f.Session, default)).ScheduledDungeons,
            x => x.DungeonId == 3505).Open);
        await using (var tx = f.Scope.Begin())
        {
            await f.Services.GetRequiredService<IEventManagementDomainService>().ChangeAsync(
                new(2329, 0, "disable"), "test", DateTimeOffset.UtcNow, default);
            await tx.CommitAsync(default);
        }
        Assert.False(Assert.Single((await projection.ReadNowAsync(f.Session, default)).ScheduledDungeons,
            x => x.DungeonId == 3505).Open);
    }

    [SkippableFact]
    public async Task Real_archive_admission_survives_without_maze_and_all_weighted_rooms_are_usable()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var services = ContentArchive.CreateContentProvider();
        var provider = services.GetRequiredService<PvfArchiveProvider>();
        var ordinary = new PvfDungeonDefinitionSource(provider);
        Assert.Null(await ordinary.GetAsync(3505, default));
        var header = await ordinary.GetAdmissionAsync(3505, default);
        Assert.NotNull(header);
        Assert.Equal(60, header.MinimumRequiredLevel);
        Assert.True(header.ProhibitsPractice);
        Assert.Equal(2749110, Assert.Single(header.RequiredItems).ItemId);
        var difficulty = new DungeonDifficultyRuleService(admissions: ordinary);
        Assert.True(await difficulty.AllowsAsync(3505, DungeonDifficulty.Normal, DungeonDifficulty.Normal, default));
        Assert.False(await difficulty.AllowsAsync(3505, DungeonDifficulty.Adventure, DungeonDifficulty.Tier4, default));
        var source = new PvfBossTowerContentSource(provider, ordinary);
        var rules = new BossTowerRuleService(source);
        var d = await rules.DefinitionAsync(default);
        Assert.Equal(29, d.Stages.Sum(x => x.Maps.Count));
        Assert.Equal(new double[] {1, 1, 2, 2, 3, 3, 4, 4}, d.Rewards.ExperienceWeights);
        Assert.Equal(new[] {1, 2, 3, 4, 6, 8, 9, 10}, d.Rewards.CardCounts);
        Assert.Equal(11, d.Rewards.GoldWeight);
        Assert.All(d.Stages, stage =>
        {
            Assert.Equal(10000, stage.Maps.Sum(x => x.Weight));
            Assert.All(stage.Maps, map =>
            {
                Assert.Contains(1010, map.NpcIds);
                Assert.NotEmpty(map.Room.Actors);
                Assert.All(map.Room.Actors, actor => Assert.InRange(actor.LevelValue, 68, 72));
            });
        });
        for (var stage = 1; stage <= 8; stage++)
        {
            var room = await rules.CreateRoomAsync(stage, 12345, default);
            var repeat = await rules.CreateRoomAsync(stage, 12345, default);
            Assert.Equal(room.Combat.MapId, repeat.Combat.MapId);
            Assert.Equal(stage, room.Combat.Level);
            Assert.Contains(d.Stages[stage - 1].Maps, m => m.Room.MapId == room.Combat.MapId);
        }
    }
}
