using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Quests;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Parties;
using Dfo.Server.Protocol.Tests.Game.Party;
using Dfo.Server.Protocol.Game.MonsterDeath;
using QuestTaken = Dfo.Server.Abstractions.Quests.QuestAcceptance;

namespace Dfo.Server.Protocol.Tests.Game.MonsterDeath;

public sealed class AdvanceHuntEnemyQuestCommandTests
{
    private static readonly AccountId Account = new(6);
    private static readonly CharacterId Character = new(60);

    [Fact]
    public async Task AServerVerifiedMonsterTargetAdvancesFromOneToZero()
    {
        var log = new OneLog(new CarriedQuest(0, 1776, 1));
        var handler = Handler(log);

        var result = await handler.HandleAsync(
            Context(),
            Command(
                dungeonId: 144,
                difficulty: 0,
                enemyCode: 13099,
                enemyType: HuntEnemyTargetTypes.Monster),
            CancellationToken.None);

        Assert.Equal(new CarriedQuest(0, 1776, 0), log.Carried);
        Assert.Equal(
            [new HuntEnemyQuestAdvance(1776, 1, 0)],
            result.Advances);
    }

    [Fact]
    public async Task TheAtomicKillUsesMonsterFamilyRatherThanMonsterRankForQuestMatching()
    {
        var log = new OneLog(new CarriedQuest(0, 1776, 1));
        var handler = new ApplyMonsterKillCommandHandler(
            new UnusedValuation(),
            new UnusedBonuses(),
            new NoExperienceAward(),
            new OneCharacterLog(log),
            new CharacterContainerDomainService(),
            Progress(log),
            new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService());

        var result = await handler.HandleAsync(
            new CommandExecutionContext(
                Guid.Empty,
                typeof(ApplyMonsterKillCommand),
                typeof(ApplyMonsterKillCommandHandler),
                CommandExecutionKind.Transactional),
            new ApplyMonsterKillCommand(
                Account,
                Character,
                new KilledMonster(13099, 5, MonsterRank.Boss),
                144,
                0,
                1),
            CancellationToken.None);

        Assert.Equal([new HuntEnemyQuestAdvance(1776, 1, 0)], result.QuestAdvances);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task OnlyTheFinalBossKillReportsDungeonClearWithinItsQuestTransaction(
        bool finalBoss,
        int expectedTrigger)
    {
        var log = new OneLog(new CarriedQuest(0, 1781, 1));
        var definition = new QuestDefinition(
            1781, "", null, null, null, null, null, null, [],
            "condition under clear", "", null, null, null, null, null, null,
            null, [], [], [], [],
            DungeonClearRequirement: new DungeonClearRequirement(146, -1));
        var handler = new ApplyMonsterKillCommandHandler(
            new UnusedValuation(), new UnusedBonuses(), new NoExperienceAward(),
            new OneCharacterLog(log), new CharacterContainerDomainService(),
            Progress(log, definition),
            new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService());

        var result = await handler.HandleAsync(
            new CommandExecutionContext(Guid.Empty, typeof(ApplyMonsterKillCommand),
                typeof(ApplyMonsterKillCommandHandler), CommandExecutionKind.Transactional),
            new ApplyMonsterKillCommand(
                Account, Character, new KilledMonster(3001, 5, MonsterRank.Boss),
                146, 0, 1, IsFinalBossDeath: finalBoss),
            CancellationToken.None);

        Assert.Equal(expectedTrigger, log.Carried.TriggerValue);
        Assert.Equal(finalBoss, result.QuestProgress is not null);
    }

    [Fact]
    public async Task AnAiCharacterDeathAdvancesItsTargetWithoutAwardingExperience()
    {
        var log = new OneLog(new CarriedQuest(0, 1776, 1));
        var handler = new ApplyMonsterKillCommandHandler(
            new UnusedValuation(),
            new UnusedBonuses(),
            new ExperienceMustNotBeAwarded(),
            new OneCharacterLog(log),
            new CharacterContainerDomainService(),
            Progress(log, Quest1776(HuntEnemyTargetTypes.AiCharacter)),
            new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService());

        var result = await handler.HandleAsync(
            new CommandExecutionContext(
                Guid.Empty,
                typeof(ApplyMonsterKillCommand),
                typeof(ApplyMonsterKillCommandHandler),
                CommandExecutionKind.Transactional),
            new ApplyMonsterKillCommand(
                Account,
                Character,
                new KilledMonster(13099, 5, MonsterRank.Ordinary),
                144,
                0,
                1,
                HuntEnemyTargetTypes.AiCharacter),
            CancellationToken.None);

        Assert.Null(result.Award);
        Assert.Equal([new HuntEnemyQuestAdvance(1776, 1, 0)], result.QuestAdvances);
    }

    [Fact]
    public async Task TheAtomicKillCarriesTheFrozenPartySizeIntoExperienceValuation()
    {
        var valuation = new RecordingValuation();
        var log = new OneLog(new CarriedQuest(0, 1776, 1));
        var handler = new ApplyMonsterKillCommandHandler(
            valuation,
            new UnusedBonuses(),
            new PricingOnlyExperience(),
            new OneCharacterLog(log),
            new CharacterContainerDomainService(),
            Progress(log),
            new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService());

        await handler.HandleAsync(
            new CommandExecutionContext(
                Guid.Empty,
                typeof(ApplyMonsterKillCommand),
                typeof(ApplyMonsterKillCommandHandler),
                CommandExecutionKind.Transactional),
            new ApplyMonsterKillCommand(
                Account,
                Character,
                new KilledMonster(Code: 13098, Level: 5, Rank: MonsterRank.Ordinary),
                DungeonId: 144,
                Difficulty: 0,
                DungeonWeight: 1,
                PartyMemberCount: 2),
            CancellationToken.None);

        Assert.Equal(2, valuation.Kill?.PartyMemberCount);
    }

    [Fact]
    public async Task A_direct_quest_item_is_banked_for_its_owner_and_capped_again_at_kill_time()
    {
        var bag = new DirectGrantBag(14);
        var containers = new CharacterContainerDomainService();
        containers.Register(Character, bag);
        var seeking = new RecordingSeekingProgress();
        var log = new OneLog(new CarriedQuest(0, 1776, 1));
        var handler = new ApplyMonsterKillCommandHandler(
            new UnusedValuation(),
            new UnusedBonuses(),
            new NoExperienceAward(),
            new OneCharacterLog(log),
            containers,
            Progress(log, seeking: seeking),
            new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService());

        var result = await handler.HandleAsync(
            new CommandExecutionContext(
                Guid.Empty,
                typeof(ApplyMonsterKillCommand),
                typeof(ApplyMonsterKillCommandHandler),
                CommandExecutionKind.Transactional),
            new ApplyMonsterKillCommand(
                Account, Character,
                new KilledMonster(13098, 5, MonsterRank.Ordinary),
                144, 0, 1,
                EnemyType: HuntEnemyTargetTypes.AiCharacter,
                PendingLoot:
                [
                    new RolledDrop(10149163, 3, DropRarity.Common, DropItemKind.Stackable,
                        OwnerCharacterId: Character,
                        Delivery: RolledDropDelivery.DirectQuestItem,
                        QuestId: 1776, GrantLimit: 15),
                    new RolledDrop(10149163, 1, DropRarity.Common, DropItemKind.Stackable,
                        OwnerCharacterId: new CharacterId(61),
                        Delivery: RolledDropDelivery.DirectQuestItem,
                        QuestId: 1776, GrantLimit: 15),
                ]),
            CancellationToken.None);

        Assert.Equal(15, bag.Count);
        Assert.Single(result.QuestItems!.Items);
        Assert.Equal(1, seeking.TimesAsked);
    }

    [Fact]
    public async Task Object_claim_grants_only_its_owners_active_quest_item_and_commits_without_experience()
    {
        var scope = new Dfo.Server.Application.Transactions.DomainTransactionScope();
        var runs = new Dfo.Server.Application.World.DungeonRunDomainService(scope);
        var sessions = new Dfo.Server.Application.Sessions.SelectedSessionDomainService();
        var session = SelectedSessionIdentity.Create(
            Account, Character, new Dfo.Server.Data.Networking.NetworkConnectionId("object"), 1, 10010);
        sessions.Select(session);
        var layout = new DungeonLayout(84, 0, 1, 1, new(0, 0), new(0, 0), 10, 0,
            new Dictionary<MazeCell, int> { [new(0, 0)] = 10 });
        var seed = DungeonCellSeed.CompleteEmpty(new(0, 0), 10) with
        {
            Objects = [new(new(new(DungeonObjectSection.SpecialPassive, 0), 8961, 10, 10, 0),
                [new(10149163, 3, DropRarity.Common, DropItemKind.Stackable,
                    OwnerCharacterId: Character, Delivery: RolledDropDelivery.DirectQuestItem,
                    QuestId: 1776, GrantLimit: 15)])],
        };
        var run = runs.PreparePartyEntry([new(Account, Character, 0, true)], layout,
            DungeonDifficulty.Normal, seed, DungeonRunId.New(), DateTimeOffset.UtcNow).Run;
        var bag = new DirectGrantBag(14);
        var containers = new CharacterContainerDomainService();
        var log = new OneLog(new CarriedQuest(0, 1776, 1));
        var seeking = new RecordingSeekingProgress();
        var handler = new ApplyObjectDestructionCommandHandler(runs, sessions, new OneCharacterLog(log),
            containers, new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService(),
            Progress(log, seeking: seeking));
        var context = new CommandExecutionContext(Guid.Empty, typeof(ApplyObjectDestructionCommand),
            typeof(ApplyObjectDestructionCommandHandler), CommandExecutionKind.Transactional);
        var command = new ApplyObjectDestructionCommand(session, run.RunId, run.RoomVersion, 8961);

        // An unavailable inventory must not eat the queued reward.
        await using (var failed = scope.Begin())
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await handler.HandleAsync(context, command, default));
        Assert.False(runs.Find(Character)!.CurrentCellState.Objects[0].IsConsumed);
        containers.Register(Character, bag);
        await using (var tx = scope.Begin())
        {
            var result = await handler.HandleAsync(context, command, default);
            Assert.Null(result.Effects!.Award);
            Assert.Empty(result.Effects.QuestAdvances);
            Assert.Single(result.Effects.QuestItems!.Items);
            Assert.Equal(15, bag.Count);
            Assert.False(runs.Find(Character)!.CurrentCellState.Objects[0].IsConsumed);
            await tx.CommitAsync(default);
        }
        Assert.True(runs.Find(Character)!.CurrentCellState.Objects[0].IsConsumed);
        Assert.Empty(runs.Find(Character)!.Kills);
        Assert.Equal(1, seeking.TimesAsked);
        await using var replay = scope.Begin();
        Assert.Null((await handler.HandleAsync(context, command, default)).Effects);
        Assert.Equal(15, bag.Count);
    }

