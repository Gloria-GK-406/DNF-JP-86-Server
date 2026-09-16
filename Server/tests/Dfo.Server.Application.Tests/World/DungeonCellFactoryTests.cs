using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonCellFactoryTests
{
    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public async Task Cell_creation_freezes_monster_clear_and_loot_semantics(
        byte actorType,
        bool isMonster)
    {
        var loot = new RecordingLoot();
        var layout = new DungeonLayout(
            DungeonId: 10000,
            MazeIndex: 0,
            Width: 1,
            Height: 1,
            StartCell: new MazeCell(0, 0),
            BossCell: new MazeCell(0, 0),
            StartRoomMapId: 61000,
            Seed: 123,
            Rooms: new Dictionary<MazeCell, int> { [new MazeCell(0, 0)] = 61000 });
        DungeonRunParticipant[] participants =
        [
            new(new AccountId(1), new CharacterId(10), Slot: 0, IsLeader: true),
        ];
        var factory = new DungeonCellFactory(
            new OneActorRoom(actorType),
            new NoPlacement(),
            loot,
            new MonsterBoundary());

        var seed = await factory.CreateAsync(
            new DungeonCellCreationContext(
                DungeonRunId.New(),
                layout,
                layout.StartCell,
                layout.StartRoomMapId,
                Difficulty: 0,
                DateTimeOffset.UnixEpoch,
                participants),
            CancellationToken.None);

        var actor = Assert.Single(seed.Actors);
        Assert.Equal(isMonster, actor.IsMonster);
        Assert.Equal(isMonster ? 1 : 0, loot.CallCount);
        Assert.Equal(isMonster ? 1 : 0, actor.PendingLoot.Count);
        Assert.Equal(!isMonster, seed.Snapshot().IsCleared);
    }

    private sealed class OneActorRoom(byte actorType) : IDungeonRoomRuleService
    {
        public ValueTask<DungeonRoomContents> DescribeAsync(
            DungeonCellCreationContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DungeonRoomContents(
                [new RoomActorDescription(
                    new DungeonRoomActor(61670, 1, actorType),
                    IsAiCharacter: actorType >= 5)],
                [],
                FirstActorSequence: 100));

        public IReadOnlyList<RoomActorDescription> Describe(
            IReadOnlyList<DungeonRoomActor> declared) => throw new NotSupportedException();

        public ushort FirstActorSequenceFor(DungeonCellCreationContext context) => 100;
    }

    private sealed class RecordingLoot : IMonsterLootRuleService
    {
        public int CallCount { get; private set; }

        public ValueTask<IReadOnlyList<RolledDrop>> ResolveAsync(
            MonsterKill kill,
            CancellationToken cancellationToken)
        {
            this.CallCount++;
            return ValueTask.FromResult<IReadOnlyList<RolledDrop>>(
                [new RolledDrop(1001, 1, DropRarity.Common, DropItemKind.Stackable)]);
        }
    }

    private sealed class MonsterBoundary : IExperienceRuleService
    {
        public uint WorthOfKill(in KillCircumstances kill) => throw new NotSupportedException();

        public bool IsMonster(byte declaredActorType) =>
            MonsterExperienceRules.IsMonster(declaredActorType);

        public ClearExperienceWorth WorthOfClear(in ClearCircumstances clear) =>
            throw new NotSupportedException();

        public IReadOnlyList<ExperienceModifier> ModifiersFor(
            ExperienceEvent occasion,
            IReadOnlyList<AccountPremiumEntryState> premiums,
            DateTimeOffset now,
            int channelId,
            int dungeonId) => throw new NotSupportedException();

        public DungeonExperienceProgressionPolicy Progression() =>
            throw new NotSupportedException();
    }

    private sealed class NoPlacement : IItemPlacementRuleService
    {
        public ValueTask<ItemPlacement?> PlacementForAsync(
            int itemId,
            CancellationToken cancellationToken) => ValueTask.FromResult<ItemPlacement?>(null);

        public ValueTask<byte?> WornSlotForAsync(
            int itemId,
            CancellationToken cancellationToken) => ValueTask.FromResult<byte?>(null);

        public ValueTask<ItemWorldClass> ClassifyAsync(
            int itemId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(ItemWorldClass.Interchangeable);

        public ValueTask<bool> IsEquipmentAsync(
            int itemId,
            CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }
}
