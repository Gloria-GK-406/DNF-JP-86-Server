using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonCellFactoryTests
{
    private sealed class NoLostLandDrops : ILostLandDropRuleService
    {
        public ValueTask<LostLandDropPlan> PrepareAsync(DungeonCellCreationContext context,
            RoomActorDescription actor, ushort sequence, CancellationToken cancellationToken) =>
            ValueTask.FromResult(LostLandDropPlan.Empty);
    }

    [Fact]
    public async Task Repeated_hell_group_across_orders_freezes_one_shared_reward_and_preserves_actor_loot()
    {
        var cell = new MazeCell(0, 0);
        var layout = new DungeonLayout(77, 0, 1, 1, cell, cell, 60064, 123,
            new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(60064) })
        { Mode = new DungeonRunMode.Hell(new(1, cell, 60064, [])) };
        var factory = new DungeonCellFactory(new HellRoom(), new NoPlacement(), new RecordingLoot(),
            new MonsterBoundary(), new RecordingQuestLoot(), new NoQuestBonus(), new FrozenHellRewards(), new NoLostLandDrops());
        var seed = await factory.CreateAsync(new(DungeonRunId.New(), layout, cell, 60064, 0,
            DateTimeOffset.UnixEpoch, [new(new(1), new(10), 0, true)]), default);
        Assert.Empty(seed.GroundItems);
        Assert.Equal(4, seed.Actors.Count);
        Assert.All(seed.Actors, actor =>
        {
            Assert.Equal(3330, Assert.Single(actor.PendingLoot).ItemId);
            Assert.False(actor.IsMonster);
            Assert.True(actor.EligibleForHellGroupReward);
        });
        var group = Assert.Single(seed.HellGroups);
        Assert.Equal(136, group.GroupIndex);
        Assert.Equal(new ushort[] { 100, 101, 102, 103 }, group.ActorSequences);
        Assert.Equal(14400, Assert.Single(group.PendingLoot).ItemId);
    }

    [Theory]
    [InlineData(false, 6, null, 6)]
    [InlineData(false, 26, null, 26)]
    [InlineData(false, 0, null, 55)]
    [InlineData(false, 0, -2, 53)]
    [InlineData(true, 6, null, 86)]
    [InlineData(true, 26, null, 86)]
    [InlineData(true, 55, null, 86)]
    [InlineData(true, 0, null, 86)]
    [InlineData(true, 0, -2, 86)]
    public async Task Materialized_level_is_shared_by_cell_drop_and_kill_across_rooms(
        bool mirror, byte declaredLevel, int? offset, byte expected)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var start = new MazeCell(0, 0);
            var next = new MazeCell(1, 0);
            var layout = new DungeonLayout(4, 0, 2, 1, start, next, 123, 456,
                new Dictionary<MazeCell, DungeonCellDefinition>
                { [start] = DungeonCellDefinition.Fixed(123), [next] = DungeonCellDefinition.Fixed(124) },
                BasisLevel: mirror ? (byte)86 : (byte)55)
            { FixedMonsterLevel = mirror ? (byte)86 : null };
            var source = new LevelRoomSource(new(61670, declaredLevel, 0, offset));
            var rooms = new DungeonRoomRuleService(source, source, source, source);
            var loot = new RecordingLoot();
            var factory = new DungeonCellFactory(rooms, new NoPlacement(), loot,
                new MonsterBoundary(), new RecordingQuestLoot(), new NoQuestBonus(), new NoHellRewards(), new NoLostLandDrops());
            DungeonRunParticipant[] members = [new(new(1), new(10), 0, true)];
            var runs = TestParties.Runs();
            var runId = DungeonRunId.New();
            var seed = await factory.CreateAsync(new(runId, layout, start, 123, 0,
                DateTimeOffset.UnixEpoch, members), default);
            var run = runs.PreparePartyEntry(members, layout, 0, seed, runId, DateTimeOffset.UnixEpoch).Run;
            foreach (var cell in new[] { start, next })
            {
                if (cell == next)
                {
                    seed = await factory.CreateAsync(new(runId, layout, cell, 124, 0,
                        DateTimeOffset.UnixEpoch, members), default);
                    run = Assert.IsType<DungeonRunState>(runs.TryMove(new(10), run.RunId, run.RoomVersion, cell, seed));
                }
                var actor = Assert.Single(seed.Actors);
                Assert.Equal(expected, actor.Actor.Level);
                Assert.Null(actor.Actor.BasisLevelOffset);
                Assert.Equal(expected, loot.Context!.MonsterLevel);
                var identification = await new MonsterKillDomainService().IdentifyAsync(run, actor.Sequence, default);
                Assert.Equal(expected, identification.Kill!.Value.Level);
                var claim = Assert.IsType<DungeonActorDeathClaim>(runs.TryClaimActorDeath(new(10), actor.Sequence));
                run = Assert.IsType<DungeonRunState>(runs.CommitActorDeath(claim));
                Assert.Equal(expected, run.Kills.Last().MonsterLevel);
            }
        });
    }

    [Theory]
    [InlineData(4, DungeonActorSpawnSource.Monster, false)]
    [InlineData(5, DungeonActorSpawnSource.AiCharacter, false)]
    [InlineData(8, DungeonActorSpawnSource.AiCharacter, false)]
    [InlineData(0, DungeonActorSpawnSource.Monster, true)]
    [InlineData(0, DungeonActorSpawnSource.ConditionalSummon, false)]
    [InlineData(0, DungeonActorSpawnSource.SpecialPassiveObject, false)]
    public async Task Fixed_level_does_not_override_APC_dummy_or_script_owned_templates(
        byte type, DungeonActorSpawnSource spawnSource, bool dummy)
    {
        var cell = new MazeCell(0, 0);
        var layout = new DungeonLayout(4, 0, 1, 1, cell, cell, 123, 456,
            new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(123) },
            BasisLevel: 86) { FixedMonsterLevel = 86 };
        var source = new LevelRoomSource(new DungeonRoomActor(61670, 26, type)
        { Declaration = new(61670, spawnSource, 0) { IsDummy = dummy, ParentObjectIndex = 0, ChildIndex = 0 } });
        var contents = await new DungeonRoomRuleService(source, source, source, source)
            .DescribeAsync(new(DungeonRunId.New(), layout, cell, 123, 0, DateTimeOffset.UnixEpoch, []), default);
        Assert.Equal((byte)26, Assert.Single(contents.Actors).Actor.Level);
    }

    private sealed class LevelRoomSource(DungeonRoomActor actor) : IDungeonRoomActorSource,
        IDungeonRoomDropSource, IObjectDropRuleService, IDungeonRoomObjectSource
    {
        public ValueTask<IReadOnlyList<DungeonRoomActor>> GetAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<DungeonRoomActor>>([actor]);
        public ValueTask<IReadOnlyList<ObjectDropDefinition>> GetAsync(int dungeon, int map, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<ObjectDropDefinition>>([]);
        public IReadOnlyList<DungeonRoomDrop> Draw(IReadOnlyList<ObjectDropDefinition> definitions, DungeonCellCreationContext context) => [];
        public ValueTask<IReadOnlyList<DungeonRoomObject>> GetObjectsAsync(int map, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<DungeonRoomObject>>([]);
    }

    private sealed class HellRoom : IDungeonRoomRuleService
    {
        public ValueTask<DungeonRoomContents> DescribeAsync(DungeonCellCreationContext context, CancellationToken ct) =>
            ValueTask.FromResult(new DungeonRoomContents(Enumerable.Range(0, 4).Select(i =>
                new RoomActorDescription(new(20444, 75, 5), true)
                { IsHellTemplate = true, HellGroupIndex = 136, HellOrder = (ushort)(1 + i / 2) }).ToArray(), [], 100));
        public IReadOnlyList<RoomActorDescription> Describe(IReadOnlyList<DungeonRoomActor> actors) => throw new NotSupportedException();
        public ushort FirstActorSequenceFor(DungeonCellCreationContext context) => 100;
    }

    private sealed class FrozenHellRewards : IHellRewardDomainService
    {
        public ValueTask<HellActorReward> FreezeActorAsync(int code, MonsterDropContext context, CancellationToken ct) =>
            ValueTask.FromResult(new HellActorReward([new(3330, 1, DropRarity.Common, DropItemKind.Stackable)], true));
        public ValueTask<IReadOnlyList<RolledDrop>> FreezeGroupAsync(HellGroupRewardContext context, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<RolledDrop>>([new(14400, 1, DropRarity.Rare, DropItemKind.Equipment, 25)]);
    }

    [Theory]
    [InlineData(3, true, 1)]
    [InlineData(3, true, 2)]
    [InlineData(4, false, 2)]
    [InlineData(5, false, 2)]
    [InlineData(5, false, 2, true)]
    [InlineData(8, false, 1, true)]
    [InlineData(3, false, 1, false, true)]
    [InlineData(0, true, 1, false, false, true)]
    [InlineData(0, true, 1, false, false, true, true)]
    public async Task Cell_creation_freezes_monster_clear_and_loot_semantics(
        byte actorType,
        bool isMonster, int memberCount, bool hostile = false, bool dummy = false,
        bool objectTemplate = false, bool sceneWave = false)
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
            Cells: new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(61000)});
        var participants = Enumerable.Range(0, memberCount)
            .Select(index => new DungeonRunParticipant(new AccountId(index + 1),
                new CharacterId(index + 10), Slot: (byte)index, IsLeader: index == 0)).ToArray();
        var factory = new DungeonCellFactory(
            new OneActorRoom(actorType, hostile, dummy, objectTemplate, sceneWave),
            new NoPlacement(),
            loot,
            new MonsterBoundary(), new RecordingQuestLoot(), new NoQuestBonus(), new NoHellRewards(), new NoLostLandDrops());

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
        Assert.Equal(isMonster, actor.IsNamedMonster);
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
        var blocksClear = (isMonster || hostile) && !dummy && (!objectTemplate || sceneWave);
        Assert.Equal(blocksClear, actor.BlocksClear);
        Assert.Equal(!blocksClear, seed.Snapshot().IsCleared);
        if (objectTemplate)
        {
            Assert.Equal((byte)2, actor.ObjectTemplateOwner);
            Assert.Equal(3, actor.ObjectTemplateIndex);
        }
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
        await TestGameplay.RunAsync(async () =>
        {
            var questLoot = new RecordingQuestLoot();
            var cell = new MazeCell(0, 0);
            var layout = new DungeonLayout(84, 0, 1, 1, cell, cell, 123, 456,
                new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(123)});
            DungeonRunParticipant[] members = [new(new(1), new(10), 0, true), new(new(2), new(20), 1, false)];
            var factory = new DungeonCellFactory(new ObjectRoom(), new NoPlacement(), new RecordingLoot(),
                new MonsterBoundary(), questLoot, new NoQuestBonus(), new NoHellRewards(), new NoLostLandDrops());
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
            var runs = TestParties.Runs();
            TestParties.EnsureEntryParty(runs, members);
            var run = runs.PreparePartyEntry(members, layout, 0, initialCell: seed, DungeonRunId.New(), DateTimeOffset.UnixEpoch).Run;
            Assert.Equal(3, run.CurrentCellState!.Objects.Count);
            // A caller cannot mutate the run through the unpublished seed's pending array.
            ((RolledDrop[])seed.Objects[0].PendingLoot)[0] = default;
            Assert.Equal(10149153, run.CurrentCellState.Objects[0].PendingLoot[0].ItemId);
        });
    }

    private sealed class NoQuestBonus : IQuestHelperGroundBonusDomainService
    {
        public ValueTask<DungeonCellSeed> ApplyAsync(DungeonCellCreationContext context, DungeonCellSeed cell,
            CancellationToken cancellationToken) => ValueTask.FromResult(cell);
    }

    private sealed class NoHellRewards : IHellRewardDomainService
    {
        public ValueTask<HellActorReward> FreezeActorAsync(int code, MonsterDropContext context, CancellationToken ct) =>
            throw new NotSupportedException();
        public ValueTask<IReadOnlyList<RolledDrop>> FreezeGroupAsync(HellGroupRewardContext context, CancellationToken ct) =>
            throw new NotSupportedException();
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

    private sealed class OneActorRoom(byte actorType, bool hostile = false, bool dummy = false,
        bool objectTemplate = false, bool sceneWave = false) : IDungeonRoomRuleService
    {
        public ValueTask<DungeonRoomContents> DescribeAsync(
            DungeonCellCreationContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DungeonRoomContents(
                [new RoomActorDescription(
                    new DungeonRoomActor(61670, 1, actorType, RandomDropCount: 3)
                        { IsHostile = actorType <= 3 || hostile,
                            Declaration = dummy || objectTemplate ? new(61670, objectTemplate
                                ? DungeonActorSpawnSource.SpecialPassiveObject : DungeonActorSpawnSource.Monster, 0)
                            { IsDummy = dummy, IsSceneOwnedWave = sceneWave, ParentObjectIndex = 2, ChildIndex = 3 } : null },
                    IsAiCharacter: actorType >= 5)
                    { IsNamedMonster = true, ObjectTemplateOwner = objectTemplate ? (byte)2 : null, ObjectTemplateIndex = objectTemplate ? 3 : 0 }],
                [],
                FirstActorSequence: 100));

        public IReadOnlyList<RoomActorDescription> Describe(
            IReadOnlyList<DungeonRoomActor> declared) => throw new NotSupportedException();

        public ushort FirstActorSequenceFor(DungeonCellCreationContext context) => 100;
    }

    private sealed class RecordingLoot : IMonsterDropDomainService
    {
        public ValueTask<IReadOnlyList<RolledDrop>> GenerateIndependentAsync(
            int monsterId, MonsterDropContext context, int growthBonusPercent, CancellationToken ct) => throw new NotSupportedException();
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
        public Dfo.Server.Abstractions.World.CharacterExperienceScale CharacterScale() => new(Progression().LevelThresholds);
        public uint MobReward(int level) => Dfo.Server.Application.World.MonsterExperienceWorth.For(level, Dfo.Server.Abstractions.World.MonsterWorthCurve.Recovered);
        public Dfo.Server.Abstractions.Effects.ExperienceAwardDecision DecideAward(Dfo.Server.Data.Characters.CharacterProgressionState before,
            Dfo.Server.Abstractions.World.ExperienceBreakdown breakdown) => Dfo.Server.Application.World.ExperienceProgressionRules.Decide(before, breakdown, CharacterScale());
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