    [Fact]
    public async Task A_departed_owner_does_not_receive_an_ungranted_direct_quest_item()
    {
        var bag = new DirectGrantBag(14);
        var containers = new CharacterContainerDomainService();
        containers.Register(Character, bag);
        var log = new OneLog(new CarriedQuest(0, 1776, 1));
        var handler = new ApplyMonsterKillCommandHandler(
            new UnusedValuation(), new UnusedBonuses(), new NoExperienceAward(),
            new OneCharacterLog(log), containers, Progress(log),
            new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService(),
            new VirtualOnlyPartyService());

        var result = await handler.HandleAsync(
            new CommandExecutionContext(Guid.Empty, typeof(ApplyMonsterKillCommand),
                typeof(ApplyMonsterKillCommandHandler), CommandExecutionKind.Transactional),
            new ApplyMonsterKillCommand(
                Account, Character, new KilledMonster(13098, 5, MonsterRank.Ordinary),
                144, 0, 1, EnemyType: HuntEnemyTargetTypes.AiCharacter,
                PendingLoot:
                [
                    new RolledDrop(10149163, 1, DropRarity.Common, DropItemKind.Stackable,
                        OwnerCharacterId: Character,
                        Delivery: RolledDropDelivery.DirectQuestItem,
                        QuestId: 1776, GrantLimit: 15),
                ],
                PartyKey: PartyKey.Virtual(Character)),
            CancellationToken.None);

        Assert.Equal(14, bag.Count);
        Assert.Null(result.QuestItems);
    }

