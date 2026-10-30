using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Dungeons;

namespace Dfo.Server.Application.Tests.World;

public sealed class LostLandDropTests
{
    [SkippableTheory]
    [InlineData(38030, 0, 0, 150)]
    [InlineData(38044, 4, 2, 151)]
    public async Task Real_forest_conflict_monsters_materialize_red_crystals_on_death(int mapId, byte x, byte y, int team)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var fixture = new LearningFixture(useArchive: true, configure: services =>
            services.AddSingleton(new Dfo.Server.Infrastructure.Content.PvfContentOptions { ArchivePath = ContentArchive.Path }));
        await fixture.SeedAsync();
        fixture.Services.GetRequiredService<IIndependentDropRuleService>().Initialize();
        fixture.Services.GetRequiredService<IExtraDropRuleService>().Initialize();
        fixture.Services.GetRequiredService<IMonsterDeclaredDropRuleService>().Initialize();
        fixture.Services.GetRequiredService<IWorldDropRuleService>().Initialize();
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            var layout = await fixture.Services.GetRequiredService<ILostLandRuleService>()
                .LayoutAsync(DungeonEntryFacts.None, 17, default);
            var cell = new MazeCell(x, y);
            layout = layout with { StartCell = cell, StartRoomMapId = mapId };
            DungeonRunParticipant[] participants = [new(new(1), new(71), 0, true)];
            var id = DungeonRunId.New();
            var seed = await fixture.Services.GetRequiredService<IDungeonCellFactory>().CreateAsync(
                new(id, layout, cell, mapId, 0, DateTimeOffset.UnixEpoch, participants), default);
            Assert.NotEmpty(seed.Actors);
            Assert.All(seed.Actors, actor => Assert.Equal(team, actor.Actor.Declaration?.MonsterTeam));
            IDungeonRunDomainService runs = global::Dfo.Server.Testing.TestParties.Runs();
            runs.PreparePartyEntry(participants, layout, 0, seed, id, DateTimeOffset.UnixEpoch);
            var crystals = 0;
            foreach (var actor in seed.Actors)
            {
                var death = runs.TryClaimActorDeath(new(71), actor.Sequence)!;
                var change = runs.CommitActorDeathChange(death)!;
                crystals += change.AddedGroundItems.Where(item => item.ItemId == 10092845).Sum(item => item.Count);
                Assert.Null(runs.CommitActorDeathChange(death));
            }
            Assert.True(crystals > 0, $"Forest map {mapId}, monster team {team}: no red crystals after clearing the room.");
        });
    }

    [Fact]
    public async Task Alternative_bosses_and_tower_variants_share_one_bonus_across_precreated_rooms()
    {
        var rules = new LostLandDropRuleService(new LostLandDropRuleTests.Content());
        var context = LostLandDropRuleTests.Context(17, 1);
        var first = await rules.PrepareAsync(context, new(new(69584, 85, 3), false), 1, default);
        var second = await rules.PrepareAsync(context, new(new(69591, 85, 3), false), 2, default);
        var towerContext = LostLandDropRuleTests.Context(17, 5);
        var tower = await rules.PrepareAsync(towerContext, new(new(69668, 85, 3), false), 3, default);
        var alternateTower = await rules.PrepareAsync(towerContext, new(new(69669, 85, 3), false), 4, default);
        DungeonCellActorState Actor(ushort id, int code, LostLandDropPlan plan) =>
            new(id, new(code, 85, 3), false, false, plan.Regular) { LostLandBonus = plan.Bonus };
        TestGameplay.Run(() =>
        {
            IDungeonRunDomainService runs = global::Dfo.Server.Testing.TestParties.Runs();
            var layout = context.Layout with { Width = 2,
                Cells = new Dictionary<MazeCell, DungeonCellDefinition>
                { [new(0,0)] = DungeonCellDefinition.Fixed(1001), [new(1,0)] = DungeonCellDefinition.Fixed(1005) } };
            var seed = new DungeonCellSeed(new(0,0), 1001, 1,
                [Actor(1,69584,first), Actor(2,69591,second)], []);
            var id = DungeonRunId.New();
            runs.PreparePartyEntry(context.Participants, layout, 0, seed, id, context.StartedAt);
            DungeonActorDeathChange Death(ushort actor) => runs.CommitActorDeathChange(runs.TryClaimActorDeath(new(71), actor)!)!;
            var paid = Death(1);
            Assert.Contains(paid.AddedGroundItems, item => item.ItemId == 10092849);
            var alternative = Death(2);
            Assert.Equal(10092845, Assert.Single(alternative.AddedGroundItems).ItemId);
            var next = new DungeonCellSeed(new(1,0),1005,3,[Actor(3,69668,tower),Actor(4,69669,alternateTower)],[]);
            var current = runs.Find(new Dfo.Server.Data.Characters.CharacterId(71))!;
            Assert.NotNull(runs.TryMove(new(71), id, current.RoomVersion, new(1,0), next));
            Assert.Equal(3, Assert.Single(Death(3).AddedGroundItems, item => item.ItemId == 10092856).Count);
            Assert.Empty(Death(4).AddedGroundItems);
            current = runs.Find(new Dfo.Server.Data.Characters.CharacterId(71))!;
            Assert.NotNull(runs.TryMove(new(71),id,current.RoomVersion,new(0,0),seed));
            Assert.Null(runs.TryClaimActorDeath(new(71),1));
            if (runs.Find(new Dfo.Server.Data.Characters.CharacterId(71)) is { } run71) runs.End(new(71), run71.RunId);
            runs.PreparePartyEntry(context.Participants,layout,0,seed,DungeonRunId.New(),context.StartedAt);
            Assert.Contains(Death(1).AddedGroundItems,item => item.ItemId==10092849);
        });
    }

    [SkippableFact]
    public async Task Tower_crystal_and_bag_stacks_follow_the_real_pickup_route_and_survive_reselection()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var fixture = new ShopFixture(services =>
        {
            services.AddSingleton(new Dfo.Server.Infrastructure.Content.PvfContentOptions { ArchivePath = ContentArchive.Path });
            services.AddSingleton<IItemPlacementDefinitionSource, Dfo.Server.Infrastructure.Content.PvfItemPlacementDefinitionSource>();
        }, useArchive:true);
        var context = LostLandDropRuleTests.Context(17,5);
        context = context with { MapId=11248, Layout=context.Layout with {StartRoomMapId=11248},
            Participants=[new(fixture.Session.AccountId,fixture.Session.CharacterId,0,true)] };
        var plan = await fixture.Services.GetRequiredService<ILostLandDropRuleService>()
            .PrepareAsync(context,new(new(69668,85,3),false),1,default);
        var runs=fixture.Services.DungeonRuns();
        var run=fixture.InTransaction(() => runs.PreparePartyEntry(context.Participants,context.Layout,0,
            new(context.Cell,11248,1,[new(1,new(69668,85,3),false,false,plan.Regular) {LostLandBonus=plan.Bonus}],[]),
            context.RunId,fixture.Clock.GetUtcNow()).Run);
        var change=TestGameplay.Run(fixture.Services,()=>
        {
            fixture.Services.GetRequiredService<IDungeonRunProjectionDomainService>().Register(run.RunId,[fixture.Session]);
            runs.TryMarkParticipantReady(fixture.Session.CharacterId,run.RunId,run.RoomVersion);
            return runs.CommitActorDeathChange(runs.TryClaimActorDeath(fixture.Session.CharacterId,1)!)!;
        });
        var crystal=Assert.Single(change.AddedGroundItems,item=>item.ItemId==10092849);
        var bag=Assert.Single(change.AddedGroundItems,item=>item.ItemId==10092856);
        ushort sequence = 17;
        foreach(var item in new[]{crystal,bag})
        {
            var body=new byte[2];BinaryPrimitives.WriteUInt16LittleEndian(body,item.Number);
            await EffectRequestTransport.SendRaw(fixture,new GameMessageMetadata(1,0x002B,0,sequence++),body);
            Assert.Equal(item.Count,fixture.Count(item.ItemId));
            await EffectRequestTransport.SendRaw(fixture,new GameMessageMetadata(1,0x002B,0,sequence++),body);
            Assert.Equal(item.Count,fixture.Count(item.ItemId));
        }
        TestGameplay.Run(fixture.Services,() =>
        {
            if (runs.Find(fixture.Session.CharacterId) is { } current) runs.End(fixture.Session.CharacterId, current.RunId);
            fixture.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(fixture.Session.CharacterId);
        });
        fixture.Select(1);
        Assert.Equal(crystal.Count,fixture.Count(10092849));
        Assert.Equal(3,fixture.Count(10092856));
    }

    [SkippableFact]
    public async Task Real_tower_cell_freezes_and_materializes_the_guaranteed_reward_once()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var fixture = new LearningFixture(useArchive: true, configure: services =>
            services.AddSingleton(new Dfo.Server.Infrastructure.Content.PvfContentOptions { ArchivePath = ContentArchive.Path }));
        await fixture.SeedAsync();
        fixture.Services.GetRequiredService<IIndependentDropRuleService>().Initialize();
        fixture.Services.GetRequiredService<IExtraDropRuleService>().Initialize();
        fixture.Services.GetRequiredService<IMonsterDeclaredDropRuleService>().Initialize();
        fixture.Services.GetRequiredService<IWorldDropRuleService>().Initialize();
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            var layout = await fixture.Services.GetRequiredService<ILostLandRuleService>()
                .LayoutAsync(DungeonEntryFacts.None, 17, default);
            layout = layout with { StartCell = new(7, 0), StartRoomMapId = 11248 };
            DungeonRunParticipant[] participants = [new(new(1), new(71), 0, true)];
            var id = DungeonRunId.New();
            var seed = await fixture.Services.GetRequiredService<IDungeonCellFactory>().CreateAsync(
                new(id, layout, layout.StartCell, 11248, 0, DateTimeOffset.UtcNow, participants), default);
            var boss = Assert.Single(seed.Actors, actor => actor.Actor.Code == 69668);
            IDungeonRunDomainService runs = global::Dfo.Server.Testing.TestParties.Runs();
            runs.PreparePartyEntry(participants, layout, 0, seed, id, DateTimeOffset.UtcNow);
            var death = runs.TryClaimActorDeath(new(71), boss.Sequence)!;
            var change = runs.CommitActorDeathChange(death)!;
            Assert.InRange(Assert.Single(change.AddedGroundItems, item => item.ItemId == 10092849).Count, 40, 60);
            Assert.Equal(3, Assert.Single(change.AddedGroundItems, item => item.ItemId == 10092856).Count);
            Assert.Null(runs.CommitActorDeathChange(death));
            Assert.Null(runs.TryClaimActorDeath(new(71), boss.Sequence));
        });
    }
}
