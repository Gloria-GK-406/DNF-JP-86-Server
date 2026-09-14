using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
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
            new OneQuestDefinition(Quest1776()),
            new OneCharacterLog(log));

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

    [Fact]
    public async Task AnAiCharacterDeathAdvancesItsTargetWithoutAwardingExperience()
    {
        var log = new OneLog(new CarriedQuest(0, 1776, 1));
        var handler = new ApplyMonsterKillCommandHandler(
            new UnusedValuation(),
            new UnusedBonuses(),
            new ExperienceMustNotBeAwarded(),
            new OneQuestDefinition(Quest1776(HuntEnemyTargetTypes.AiCharacter)),
            new OneCharacterLog(log));

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
        var handler = new ApplyMonsterKillCommandHandler(
            valuation,
            new UnusedBonuses(),
            new PricingOnlyExperience(),
            new OneQuestDefinition(Quest1776()),
            new OneCharacterLog(new OneLog(new CarriedQuest(0, 1776, 1))));

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

    [Theory]
    [InlineData(145, 0, 13099, 3)]
    [InlineData(144, 0, 13100, 3)]
    [InlineData(144, 0, 13099, 2)]
    public async Task AThingTheObjectiveDoesNotNameDoesNotAdvance(
        int dungeonId,
        byte difficulty,
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
        new(new OneQuestDefinition(Quest1776()), new OneCharacterLog(log));

    private static AdvanceHuntEnemyQuestCommand Command(
        int dungeonId,
        byte difficulty,
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
        public IReadOnlyList<ExperienceModifier> Collect(in ExperienceOccasion occasion) =>
            throw new NotSupportedException();
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
        public ValueTask<QuestFinishVerdict> FinishAsync(int questId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<bool> GiveUpAsync(int questId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask TrackAsync(IReadOnlyList<int> questIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