    [Theory]
    [InlineData(145, DungeonDifficulty.Normal, 13099, 3)]
    [InlineData(144, DungeonDifficulty.Normal, 13100, 3)]
    [InlineData(144, DungeonDifficulty.Normal, 13099, 2)]
    public async Task AThingTheObjectiveDoesNotNameDoesNotAdvance(
        int dungeonId,
        DungeonDifficulty difficulty,
        int enemyCode,
        int enemyType)
    {
        var log = new OneLog(new CarriedQuest(0, 1776, 1));

        var result = await Handler(log).HandleAsync(
            Context(),
            Command(dungeonId, difficulty, enemyCode, enemyType),
            CancellationToken.None);

        Assert.Equal(1, log.Carried.TriggerValue);
        Assert.Empty(result.Advances);
    }

    private static AdvanceHuntEnemyQuestCommandHandler Handler(OneLog log) =>
        new(Progress(log));

    private static IQuestProgressDomainService Progress(
        OneLog log,
        QuestDefinition? definition = null,
        ISeekingQuestProgressDomainService? seeking = null) =>
        new QuestProgressDomainService(
            new OneQuestDefinition(definition ?? Quest1776()),
            new OneCharacterLog(log),
            new UnusedClientReports(),
            seeking ?? new NoSeekingProgress());

