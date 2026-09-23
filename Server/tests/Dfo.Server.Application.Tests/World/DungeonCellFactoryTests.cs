using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonCellFactoryTests
{
    [Theory]
    [InlineData(3, true, 1)]
    [InlineData(3, true, 2)]
    [InlineData(4, false, 2)]
    public async Task Cell_creation_freezes_monster_clear_and_loot_semantics(
        byte actorType,
        bool isMonster, int memberCount)
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
        var participants = Enumerable.Range(0, memberCount)
            .Select(index => new DungeonRunParticipant(new AccountId(index + 1),
                new CharacterId(index + 10), Slot: (byte)index, IsLeader: index == 0)).ToArray();
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
        Assert.Equal(isMonster ? 1 + memberCount : 0, actor.PendingLoot.Count);
        Assert.Equal(isMonster ? memberCount : 0, loot.PersonalCalls.Count);
        if (isMonster)
        {
            Assert.Equal(participants.Select(p => p.CharacterId), loot.PersonalCalls);
            Assert.Single(actor.PendingLoot, drop => drop.Delivery == RolledDropDelivery.Ground);
            Assert.Equal(participants.Select(p => (CharacterId?)p.CharacterId), actor.PendingLoot
                .Where(drop => drop.Delivery == RolledDropDelivery.DirectQuestItem)
                .Select(drop => drop.OwnerCharacterId));
        }
        Assert.Equal(!isMonster, seed.Snapshot().IsCleared);
        if (isMonster)
        {
            Assert.Equal(61670, loot.MonsterId);
            var context = Assert.IsType<MonsterDropContext>(loot.Context);
            Assert.Equal(layout.DungeonId, context.DungeonId);
            Assert.Equal(layout.Seed, context.Seed);
            Assert.Equal((ushort)100, context.ActorSequence);
            Assert.Equal(actorType, context.MonsterRank);
            Assert.Equal(3, context.RandomDropCount);
            Assert.Equal(new CharacterId(10), context.CharacterId);
            Assert.Equal(participants.Select(p => new MonsterDropParticipant(p.AccountId, p.CharacterId)),
                context.Participants);
        }
    }

    private sealed class OneActorRoom(byte actorType) : IDungeonRoomRuleService
    {
        public ValueTask<DungeonRoomContents> DescribeAsync(
            DungeonCellCreationContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DungeonRoomContents(
                [new RoomActorDescription(
                    new DungeonRoomActor(61670, 1, actorType, RandomDropCount: 3),
                    IsAiCharacter: actorType >= 5)],
                [],
                FirstActorSequence: 100));

        public IReadOnlyList<RoomActorDescription> Describe(
            IReadOnlyList<DungeonRoomActor> declared) => throw new NotSupportedException();

        public ushort FirstActorSequenceFor(DungeonCellCreationContext context) => 100;
    }

    private sealed class RecordingLoot : IMonsterDropDomainService
    {
        public int CallCount { get; private set; }
        public List<CharacterId> PersonalCalls { get; } = [];
        public ValueTask<IReadOnlyList<RolledDrop>> GeneratePersonalAsync(
            int monsterId, CharacterId characterId, MonsterDropContext context, CancellationToken ct)
        {
            this.PersonalCalls.Add(characterId);
            return ValueTask.FromResult<IReadOnlyList<RolledDrop>>(
                [new RolledDrop(9001, 1, DropRarity.Common, DropItemKind.Stackable,
                    OwnerCharacterId: characterId, Delivery: RolledDropDelivery.DirectQuestItem)]);
        }
        public int MonsterId { get; private set; }
        public MonsterDropContext? Context { get; private set; }

        public ValueTask<IReadOnlyList<RolledDrop>> GenerateGroundAsync(
            int monsterId, MonsterDropContext context,
            CancellationToken cancellationToken)
        {
            this.CallCount++;
            this.MonsterId = monsterId;
            this.Context = context;
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

        public ValueTask<BusinessSlot?> WornSlotForAsync(
            int itemId,
            CancellationToken cancellationToken) => ValueTask.FromResult<BusinessSlot?>(null);

        public ValueTask<ItemWorldClass> ClassifyAsync(
            int itemId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(ItemWorldClass.Interchangeable);

        public ValueTask<bool> IsEquipmentAsync(
            int itemId,
            CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }
}
