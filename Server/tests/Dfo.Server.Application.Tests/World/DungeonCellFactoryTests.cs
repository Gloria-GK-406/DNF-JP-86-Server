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
    [InlineData(5, false, 2)]
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
            new MonsterBoundary(), new RecordingQuestLoot());

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
        Assert.Equal(isMonster || actorType >= 5 ? 1 : 0, loot.CallCount);
        Assert.Equal(isMonster || actorType >= 5 ? 1 + memberCount : 0, actor.PendingLoot.Count);
        Assert.Equal(isMonster || actorType >= 5 ? memberCount : 0, loot.PersonalCalls.Count);
        if (isMonster || actorType >= 5)
        {
            Assert.Equal(participants.Select(p => p.CharacterId), loot.PersonalCalls);
            Assert.Single(actor.PendingLoot, drop => drop.Delivery == RolledDropDelivery.Ground);
            Assert.Equal(participants.Select(p => (CharacterId?)p.CharacterId), actor.PendingLoot
                .Where(drop => drop.Delivery == RolledDropDelivery.DirectQuestItem)
                .Select(drop => drop.OwnerCharacterId));
        }
        if (actorType >= 5)
            Assert.Equal(IndependentDropSourceType.APC, loot.Context!.SourceType);
        Assert.Equal(!isMonster, seed.Snapshot().IsCleared);
        if (isMonster || actorType >= 5)
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

    [Fact]
    public async Task Duplicate_object_templates_bind_separate_personal_loot_without_monster_or_ground_effects()
    {
        var questLoot = new RecordingQuestLoot();
        var cell = new MazeCell(0, 0);
        var layout = new DungeonLayout(84, 0, 1, 1, cell, cell, 123, 456,
            new Dictionary<MazeCell, int> { [cell] = 123 });
        DungeonRunParticipant[] members = [new(new(1), new(10), 0, true), new(new(2), new(20), 1, false)];
        var factory = new DungeonCellFactory(new ObjectRoom(), new NoPlacement(), new RecordingLoot(),
            new MonsterBoundary(), questLoot);
        var seed = await factory.CreateAsync(new(DungeonRunId.New(), layout, cell, 123, 0,
            DateTimeOffset.UnixEpoch, members), CancellationToken.None);
        Assert.Empty(seed.Actors);
        Assert.Empty(seed.GroundItems);
        Assert.True(seed.Snapshot().IsCleared);
        Assert.Equal(3, seed.Objects.Count);
        Assert.Equal(3, seed.Objects.Select(o => o.Object.Id).Distinct().Count());
        Assert.Equal(3, questLoot.Requests.Select(r => r.InstanceId).Distinct().Count());
        Assert.All(questLoot.Requests, r => Assert.Equal(QuestEnemyType.PassiveObject, r.EnemyType));
        Assert.All(seed.Objects, obj => Assert.Equal(members.Select(m => (CharacterId?)m.CharacterId),
            obj.PendingLoot.Select(drop => drop.OwnerCharacterId)));
        var run = new DungeonRunDomainService().PreparePartyEntry(members, layout, 0, initialCell: seed, DungeonRunId.New(), DateTimeOffset.UnixEpoch).Run;
        Assert.Equal(3, run.CurrentCellState!.Objects.Count);
        // A caller cannot mutate the run through the unpublished seed's pending array.
        ((RolledDrop[])seed.Objects[0].PendingLoot)[0] = default;
        Assert.Equal(10149153, run.CurrentCellState.Objects[0].PendingLoot[0].ItemId);
    }

    private sealed class ObjectRoom : IDungeonRoomRuleService
    {
        public ValueTask<DungeonRoomContents> DescribeAsync(DungeonCellCreationContext context, CancellationToken ct) =>
            ValueTask.FromResult(new DungeonRoomContents([], [], 1)
            {
                Objects = [new(new(DungeonObjectSection.Passive, 0), 8961, 1, 2, 0),
                    new(new(DungeonObjectSection.Passive, 1), 8961, 3, 4, 0),
                    new(new(DungeonObjectSection.SpecialPassive, 0), 8961, 5, 6, 0)],
            });
        public IReadOnlyList<RoomActorDescription> Describe(IReadOnlyList<DungeonRoomActor> actors) => [];
        public ushort FirstActorSequenceFor(DungeonCellCreationContext context) => 1;
    }

    private sealed class RecordingQuestLoot : IQuestEnemyDropDomainService
    {
        public List<QuestEnemyDropRequest> Requests { get; } = [];
        public ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(QuestEnemyDropRequest request,
            MonsterDropParticipant participant, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return ValueTask.FromResult<IReadOnlyList<RolledDrop>>([new(10149153, 1,
                DropRarity.Common, DropItemKind.Stackable, OwnerCharacterId: participant.CharacterId,
                Delivery: RolledDropDelivery.DirectQuestItem, QuestId: 2201, GrantLimit: 10)]);
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