    private sealed class UnusedClientReports : IQuestTriggerDomainService
    {
        public ValueTask<QuestAdvance> ApplyClientReportAsync(
            AccountId accountId, CharacterId characterId, QuestDefinition definition,
            byte selector, bool increment, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static AdvanceHuntEnemyQuestCommand Command(
        int dungeonId,
        DungeonDifficulty difficulty,
        int enemyCode,
        int enemyType) =>
        new(Account, Character, dungeonId, difficulty, enemyCode, enemyType);

    private static CommandExecutionContext Context() => new(
        Guid.Empty,
        typeof(AdvanceHuntEnemyQuestCommand),
        typeof(AdvanceHuntEnemyQuestCommandHandler),
        CommandExecutionKind.Transactional);

    private static QuestDefinition Quest1776(
        int enemyType = HuntEnemyTargetTypes.Monster) =>
        new(1776, "", null, null, null, null, null, null, [], "hunt enemy", "",
            null, null, null, null, null, null, null, [], [], [], [],
            InitialTrigger: 1,
            HuntEnemyRequirements:
            [
                new HuntEnemyRequirement(
                    144, -1, 13099, enemyType, 1),
            ]);

    private sealed class OneQuestDefinition(QuestDefinition definition)
        : IQuestDefinitionDomainService
    {
        public ValueTask<QuestDefinition?> FindAsync(
            int questId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<QuestDefinition?>(
                questId == definition.QuestId ? definition : null);

        public QuestParameters RewardParameters() => throw new NotSupportedException();

        public QuestRewardResolution ResolveRewards(
            QuestDefinition definition,
            QuestRewardCandidate candidate,
            ushort rewardSelection,
            ushort completionCount) => throw new NotSupportedException();
    }

    private sealed class UnusedValuation : IExperienceValuationDomainService
    {
        public uint WorthOfKill(in KillCircumstances kill) =>
            throw new NotSupportedException();

        public ClearExperienceWorth WorthOfClear(in ClearCircumstances clear) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingValuation : IExperienceValuationDomainService
    {
        public KillCircumstances? Kill { get; private set; }

        public uint WorthOfKill(in KillCircumstances kill)
        {
            this.Kill = kill;
            return 0;
        }

        public ClearExperienceWorth WorthOfClear(in ClearCircumstances clear) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedBonuses : IExperienceBonusDomainService
    {
        public ValueTask<IReadOnlyList<ExperienceModifier>> CollectAsync(
            ExperienceOccasion occasion, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ExperienceModifier>>([]);
    }

    private sealed class NoExperienceAward : ICharacterExperienceDomainService
    {
        public uint? ThresholdForLevel(byte level) => throw new NotSupportedException();

        public ValueTask<bool> TrySetProgressAsync(
            CharacterId characterId,
            byte level,
            uint experience,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<ExperienceAward?> AwardAsync(
            CharacterId characterId,
            Func<byte, ExperienceValuation> valuation,
            CancellationToken cancellationToken) => ValueTask.FromResult<ExperienceAward?>(null);
    }

    private sealed class PricingOnlyExperience : ICharacterExperienceDomainService
    {
        public uint? ThresholdForLevel(byte level) => throw new NotSupportedException();

        public ValueTask<bool> TrySetProgressAsync(
            CharacterId characterId,
            byte level,
            uint experience,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<ExperienceAward?> AwardAsync(
            CharacterId characterId,
            Func<byte, ExperienceValuation> valuation,
            CancellationToken cancellationToken)
        {
            _ = valuation(3);
            return ValueTask.FromResult<ExperienceAward?>(null);
        }
    }

    private sealed class ExperienceMustNotBeAwarded : ICharacterExperienceDomainService
    {
        public uint? ThresholdForLevel(byte level) => throw new NotSupportedException();

        public ValueTask<bool> TrySetProgressAsync(
            CharacterId characterId,
            byte level,
            uint experience,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<ExperienceAward?> AwardAsync(
            CharacterId characterId,
            Func<byte, ExperienceValuation> valuation,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("AI character deaths must not award experience.");
    }

    private sealed class OneCharacterLog(IQuestLog log) : ICharacterQuestLogDomainService
    {
        public IQuestLog LogOf(AccountId accountId, CharacterId characterId)
        {
            Assert.Equal(Account, accountId);
            Assert.Equal(Character, characterId);
            return log;
        }
    }

    private sealed class NoSeekingProgress : ISeekingQuestProgressDomainService
    {
        public ValueTask<SeekingQuestProgressSynchronization?> SynchronizeAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<SeekingQuestProgressSynchronization?>(null);
    }

    private sealed class RecordingSeekingProgress : ISeekingQuestProgressDomainService
    {
        public int TimesAsked { get; private set; }

        public ValueTask<SeekingQuestProgressSynchronization?> SynchronizeAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
        {
            this.TimesAsked++;
            return ValueTask.FromResult<SeekingQuestProgressSynchronization?>(null);
        }
    }

    private sealed class DirectGrantBag(int count) : IItemContainer
    {
        public int Count { get; private set; } = count;
        public ContainerId Id => new("direct-quest-test");
        public InventoryContainerType Type => InventoryContainerType.Main;
        public ContainerShape Shape => new(this.Type, 56);
        public bool IsRemovable => false;

        private ContainerSlotItem? Slot => this.Count == 0
            ? null
            : new ContainerSlotItem(0,
                StackableItemCore.Create(6, 10149163, this.Count),
                ItemWorldClass.Interchangeable);

        public ValueTask<ContainerSlotItem?> ReadSlotAsync(
            int slotIndex, CancellationToken cancellationToken) =>
            ValueTask.FromResult(slotIndex == 0 ? this.Slot : null);

        public ValueTask<IReadOnlyList<ContainerSlotItem>> ReadAllAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ContainerSlotItem>>(
                this.Slot is { } item ? [item] : []);

        public ValueTask<ItemPlacementOutcome> AddAsync(
            int itemId, int incoming, CancellationToken cancellationToken)
        {
            Assert.Equal(10149163, itemId);
            this.Count += incoming;
            return ValueTask.FromResult<ItemPlacementOutcome>(
                new ItemPlacementOutcome.AddedToPile(0, incoming, this.Count));
        }

        public ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<ContainerSlotPair> ReadSlotsAsync(int firstSlotIndex, int secondSlotIndex,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask PutAsync(int slotIndex, InventoryItemCoreState core,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask ClearAsync(int slotIndex, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<ItemPlacementOutcome> RefillAsync(ItemRefill refill,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ItemMoveOutcome> MoveToAsync(int fromSlotIndex, int toSlotIndex, int count,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class OneLog(CarriedQuest carried) : IQuestLog
    {
        public CharacterId CharacterId => Character;

        public CarriedQuest Carried { get; private set; } = carried;

        public ValueTask<IReadOnlyList<CarriedQuest>> CarriedAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<CarriedQuest>>([this.Carried]);

        public ValueTask<CarriedQuest?> FindCarriedAsync(
            int questId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<CarriedQuest?>(
                questId == this.Carried.QuestId ? this.Carried : null);

        public ValueTask<bool> RecordProgressAsync(
            CarriedQuest asLastSeen,
            int triggerValue,
            CancellationToken cancellationToken)
        {
            if (asLastSeen != this.Carried)
                return ValueTask.FromResult(false);
            this.Carried = this.Carried with { TriggerValue = triggerValue };
            return ValueTask.FromResult(true);
        }

        public ValueTask<IReadOnlyList<ClearedQuest>> ClearedAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<IReadOnlyList<int>> TrackedAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<QuestTaken> AcceptAsync(QuestDefinition definition, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<QuestAdvance> AdvanceAsync(QuestDefinition definition, byte selector, bool increment, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<bool> CompleteAsync(int questId, byte mark, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<IReadOnlyList<int>> CompleteWithoutRewardsAsync(
            IReadOnlyCollection<int> questIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<QuestFinishVerdict> FinishAsync(int questId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<bool> GiveUpAsync(int questId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask TrackAsync(IReadOnlyList<int> questIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
