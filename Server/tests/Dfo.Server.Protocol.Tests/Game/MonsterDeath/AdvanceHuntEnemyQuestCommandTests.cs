using Dfo.Server.Protocol.Tests.Support;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Infrastructure.Content;
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
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.MonsterDeath;

public sealed class AdvanceHuntEnemyQuestCommandTests
{
    private static readonly AccountId Account = new(6);
    private static readonly CharacterId Character = new(60);

    [Fact]
    public async Task AServerVerifiedMonsterTargetAdvancesFromOneToZero()
    {
        await TestGameplay.RunAsync(async () =>
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
                [new QuestAdvanceFact(1776, 1, 0)],
                result.Advances);
        });
    }

    [Fact]
    public async Task TheAtomicKillUsesMonsterFamilyRatherThanMonsterRankForQuestMatching()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var log = new OneLog(new CarriedQuest(0, 1776, 1));
            var handler = new Dfo.Server.Application.World.MonsterKillRewardsDomainService(new NoExperienceAward(),
                new Dfo.Server.Application.World.QuestItemDropDomainService(new OneCharacterLog(log), new CharacterContainerDomainService(TestRequired.TransactionScope()),
                    new Dfo.Server.Persistence.Tests.Support.BagRewardDelivery(new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService(), new CharacterContainerDomainService(TestRequired.TransactionScope()))),
                Progress(log), clock: TimeProvider.System, parties: TestRequired.Absent<Dfo.Server.Abstractions.Parties.IPartyService>(), gameplay: TestRequired.Absent<Dfo.Server.Abstractions.Achievements.ICharacterGameplayFactDomainService>());

            var result = await TestEffectFootprints.Run(new Dfo.Server.Abstractions.Effects.EffectFootprintOwner(Account, Character, null), cap => MonsterRewardDomainDriver.ApplyAsync(handler, new MonsterRewardFacts(
                    Account,
                    Character,
                    new MonsterRewardIdentity(13099, 5, MonsterRank.Boss),
                    144,
                    0,
                    1), cap, CancellationToken.None));

            Assert.Equal([new QuestAdvanceFact(1776, 1, 0)], result.QuestAdvances);
        });
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task OnlyTheFinalBossKillReportsDungeonClearWithinItsQuestTransaction(
        bool finalBoss,
        int expectedTrigger)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var log = new OneLog(new CarriedQuest(0, 1781, 1));
            var definition = new QuestDefinition(
                1781, "", null, null, null, null, null, null, [],
                "condition under clear", "", null, null, null, null, null, null,
                null, [], [], [], [],
                DungeonClearRequirement: new DungeonClearRequirement(146, -1));
            var handler = new Dfo.Server.Application.World.MonsterKillRewardsDomainService(new NoExperienceAward(),
                new Dfo.Server.Application.World.QuestItemDropDomainService(new OneCharacterLog(log), new CharacterContainerDomainService(TestRequired.TransactionScope()),
                    new Dfo.Server.Persistence.Tests.Support.BagRewardDelivery(new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService(), new CharacterContainerDomainService(TestRequired.TransactionScope()))),
                Progress(log, definition), clock: TimeProvider.System, parties: TestRequired.Absent<Dfo.Server.Abstractions.Parties.IPartyService>(), gameplay: TestRequired.Absent<Dfo.Server.Abstractions.Achievements.ICharacterGameplayFactDomainService>());

            var result = await TestEffectFootprints.Run(new Dfo.Server.Abstractions.Effects.EffectFootprintOwner(Account, Character, null), cap => MonsterRewardDomainDriver.ApplyAsync(handler, new MonsterRewardFacts(
                    Account, Character, new MonsterRewardIdentity(3001, 5, MonsterRank.Boss),
                    146, 0, 1, IsFinalBossDeath: finalBoss), cap, CancellationToken.None));

            Assert.Equal(expectedTrigger, log.Carried.TriggerValue);
            Assert.Equal(finalBoss, result.QuestProgress is not null);
        });
    }

    [Fact]
    public async Task AnAiCharacterDeathAdvancesItsTargetWithoutAwardingExperience()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var log = new OneLog(new CarriedQuest(0, 1776, 1));
            var handler = new Dfo.Server.Application.World.MonsterKillRewardsDomainService(new ExperienceMustNotBeAwarded(),
                new Dfo.Server.Application.World.QuestItemDropDomainService(new OneCharacterLog(log), new CharacterContainerDomainService(TestRequired.TransactionScope()),
                    new Dfo.Server.Persistence.Tests.Support.BagRewardDelivery(new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService(), new CharacterContainerDomainService(TestRequired.TransactionScope()))),
                Progress(log, Quest1776(HuntEnemyTargetTypes.AiCharacter)), clock: TimeProvider.System, parties: TestRequired.Absent<Dfo.Server.Abstractions.Parties.IPartyService>(), gameplay: TestRequired.Absent<Dfo.Server.Abstractions.Achievements.ICharacterGameplayFactDomainService>());

            var result = await TestEffectFootprints.Run(new Dfo.Server.Abstractions.Effects.EffectFootprintOwner(Account, Character, null), cap => MonsterRewardDomainDriver.ApplyAsync(handler, new MonsterRewardFacts(
                    Account,
                    Character,
                    new MonsterRewardIdentity(13099, 5, MonsterRank.Ordinary),
                    144,
                    0,
                    1,
                    HuntEnemyTargetTypes.AiCharacter), cap, CancellationToken.None));

            Assert.Null(result.Award);
            Assert.Equal([new QuestAdvanceFact(1776, 1, 0)], result.QuestAdvances);
        });
    }

    [Fact]
    public async Task TheAtomicKillCarriesTheFrozenPartySizeIntoExperienceValuation()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var valuation = new RecordingValuation();
            var log = new OneLog(new CarriedQuest(0, 1776, 1));
            var handler = new Dfo.Server.Application.World.MonsterKillRewardsDomainService(new PricingOnlyExperience(new Dfo.Server.Application.World.ExperienceCalculationDomainService(valuation, new UnusedBonuses(), tower: TestRequired.Absent<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerRewardRuleService>(), bossTower: TestRequired.Absent<Dfo.Server.Abstractions.World.BossTower.IBossTowerRewardRuleService>())),
                new Dfo.Server.Application.World.QuestItemDropDomainService(new OneCharacterLog(log), new CharacterContainerDomainService(TestRequired.TransactionScope()),
                    new Dfo.Server.Persistence.Tests.Support.BagRewardDelivery(new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService(), new CharacterContainerDomainService(TestRequired.TransactionScope()))),
                Progress(log), clock: TimeProvider.System, parties: TestRequired.Absent<Dfo.Server.Abstractions.Parties.IPartyService>(), gameplay: TestRequired.Absent<Dfo.Server.Abstractions.Achievements.ICharacterGameplayFactDomainService>());

            await TestEffectFootprints.Run(new Dfo.Server.Abstractions.Effects.EffectFootprintOwner(Account, Character, null), cap => MonsterRewardDomainDriver.ApplyAsync(handler, new MonsterRewardFacts(
                    Account,
                    Character,
                    new MonsterRewardIdentity(Code: 13098, Level: 5, Rank: MonsterRank.Ordinary),
                    DungeonId: 144,
                    Difficulty: 0,
                    DungeonWeight: 1,
                    PartyMemberCount: 2), cap, CancellationToken.None));

            Assert.Equal(2, valuation.Kill?.PartyMemberCount);
        });
    }

    [Fact]
    public async Task A_direct_quest_item_is_banked_for_its_owner_and_capped_again_at_kill_time()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var bag = new DirectGrantBag(14);
            var containers = new CharacterContainerDomainService(TestRequired.TransactionScope());
            containers.Register(Character, bag);
            var seeking = new RecordingSeekingProgress();
            var log = new OneLog(new CarriedQuest(0, 1776, 1));
            var handler = new Dfo.Server.Application.World.MonsterKillRewardsDomainService(new NoExperienceAward(),
                new Dfo.Server.Application.World.QuestItemDropDomainService(new OneCharacterLog(log), containers,
                    new Dfo.Server.Persistence.Tests.Support.BagRewardDelivery(new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService(), containers)),
                Progress(log, seeking: seeking), clock: TimeProvider.System, parties: TestRequired.Absent<Dfo.Server.Abstractions.Parties.IPartyService>(), gameplay: TestRequired.Absent<Dfo.Server.Abstractions.Achievements.ICharacterGameplayFactDomainService>());

            var result = await TestEffectFootprints.Run(new Dfo.Server.Abstractions.Effects.EffectFootprintOwner(Account, Character, null), cap => MonsterRewardDomainDriver.ApplyAsync(handler, new MonsterRewardFacts(
                    Account, Character,
                    new MonsterRewardIdentity(13098, 5, MonsterRank.Ordinary),
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
                    ]), cap, CancellationToken.None));

            Assert.Equal(15, bag.Count);
            Assert.Single(result.QuestItems!.Items);
            Assert.Equal(1, seeking.TimesAsked);
        });
    }

    [Fact]
    public async Task Object_claim_grants_only_its_owners_active_quest_item_and_commits_without_experience()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var scope = new Dfo.Server.Application.Transactions.DomainTransactionScope(logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<Dfo.Server.Application.Transactions.DomainTransaction>.Instance, outboxes: TestRequired.Outboxes());
            var runs = TestParties.Runs(scope);
            var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state);
            var session = Dfo.Server.Testing.TestSessions.InWorld(sessions, state, Account, Character, "object", 10010);
            var layout = new DungeonLayout(84, 0, 1, 1, new(0, 0), new(0, 0), 10, 0,
                new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(10)});
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
            var containers = new CharacterContainerDomainService(TestRequired.TransactionScope());
            var log = new OneLog(new CarriedQuest(0, 1776, 1));
            var seeking = new RecordingSeekingProgress();
            var handler = new ObjectDestructionAlgorithmDriver(runs, sessions, new OneCharacterLog(log),
                containers, new Dfo.Server.Persistence.Tests.Support.BagRewardDelivery(new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService(), containers),
                Progress(log, seeking: seeking));
            var context = DomainCommandFixture.Context(Guid.Empty, typeof(ObjectDestructionFacts),
                typeof(ObjectDestructionAlgorithmDriver), FixtureUnitKind.Transactional);
            var command = new ObjectDestructionFacts(session, run.RunId, run.RoomVersion, 8961);

            // An unavailable inventory must not eat the queued reward.
            await using (var failed = scope.Begin())
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await handler.HandleAsync(context, command, default));
            Assert.False(runs.Find(Character)!.CurrentCellState.Objects[0].IsConsumed);
            containers.Register(Character, bag);
            await using (var tx = scope.Begin())
            {
                var result = await handler.HandleAsync(context, command, default);
                var effects = Assert.Single(result.Rewards!).Effects;
                Assert.Null(effects.Award);
                Assert.Empty(effects.QuestChanges);
                Assert.Single(effects.QuestItems!.Items);
                Assert.Equal(15, bag.Count);
                Assert.False(runs.Find(Character)!.CurrentCellState.Objects[0].IsConsumed);
                await tx.CommitAsync(default);
            }
            Assert.True(runs.Find(Character)!.CurrentCellState.Objects[0].IsConsumed);
            Assert.Empty(runs.Find(Character)!.Kills);
            Assert.Equal(1, seeking.TimesAsked);
            await using var replay = scope.Begin();
            Assert.Null((await handler.HandleAsync(context, command, default)).Rewards);
            Assert.Equal(15, bag.Count);
        });
    }

    [Theory]
    [InlineData(1776, 5, 8)]
    [InlineData(1776, 15, 15)]
    [InlineData(9999, 5, 5)]
    public async Task One_object_pays_each_active_quest_owner_with_independent_holding_caps(
        int memberQuest, int before, int expected)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = new ObjectPartyFixture(memberQuest, before);
            await using (var tx = fixture.Scope.Begin())
            {
                var result = await fixture.Apply();
                Assert.Equal(2, result.Rewards!.Count);
                Assert.Equal(15, fixture.FirstBag.Count);
                Assert.Equal(expected, fixture.SecondBag.Count);
                Assert.Equal(fixture.Command.Recipients, result.Rewards.Select(value => value.Session));
                Assert.All(result.Rewards, value => Assert.Null(value.Effects.Award));
                Assert.NotNull(result.Rewards[0].Effects.QuestItems);
                Assert.Equal(expected != before, result.Rewards[1].Effects.QuestItems is not null);
                await tx.CommitAsync(default);
            }
            Assert.True(fixture.Run.CurrentCellState.Objects[0].IsConsumed);
            Assert.Empty(fixture.Run.Kills);
            await using var again = fixture.Scope.Begin();
            Assert.Null((await fixture.Apply()).Rewards);
            Assert.Equal(expected, fixture.SecondBag.Count);
        });
    }

    [Fact]
    public async Task Failure_in_second_inventory_rolls_back_both_owners_and_releases_object()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = new ObjectPartyFixture(1776, 5);
            fixture.SecondBag.FailAdd = true;
            await using (var tx = fixture.Scope.Begin())
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Apply());
            Assert.Equal(14, fixture.FirstBag.Count);
            Assert.Equal(5, fixture.SecondBag.Count);
            Assert.False(fixture.Run.CurrentCellState.Objects[0].IsConsumed);
            fixture.SecondBag.FailAdd = false;
            await using (var tx = fixture.Scope.Begin())
            {
                Assert.Equal(2, (await fixture.Apply()).Rewards!.Count);
                await tx.CommitAsync(default);
            }
            Assert.Equal(15, fixture.FirstBag.Count);
            Assert.Equal(8, fixture.SecondBag.Count);
            Assert.True(fixture.Run.CurrentCellState.Objects[0].IsConsumed);
        });
    }

    [Fact]
    public async Task Replacement_session_or_incomplete_recipient_set_cannot_spend_object()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = new ObjectPartyFixture(1776, 5);
            await using (var tx = fixture.Scope.Begin())
                Assert.Null((await fixture.Apply(fixture.Command with
                    { Recipients = [fixture.Command.Session] })).Rewards);
            var member = fixture.Command.Recipients[1];
            _ = Dfo.Server.Testing.TestSessions.InWorld(
                fixture.Sessions, fixture.State, member.AccountId, member.CharacterId, "replacement", member.ListenerPort);
            await using var stale = fixture.Scope.Begin();
            Assert.Null((await fixture.Apply()).Rewards);
            Assert.Equal(14, fixture.FirstBag.Count);
            Assert.False(fixture.Run.CurrentCellState.Objects[0].IsConsumed);
        });
    }

    private sealed class ObjectPartyFixture
    {
        public readonly Dfo.Server.Application.Transactions.DomainTransactionScope Scope = new(logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<Dfo.Server.Application.Transactions.DomainTransaction>.Instance, outboxes: TestRequired.Outboxes());
        public readonly Dfo.Server.Application.Sessions.SelectedSessionDomainService Sessions;
        public readonly Dfo.Server.Application.Users.OnlineStateRegistry State;
        private readonly Dfo.Server.Application.World.DungeonRunDomainService runs;
        private readonly ObjectDestructionAlgorithmDriver handler;
        public readonly ObjectDestructionFacts Command;
        public readonly DirectGrantBag FirstBag;
        public readonly DirectGrantBag SecondBag;
        public DungeonRunState Run => this.runs.Find(Character)!;

        public ObjectPartyFixture(int memberQuest, int count)
        {
            this.runs = TestParties.Runs(this.Scope);
            this.Sessions = Dfo.Server.Testing.TestSessions.Directory(out this.State);
            var first = Dfo.Server.Testing.TestSessions.InWorld(this.Sessions, this.State, Account, Character, "first", 10010);
            var second = Dfo.Server.Testing.TestSessions.InWorld(this.Sessions, this.State, new(7), new(61), "second", 10011);
            var layout = new DungeonLayout(84, 0, 1, 1, new(0, 0), new(0, 0), 10, 0,
                new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(10)});
            var loot = new[] { first, second }.Select(owner => new RolledDrop(
                10149163, 3, DropRarity.Common, DropItemKind.Stackable,
                OwnerCharacterId: owner.CharacterId, Delivery: RolledDropDelivery.DirectQuestItem,
                QuestId: 1776, GrantLimit: 15)).ToArray();
            var seed = DungeonCellSeed.CompleteEmpty(new(0, 0), 10) with
            {
                Objects = [new(new(new(DungeonObjectSection.SpecialPassive, 0), 8961, 10, 10, 0), loot)],
            };
            DungeonRunParticipant[] roster =
                [new(first.AccountId, first.CharacterId, 0, true), new(second.AccountId, second.CharacterId, 1, false)];
            TestParties.EnsureEntryParty(this.runs, roster);
            var run = this.runs.PreparePartyEntry(
                roster,
                layout, DungeonDifficulty.Normal, seed, DungeonRunId.New(), DateTimeOffset.UtcNow).Run;
            this.FirstBag = new(14, this.Scope);
            this.SecondBag = new(count, this.Scope);
            var containers = new CharacterContainerDomainService(TestRequired.TransactionScope());
            containers.Register(first.CharacterId, this.FirstBag);
            containers.Register(second.CharacterId, this.SecondBag);
            var logs = new PartyLogs(new Dictionary<CharacterId, IQuestLog>
            {
                [first.CharacterId] = new OneLog(new(0, 1776, 1)),
                [second.CharacterId] = new OneLog(new(0, memberQuest, 1)),
            });
            this.handler = new(this.runs, this.Sessions, logs, containers,
                new Dfo.Server.Persistence.Tests.Support.BagRewardDelivery(new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService(), containers), new HoldingsOnlyProgress());
            this.Command = new(first, run.RunId, run.RoomVersion, 8961, [first, second]);
        }

        public ValueTask<Dfo.Server.Protocol.Game.ObjectDestructionEffects> Apply(ObjectDestructionFacts? command = null) =>
            this.handler.HandleAsync(DomainCommandFixture.Context(Guid.Empty, typeof(ObjectDestructionFacts),
                typeof(ObjectDestructionAlgorithmDriver), FixtureUnitKind.Transactional), command ?? this.Command, default);
    }

    private sealed class PartyLogs(IReadOnlyDictionary<CharacterId, IQuestLog> logs) : ICharacterQuestLogDomainService
    {
        public IQuestLog LogOf(AccountId accountId, CharacterId characterId) => logs[characterId];
    }

    private sealed class HoldingsOnlyProgress : IQuestProgressDomainService
    {
        public ValueTask<QuestProgressReport> ReportAsync(AccountId accountId, CharacterId characterId,
            IReadOnlyList<QuestProgressFact> facts, CancellationToken ct)
        {
            Assert.IsType<QuestHoldingsChanged>(Assert.Single(facts));
            return ValueTask.FromResult(new QuestProgressReport(null, [], null));
        }
    }

    [Fact]
    public async Task A_departed_owner_does_not_receive_an_ungranted_direct_quest_item()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var bag = new DirectGrantBag(14);
            var containers = new CharacterContainerDomainService(TestRequired.TransactionScope());
            containers.Register(Character, bag);
            var log = new OneLog(new CarriedQuest(0, 1776, 1));
            var handler = new Dfo.Server.Application.World.MonsterKillRewardsDomainService(new NoExperienceAward(),
                new Dfo.Server.Application.World.QuestItemDropDomainService(new OneCharacterLog(log), containers,
                    new Dfo.Server.Persistence.Tests.Support.BagRewardDelivery(new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService(), containers)),
                Progress(log),
                new VirtualOnlyPartyService(), clock: TimeProvider.System, gameplay: TestRequired.Absent<Dfo.Server.Abstractions.Achievements.ICharacterGameplayFactDomainService>());

            var result = await TestEffectFootprints.Run(new Dfo.Server.Abstractions.Effects.EffectFootprintOwner(Account, Character, null), cap => MonsterRewardDomainDriver.ApplyAsync(handler, new MonsterRewardFacts(
                    Account, Character, new MonsterRewardIdentity(13098, 5, MonsterRank.Ordinary),
                    144, 0, 1, EnemyType: HuntEnemyTargetTypes.AiCharacter,
                    PendingLoot:
                    [
                        new RolledDrop(10149163, 1, DropRarity.Common, DropItemKind.Stackable,
                            OwnerCharacterId: Character,
                            Delivery: RolledDropDelivery.DirectQuestItem,
                            QuestId: 1776, GrantLimit: 15),
                    ],
                    PartyKey: PartyKey.Virtual(Character)), cap, CancellationToken.None));

            Assert.Equal(14, bag.Count);
            Assert.Null(result.QuestItems);
        });
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
        await TestGameplay.RunAsync(async () =>
        {
            var log = new OneLog(new CarriedQuest(0, 1776, 1));

            var result = await Handler(log).HandleAsync(
                Context(),
                Command(dungeonId, difficulty, enemyCode, enemyType),
                CancellationToken.None);

            Assert.Equal(1, log.Carried.TriggerValue);
            Assert.Empty(result.Advances);
        });
    }

    private static HuntProgressDriver Handler(OneLog log) =>
        new(Progress(log));

    private static IQuestProgressDomainService Progress(
        OneLog log,
        QuestDefinition? definition = null,
        ISeekingQuestProgressDomainService? seeking = null) =>
        new QuestProgressDomainService(
            new OneQuestDefinition(definition ?? Quest1776()),
            new OneCharacterLog(log),
            new UnusedClientReports(),
            seeking ?? new NoSeekingProgress(), mirror: TestRequired.Absent<Dfo.Server.Abstractions.Quests.IMirrorAradQuestDomainService>());

    private sealed class UnusedClientReports : IQuestTriggerDomainService
    {
        public ValueTask<QuestAdvance> ApplyClientReportAsync(
            AccountId accountId, CharacterId characterId, QuestDefinition definition,
            byte selector, bool increment, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static HuntProgressFacts Command(
        int dungeonId,
        DungeonDifficulty difficulty,
        int enemyCode,
        int enemyType) =>
        new(Account, Character, dungeonId, difficulty, enemyCode, enemyType);

    private static CommandExecutionContext Context() => new(
        Guid.Empty,
        typeof(HuntProgressFacts),
        typeof(HuntProgressDriver));

    private static QuestDefinition Quest1776(
        int enemyType = HuntEnemyTargetTypes.Monster) =>
        new(1776, "", null, null, null, null, null, null, [], "hunt enemy", "",
            null, null, null, null, null, null, null, [], [], [], [],
            InitialTrigger: 1,
            KillObjectives: QuestKillObjectiveParser.Parse("hunt enemy", [144, -1, 13099, enemyType, 1]));

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

    private sealed class NoExperienceAward : ExperienceCharacters
    {
        public uint? ThresholdForLevel(byte level) => throw new NotSupportedException();


        public override ValueTask<ExperienceAward?> AddExpAsync(ExperienceCalculationContext source, DateTimeOffset at, Dfo.Server.Data.World.SelectedSessionIdentity? session, Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken cancellationToken) => ValueTask.FromResult<ExperienceAward?>(null);
    }

    private sealed class PricingOnlyExperience(IExperienceCalculationDomainService calculation) : ExperienceCharacters
    {
        public uint? ThresholdForLevel(byte level) => throw new NotSupportedException();


        public override async ValueTask<ExperienceAward?> AddExpAsync(ExperienceCalculationContext source, DateTimeOffset at, Dfo.Server.Data.World.SelectedSessionIdentity? session, Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken cancellationToken)
        {
            _ = await calculation.CalculateAsync(source, 3, cancellationToken);
            return null;
        }
    }

    private sealed class ExperienceMustNotBeAwarded : ExperienceCharacters
    {
        public uint? ThresholdForLevel(byte level) => throw new NotSupportedException();


        public override ValueTask<ExperienceAward?> AddExpAsync(ExperienceCalculationContext source, DateTimeOffset at, Dfo.Server.Data.World.SelectedSessionIdentity? session, Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken cancellationToken) =>
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

    private sealed class DirectGrantBag(int count,
        Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor? transactions = null) : IItemContainer, Dfo.Server.Protocol.Tests.Support.IItemGrantStub
    {
        public bool FailAdd { get; set; }
        public int Count { get; private set; } = count;
        public ContainerId Id => new("direct-quest-test");
        public InventoryContainerType Type => InventoryContainerType.Main;
        public ContainerShape Shape => new(this.Type, 56);
        public bool IsRemovable => false;

        public ValueTask<ItemPlacementOutcome> GrantAsync(
            int itemId, int incoming, CancellationToken cancellationToken)
        {
            Assert.Equal(10149163, itemId);
            if (this.FailAdd) throw new InvalidOperationException("simulated inventory failure");
            transactions?.Current?.Enlist(this, () => new BagChange(this, this.Count));
            this.Count += incoming;
            return ValueTask.FromResult<ItemPlacementOutcome>(
                new ItemPlacementOutcome.AddedToPile(177, incoming, this.Count));
        }

        private ItemReference? Slot => this.Count == 0
            ? null
            : new ItemReference(this, Id, 177, 0, ItemSnapshot.FromCore(StackableItemCore.Create(6, 10149163, this.Count), true));

        public ValueTask<ItemReference?> GetAsync(
            int slotIndex, CancellationToken cancellationToken) =>
            ValueTask.FromResult(slotIndex == 177 ? this.Slot : null);

        public ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ItemReference>>(
                this.Slot is { } item ? [item] : []);


        private sealed class BagChange(DirectGrantBag bag, int before)
            : Dfo.Server.Abstractions.Transactions.ITransactionParticipant
        {
            public bool CommitCanFail => false;
            public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
            public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;
            public ValueTask RollbackAsync(CancellationToken ct)
            {
                bag.Count = before;
                return ValueTask.CompletedTask;
            }
        }

        public ValueTask<ItemReference> PutAsync(int slotIndex, ItemRecord record, CancellationToken ct) =>
            throw new NotSupportedException("This fixture uses injected grant outcomes and only projects their results.");
        public ValueTask<ItemRecord> TakeAsync(ItemReference reference, int? count, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ItemReference> UpdateAsync(ItemReference reference, Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask FlushAsync(CancellationToken ct) => ValueTask.CompletedTask;
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
        public ValueTask<bool> GiveUpAsync(int questId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask TrackAsync(IReadOnlyList<int> questIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

internal sealed record HuntProgressFacts(AccountId AccountId, CharacterId CharacterId, int DungeonId, DungeonDifficulty Difficulty, int EnemyCode, int EnemyType);
internal sealed record HuntProgressResult(IReadOnlyList<QuestAdvanceFact> Advances);
internal sealed class HuntProgressDriver(IQuestProgressDomainService progress)
{
    public async ValueTask<HuntProgressResult> HandleAsync(CommandExecutionContext unused, HuntProgressFacts input, CancellationToken ct)
    {
        var result=await progress.ReportAsync(input.AccountId,input.CharacterId,[new QuestEnemyDefeated(input.DungeonId,input.Difficulty,input.EnemyCode,input.EnemyType)],ct);
        return new(result.Changes.Select(x=>new QuestAdvanceFact(x.QuestId,x.Before,x.After)).ToArray());
    }
}
