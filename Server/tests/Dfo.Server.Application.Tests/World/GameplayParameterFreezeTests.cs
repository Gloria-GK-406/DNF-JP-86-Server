using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game.DropMaterialization;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class DungeonCellFactoryTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task One_cell_uses_the_same_parameters_for_objects_and_every_actor_and_recipient()
    {
        var query = new ChangingParameters();
        var cell = new MazeCell(0, 0);
        var layout = new DungeonLayout(77, 0, 1, 1, cell, cell, 123, 456,
            new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(123) });
        var factory = new DungeonCellFactory(new SnapshotRoom(), new NoPlacement(), new SnapshotLoot(),
            new MonsterBoundary(), new RecordingQuestLoot(), new NoQuestBonus(), new NoHellRewards(),
            new NoLostLandDrops(), query, new DropMaterializationRuleService(DropMaterializationProtocolCapabilities.Limits),
            NullLogger<DungeonCellFactory>.Instance);
        var seed = await factory.CreateAsync(new(DungeonRunId.New(), layout, cell, 123, 0,
            DateTimeOffset.UnixEpoch, [new(new(1), new(10), 0, true), new(new(2), new(20), 1, false)]), default);
        Assert.Equal(3, Assert.Single(seed.GroundItems).Count);
        Assert.Equal(2, seed.Actors.Count);
        Assert.All(seed.Actors, actor =>
        {
            Assert.Equal(3, actor.PendingLoot.Count);
            Assert.All(actor.PendingLoot, drop => Assert.Equal(3, drop.Count));
        });
        Assert.Equal(1, (await query.CurrentAsync(default)).For(DropDrawEntry.World));
        Assert.All(seed.Actors, actor => Assert.All(actor.PendingLoot, drop => Assert.Equal(3, drop.Count)));
    }

    private sealed class ChangingParameters : IDropDrawMultiplierDomainService
    {
        private bool first = true;
        public ValueTask<DropDrawMultipliers> CurrentAsync(CancellationToken ct)
        {
            var value = first ? 3 : 1;
            first = false;
            return ValueTask.FromResult(new DropDrawMultipliers(new Dictionary<DropDrawEntry, int> { [DropDrawEntry.World] = value }));
        }
    }

    private sealed class SnapshotRoom : IDungeonRoomRuleService
    {
        public ValueTask<DungeonRoomContents> DescribeAsync(DungeonCellCreationContext context, CancellationToken ct) =>
            ValueTask.FromResult(new DungeonRoomContents([new(new(61670, 1, 0), false), new(new(61671, 1, 0), false)],
                [new(1, new(0, 1000, 0, context.DrawMultipliers.For(DropDrawEntry.World)))], 100));
        public IReadOnlyList<RoomActorDescription> Describe(IReadOnlyList<DungeonRoomActor> actors) => throw new NotSupportedException();
        public ushort FirstActorSequenceFor(DungeonCellCreationContext context) => 100;
    }

    private sealed class SnapshotLoot : IMonsterDropDomainService
    {
        public ValueTask<IReadOnlyList<RolledDrop>> GenerateGroundAsync(int id, MonsterDropContext context, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<RolledDrop>>([new(2000, context.DrawMultipliers.For(DropDrawEntry.World), DropRarity.Common, DropItemKind.Stackable)]);
        public ValueTask<IReadOnlyList<RolledDrop>> GeneratePersonalAsync(int id, CharacterId character, MonsterDropContext context, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<RolledDrop>>([new(2001, context.DrawMultipliers.For(DropDrawEntry.World), DropRarity.Common, DropItemKind.Stackable, OwnerCharacterId: character)]);
        public ValueTask<IReadOnlyList<RolledDrop>> GenerateIndependentAsync(int id, MonsterDropContext context, int bonus, CancellationToken ct) => throw new NotSupportedException();
    }
}

[Trait("Category", "Unit")]
public sealed class FatigueBatchPolicyTests
{
    [Fact]
    public async Task A_batch_charges_every_member_using_one_policy_decision()
    {
        var query = new ChangingConsumption();
        await using var fixture = new LearningFixture(configure: services => services.AddSingleton<IFatigueConsumptionQuery>(query));
        await TestGameplay.RunAsync(async () =>
        {
            await fixture.SeedAsync();
            var fatigue = fixture.Services.GetRequiredService<ICharacterFatigueDomainService>();
            await using (var tx = fixture.Scope.Begin())
            {
                await fatigue.SpendBatchAsync([new(71), new(72)], 1, default);
                await tx.CommitAsync(default);
            }
            Assert.Equal(1, (await fatigue.BalanceOfAsync(new(71), default)).Used);
            Assert.Equal(1, (await fatigue.BalanceOfAsync(new(72), default)).Used);
            await using (var tx = fixture.Scope.Begin())
            {
                await fatigue.SpendAsync(new(71), 1, default);
                await tx.CommitAsync(default);
            }
            Assert.Equal(1, (await fatigue.BalanceOfAsync(new(71), default)).Used);
        });
    }

    private sealed class ChangingConsumption : IFatigueConsumptionQuery
    {
        private bool first = true;
        public ValueTask<bool> ShouldConsumeAsync(CancellationToken ct)
        {
            var result = first;
            first = false;
            return ValueTask.FromResult(result);
        }
    }
}
