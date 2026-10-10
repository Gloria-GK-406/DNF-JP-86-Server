using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.World;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.MonsterDeath;

/// <summary>
/// What answering a reported kill does with the character's standing.
/// </summary>
/// <remarks>
/// This is where a character levels up most often -- a dungeon is settled once and a quest handed
/// in rarely, but a run is a hundred kills -- and until now nothing on this path was checked at
/// all. The experience frame it sends carries the character's remaining skill points beside the
/// total, and those points used to come off the session's entry-time projection: a character who
/// crossed a level mid-run was told the figure they walked in with until they logged out again.
/// </remarks>
public sealed class MonsterDeathHandlerTests
{
    /// <summary>What the character held before the kill.</summary>
    private const uint StartingExperience = 1000;

    /// <summary>Where it stood before the kill.</summary>
    private const byte StartingLevel = 3;

    /// <summary>What the kill was worth, as the grant hands it back.</summary>
    private const uint KillGain = 210;

    /// <summary>The actor the client reports dead, from the 2026-08-29 capture.</summary>
    private const ushort DeadActor = 0x0EE9;

    /// <summary>
    /// The points reported are the ones the kill left behind, not the ones the session holds.
    /// </summary>
    /// <remarks>
    /// <b>The criterion this whole file exists for.</b> A level buys skill points and a kill is
    /// what buys the level, so a figure taken before the award is a level out of date -- and the
    /// figure the session holds is worse than out of date, because nothing has recomputed it since
    /// the character walked in. The ledger answers a different figure each time it is asked and
    /// neither of them is the session's, so a frame carrying anything but the ledger's second
    /// answer here says exactly which mistake was made.
    /// <para>
    /// A kill that crosses nothing asks once, and that half matters too: a level is the only input
    /// to the ledger a kill can move, so asking again would be a second walk through the archive
    /// for an answer that cannot have changed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThePointsReportedAreTheOnesTheKillLeftBehind()
    {
        var crossed = new SkillPointLedger();
        var reported = await Kill(leveledUp: true, ledger: crossed);

        Assert.Equal(2, crossed.TimesAsked);
        var afterTheLevel = reported.OfType<ExperienceNotification>().Single();
        Assert.Equal(SkillPointLedger.AfterTheLevel, afterTheLevel.SkillPoints);
        Assert.NotEqual(SkillPointLedger.StaleEntryMirror, afterTheLevel.SkillPoints);

        var stayed = new SkillPointLedger();
        var unchanged = await Kill(ledger: stayed);

        Assert.Equal(1, stayed.TimesAsked);
        var beforeTheLevel = unchanged.OfType<ExperienceNotification>().Single();
        Assert.Equal(SkillPointLedger.BeforeTheLevel, beforeTheLevel.SkillPoints);
        Assert.NotEqual(SkillPointLedger.StaleEntryMirror, beforeTheLevel.SkillPoints);
    }

    /// <summary>
    /// The frame carries where the character stands after the kill, and comes before the
    /// acknowledgement.
    /// </summary>
    /// <remarks>
    /// The order is the reference server's, and it is not decoration: the popup the experience
    /// frame raises names the kill the acknowledgement confirms. A level crossed here also
    /// refreshes what the character may take, for the reason a settlement does -- a level decides
    /// which quests are offered, and a quest whose band the character has just entered would
    /// otherwise stay hidden until they next entered the world.
    /// </remarks>
    [Fact]
    public async Task TheKillReportsTheStandingItLeftBehind()
    {
        var content = new StubWorldContentEntryRuleService();
        var sent = await Kill(leveledUp: true, content: content);

        Assert.Equal(
            [typeof(ExperienceNotification), typeof(MonsterDeathNotification),
             typeof(AvailableQuestListMessage)],
            sent.Select(frame => frame.GetType()));

        var experience = sent.OfType<ExperienceNotification>().Single();
        Assert.Equal(StartingLevel + 1, experience.Level);
        Assert.Equal(StartingExperience + KillGain, experience.TotalExperience);
        Assert.Equal(StartingLevel + 1, content.LastSubject?.Level);
    }

    /// <summary>
    /// Without a skill-point ledger the kill is acknowledged, and neither paid nor reported.
    /// </summary>
    /// <remarks>
    /// The experience frame carries the character's remaining points as well as the total, so
    /// without them there is no frame to report a new standing on. Awarding regardless would leave
    /// the character paid, the death spent, and the session still holding the old total with no
    /// frame to correct it -- which is why the ledger is asked before anything is consumed.
    /// </remarks>
    [Fact]
    public async Task WithoutASkillLedgerTheKillIsNeitherPaidNorReported()
    {
        var bus = new FixedKillAward(leveledUp: false);
        var sent = await Kill(bus: bus, withLedger: false);

        Assert.Equal(0, bus.TimesAsked);
        var acknowledgement = Assert.IsType<MonsterDeathNotification>(Assert.Single(sent));
        Assert.Equal(DeadActor, acknowledgement.ActorSequence);
        Assert.Empty(acknowledgement.Drops);
    }

    [Fact]
    public async Task MaxLevelKillProjectsTheTemporaryCumulativeValue()
    {
        const uint maximum = 1046561640;
        var award = new ExperienceAward(new(Base: 14), 86, maximum, false)
            { NotificationTotal = maximum + 14, Overflow = 14 };
        var sent = await Kill(bus: new FixedKillAward(false, overrideAward: award));
        var notice = Assert.Single(sent.OfType<ExperienceNotification>());
        Assert.Equal(86, notice.Level);
        Assert.Equal(maximum + 14, notice.TotalExperience);
        Assert.Equal(maximum, award.Total);
    }

    [Fact]
    public async Task ADeathWithNoNewRewardAtCapReportsOnlyTheCap()
    {
        const uint maximum = 1046561640;
        var ledger = new SkillPointLedger { Progress = new(86, maximum) };
        var sent = await Kill(ledger: ledger, bus: new FixedKillAward(false, awarded: false));
        Assert.Equal(maximum, Assert.Single(sent.OfType<ExperienceNotification>()).TotalExperience);
    }

    [Fact]
    public async Task A_kill_without_an_award_reports_current_progress_instead_of_the_entry_copy()
    {
        var ledger = new SkillPointLedger { Progress = new CharacterProgressionState(9, 6000) };
        var sent = await Kill(ledger: ledger, bus: new FixedKillAward(false, awarded: false));
        var experience = Assert.Single(sent.OfType<ExperienceNotification>());
        Assert.Equal(9, experience.Level);
        Assert.Equal(6000u, experience.TotalExperience);
        Assert.Equal([typeof(ExperienceNotification), typeof(MonsterDeathNotification)], sent.Select(frame => frame.GetType()));
    }

    [Fact]
    public async Task A_direct_quest_grant_refreshes_the_receivers_bag_and_quest_after_death()
    {
        var item = new InventoryItemState(
            null, 180, StackableItemCore.Create(6, 10149163, 1));
        var update = new CommittedInventoryUpdateState(
            InventoryContainerType.Main, [item], DateTimeOffset.UnixEpoch);
        var progress = new QuestProgressSnapshot(
            [new CarriedQuest(0, 1779, 14)]);

        var sent = await Kill(bus: new FixedKillAward(
            leveledUp: false, questItems: update, questProgress: progress));

        Assert.Equal(
            [typeof(ExperienceNotification), typeof(MonsterDeathNotification),
                typeof(InventoryDeltaMessage), typeof(AcceptedQuestProgressMessage)],
            sent.Select(frame => frame.GetType()));
        Assert.Equal(180, Assert.Single(sent.OfType<InventoryDeltaMessage>()).Items.Single().SlotIndex);
    }

    [Fact]
    public async Task A_server_counted_kill_pushes_its_quest_progress_without_a_trigger_request()
    {
        var sent = await Kill(bus: new FixedKillAward(
            leveledUp: false,
            questProgress: new QuestProgressSnapshot([new CarriedQuest(0, 1776, 0)])));

        var notice = Assert.Single(sent.OfType<AcceptedQuestProgressMessage>());
        Assert.Equal([(1776, 0u)], notice.Quests
            .Select(quest => ((int)quest.QuestId, quest.TriggerValue)));
    }

    [Fact]
    public async Task AVerifiedFirstDeathProducesOneCanonicalQuestKillFact()
    {
        var bus = new FixedKillAward(leveledUp: false);

        await Kill(bus: bus);

        var fact = Assert.IsType<ApplyMonsterKillCommand>(bus.LastKillCommand);
        Assert.Equal(144, fact.DungeonId);
        Assert.Equal(3001, fact.Monster.Code);
        Assert.Equal(0, (int)fact.Monster.Rank);
        Assert.Equal(1, bus.QuestCommands);
    }

    [Fact]
    public async Task AFailedDurableKillCanBeRetriedWithoutDoubleApplyingIt()
    {
        var run = new OneRun();
        var bus = new FixedKillAward(leveledUp: false, failuresBeforeSuccess: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Kill(bus: bus, run: run));
        var sent = await Kill(bus: bus, run: run);

        Assert.Equal(1, run.Restores);
        Assert.Equal(2, bus.TimesAsked);
        Assert.Single(sent.OfType<MonsterDeathNotification>());
    }

    [Fact]
    public async Task AnAiCharacterDeathProducesOnlyOneServerVerifiedAiQuestFact()
    {
        var run = new OneRun();
        var bus = new FixedKillAward(
            leveledUp: false,
            awarded: false,
            failuresBeforeSuccess: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Kill(
                bus: bus,
                run: run,
                killRules: new OneDeclaredAiCharacter()));
        var sent = await Kill(
            bus: bus,
            run: run,
            killRules: new OneDeclaredAiCharacter());
        await Kill(
            bus: bus,
            run: run,
            killRules: new OneDeclaredAiCharacter());

        Assert.Equal(1, run.Restores);
        Assert.Equal(2, bus.TimesAsked);
        Assert.Equal(HuntEnemyTargetTypes.AiCharacter, bus.LastKillCommand?.EnemyType);
        Assert.Single(sent.OfType<MonsterDeathNotification>());
    }

    [Fact]
    public async Task AnAPCDeathProjectsItsCommittedIndependentDropWithoutMonsterExperience()
    {
        var bus = new FixedKillAward(leveledUp: false, awarded: false);
        var sent = await Kill(bus: bus, run: new OneRun(apcDrop: true),
            killRules: new OneDeclaredAiCharacter());
        var death = Assert.Single(sent.OfType<MonsterDeathNotification>());
        Assert.Equal(1001, Assert.Single(death.Drops).ItemId);
        Assert.Equal(HuntEnemyTargetTypes.AiCharacter, bus.LastKillCommand?.EnemyType);
    }

    [Fact]
    public async Task APartyKillPaysEachFrozenExactParticipantAndBroadcastsOneSharedDeath()
    {
        var leader = SelectedSessionIdentity.Create(
            new AccountId(1), new CharacterId(60), new NetworkConnectionId("leader"), 1, 10010);
        var member = SelectedSessionIdentity.Create(
            new AccountId(2), new CharacterId(61), new NetworkConnectionId("member"), 1, 10011);
        var participants = new[]
        {
            new DungeonRunParticipant(
                leader.AccountId, leader.CharacterId, Slot: 0, IsLeader: true),
            new DungeonRunParticipant(
                member.AccountId, member.CharacterId, Slot: 1, IsLeader: false),
        };
        var run = new PartyRun(participants);
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(run.RunId, [leader, member]);
        var sessions = new PartySessions([leader, member]);
        var runtime = new PartyRuntimeSessions([leader, member]);
        var bus = new PartyKillBus();
        var settlement = new RecordingSettlementRules();
        var sender = new PartyRecordingSender();
        var killRules = new OneClearedBossMonster();
        var handler = new MonsterDeathHandler(
            sessions,
            runtime,
            new PartyCharacters([leader.CharacterId, member.CharacterId]),
            run,
            killRules,
            new DungeonClearDomainService(run, killRules),
            new DungeonClearProjectionDelivery(sessions, run, registry, sender),
            bus,
            new QuestOfferRefreshDomainService(
                new StubWorldContentEntryRuleService(), new RecordingQuestLogs(),
                Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain()),
            registry,
            sender,
            ExperienceDiagnostics.None,
            NullLogger<MonsterDeathHandler>.Instance);

        await handler.HandleAsync(
            ContextOf(leader),
            new MonsterDeathRequest(GameMessageMetadata.Empty, DeadActor, leader.WireUserId),
            CancellationToken.None);
        await handler.HandleAsync(
            ContextOf(member),
            new MonsterDeathRequest(GameMessageMetadata.Empty, DeadActor, member.WireUserId),
            CancellationToken.None);

        Assert.Equal([leader.CharacterId, member.CharacterId],
            bus.Commands.Select(command => command.CharacterId));
        Assert.Equal(new[] { leader, member }, bus.Commands.Select(command => command.Session));
        Assert.All(bus.Commands, command => Assert.Equal(2, command.PartyMemberCount));
        Assert.All(bus.Commands, command => Assert.True(command.IsFinalBossDeath));

        foreach (var session in new[] { leader, member })
        {
            var received = sender.For(session.ConnectionId);
            Assert.Single(received.OfType<ExperienceNotification>());
            var death = received.OfType<MonsterDeathNotification>().First();
            Assert.Single(death.Drops);
            Assert.Equal(leader.WireUserId, death.Owner);
            Assert.Single(received.OfType<EnableClearDungeonNotification>());
            Assert.Single(received.OfType<SecretShopNpcNotification>());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Object_rewards_are_projected_only_to_each_owners_exact_session(bool replaceMember)
    {
        var leader = SelectedSessionIdentity.Create(new(1), new(60), new("leader"), 1, 10010);
        var member = SelectedSessionIdentity.Create(new(2), new(61), new("member"), 1, 10011);
        var sessions = new Dfo.Server.Application.Sessions.SelectedSessionDomainService();
        sessions.Select(leader);
        sessions.Select(member);
        var run = new PartyRun([
            new(leader.AccountId, leader.CharacterId, 0, true),
            new(member.AccountId, member.CharacterId, 1, false)]);
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(run.RunId, [leader, member]);
        var sender = new PartyRecordingSender();
        var kills = new OneDeclaredMonster();
        var replacement = member with { ConnectionId = new("replacement"), ConnectionGeneration = 2 };
        var bus = new ObjectRewardBus(() => { if (replaceMember) sessions.Select(replacement); });
        var handler = new MonsterDeathHandler(sessions, new PartyRuntimeSessions([leader, member]),
            new PartyCharacters([leader.CharacterId, member.CharacterId]), run, kills,
            new DungeonClearDomainService(run, kills),
            new DungeonClearProjectionDelivery(sessions, run, registry, sender), bus,
            new QuestOfferRefreshDomainService(new StubWorldContentEntryRuleService(),
                new RecordingQuestLogs(), CurrentCharacterStub.Domain()), registry, sender,
            ExperienceDiagnostics.None, NullLogger<MonsterDeathHandler>.Instance);
        await handler.HandleAsync(ContextOf(leader),
            new MonsterDeathRequest(GameMessageMetadata.Empty, 0, ushort.MaxValue) { ObjectCode = 8961 }, default);
        Assert.Equal([leader, member], bus.Command!.Recipients);
        foreach (var recipient in replaceMember ? new[] { leader } : new[] { leader, member })
        {
            var frames = sender.For(recipient.ConnectionId);
            Assert.Equal(2, frames.Count);
            var item = Assert.Single(Assert.IsType<InventoryDeltaMessage>(frames[0]).Items);
            Assert.Equal(recipient.CharacterId.Value, item.Core.Value);
            Assert.IsType<AcceptedQuestProgressMessage>(frames[1]);
            Assert.Empty(frames.OfType<ExperienceNotification>());
            Assert.Empty(frames.OfType<MonsterDeathNotification>());
        }
        Assert.Empty(sender.For(replacement.ConnectionId));
        if (replaceMember) Assert.Empty(sender.For(member.ConnectionId));
    }

    private sealed class ObjectRewardBus(Action afterCommit) : ICommandBus
    {
        public ApplyObjectDestructionCommand? Command { get; private set; }
        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct)
            where TResult : notnull
        {
            this.Command = Assert.IsType<ApplyObjectDestructionCommand>(command);
            var rewards = this.Command.Recipients.Select(session => new ObjectQuestReward(session,
                new ApplyMonsterKillResult(null, [], new CommittedInventoryUpdateState(
                    InventoryContainerType.Main,
                    [new InventoryItemState(null, 0,
                        StackableItemCore.Create(6, 10149163, session.CharacterId.Value))], DateTimeOffset.UtcNow),
                    new QuestProgressSnapshot([new CarriedQuest(0, 2201, 8)]), []))).ToArray();
            afterCommit();
            return ValueTask.FromResult((TResult)(object)new ObjectDestructionResult(rewards));
        }
    }

    [Fact]
    public async Task AReplacementConnectionDoesNotInheritAnOlderRunsKillEffects()
    {
        var leader = SelectedSessionIdentity.Create(
            new AccountId(1), new CharacterId(60), new NetworkConnectionId("leader"), 1, 10010);
        var frozenMember = SelectedSessionIdentity.Create(
            new AccountId(2), new CharacterId(61), new NetworkConnectionId("old-member"), 1, 10011);
        var replacement = frozenMember with
        {
            ConnectionId = new NetworkConnectionId("replacement"),
            ConnectionGeneration = 2,
        };
        var run = new PartyRun(
        [
            new DungeonRunParticipant(
                leader.AccountId, leader.CharacterId, 0, IsLeader: true),
            new DungeonRunParticipant(
                frozenMember.AccountId, frozenMember.CharacterId, 1,
                IsLeader: false),
        ]);
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(run.RunId, [leader, frozenMember]);
        var bus = new PartyKillBus();
        var sender = new PartyRecordingSender();
        var sessions = new PartySessions([leader, replacement]);
        var killRules = new OneClearedBossMonster();
        var settlement = new RecordingSettlementRules();
        var handler = new MonsterDeathHandler(
            sessions,
            new PartyRuntimeSessions([leader, replacement]),
            new PartyCharacters([leader.CharacterId, frozenMember.CharacterId]),
            run,
            killRules,
            new DungeonClearDomainService(run, killRules),
            new DungeonClearProjectionDelivery(sessions, run, registry, sender),
            bus,
            new QuestOfferRefreshDomainService(
                new StubWorldContentEntryRuleService(), new RecordingQuestLogs(),
                Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain()),
            registry,
            sender,
            ExperienceDiagnostics.None,
            NullLogger<MonsterDeathHandler>.Instance);

        await handler.HandleAsync(
            ContextOf(leader),
            new MonsterDeathRequest(GameMessageMetadata.Empty, DeadActor, leader.WireUserId),
            CancellationToken.None);

        var applied = Assert.Single(bus.Commands);
        Assert.Equal(leader.CharacterId, applied.CharacterId);
        Assert.Equal(2, applied.PartyMemberCount);
        Assert.Empty(sender.For(frozenMember.ConnectionId));
        Assert.Empty(sender.For(replacement.ConnectionId));
    }

    [Fact]
    public async Task ARemainingMemberCanFinishTheSharedBossAfterTheLeaderLeavesDelivery()
    {
        var leader = SelectedSessionIdentity.Create(
            new AccountId(1), new CharacterId(60), new NetworkConnectionId("leader"), 1, 10010);
        var member = SelectedSessionIdentity.Create(
            new AccountId(2), new CharacterId(61), new NetworkConnectionId("member"), 1, 10011);
        var run = new PartyRun(
        [
            new DungeonRunParticipant(
                leader.AccountId, leader.CharacterId, 0, IsLeader: true),
            new DungeonRunParticipant(
                member.AccountId, member.CharacterId, 1, IsLeader: false),
        ], leader.CharacterId);
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(run.RunId, [leader, member]);
        Assert.True(registry.RemoveParticipant(run.RunId, leader.CharacterId));
        var bus = new PartyKillBus();
        var settlement = new RecordingSettlementRules();
        var sender = new PartyRecordingSender();
        var sessions = new PartySessions([member]);
        var killRules = new OneClearedBossMonster();
        var handler = new MonsterDeathHandler(
            sessions,
            new PartyRuntimeSessions([member]),
            new PartyCharacters([member.CharacterId]),
            run,
            killRules,
            new DungeonClearDomainService(run, killRules),
            new DungeonClearProjectionDelivery(sessions, run, registry, sender),
            bus,
            new QuestOfferRefreshDomainService(
                new StubWorldContentEntryRuleService(), new RecordingQuestLogs(),
                Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain()),
            registry,
            sender,
            ExperienceDiagnostics.None,
            NullLogger<MonsterDeathHandler>.Instance);

        await handler.HandleAsync(
            ContextOf(member),
            new MonsterDeathRequest(GameMessageMetadata.Empty, DeadActor, member.WireUserId),
            CancellationToken.None);

        var applied = Assert.Single(bus.Commands);
        Assert.Equal(member.CharacterId, applied.CharacterId);
        Assert.Equal(2, applied.PartyMemberCount);
        Assert.True(applied.IsFinalBossDeath);
        var memberFrames = sender.For(member.ConnectionId);
        Assert.Single(memberFrames.OfType<EnableClearDungeonNotification>());
        var death = Assert.Single(memberFrames.OfType<MonsterDeathNotification>());
        Assert.Equal(
            leader.WireUserId,
            Assert.IsAssignableFrom<IReadOnlyDictionary<ushort, ushort>>(death.DropOwners)[1]);
    }

    [Fact]
    public void DungeonRecipientsFollowCurrentPartyButNeverAdmitAReplacementConnection()
    {
        var leader = SelectedSessionIdentity.Create(
            new AccountId(1), new CharacterId(60), new NetworkConnectionId("leader"), 1, 10010);
        var member = SelectedSessionIdentity.Create(
            new AccountId(2), new CharacterId(61), new NetworkConnectionId("member"), 1, 10011);
        var townMember = SelectedSessionIdentity.Create(
            new AccountId(3), new CharacterId(62), new NetworkConnectionId("town-member"), 1, 10012);
        var replacement = member with
        {
            ConnectionId = new NetworkConnectionId("replacement"),
            ConnectionGeneration = 2,
        };
        var party = new PartyService(
            new FixedPartyMemberStatus(), new PartyModeRuleService(),
            TimeProvider.System, NoPartyEndpoints.Instance);
        var scope = new PartyDirectoryScope(11, 1, 1);
        var first = new PartyCharacterSnapshot(
            leader.AccountId, leader.CharacterId, leader.WireUserId, scope);
        var second = new PartyCharacterSnapshot(
            member.AccountId, member.CharacterId, member.WireUserId, scope);
        Assert.True(party.RequestInvite(first, second,
            new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
        Assert.True(party.AcceptInvite(second, first).Succeeded);
        var third = new PartyCharacterSnapshot(
            townMember.AccountId, townMember.CharacterId, townMember.WireUserId, scope);
        Assert.True(party.RequestInvite(first, third,
            new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
        Assert.True(party.AcceptInvite(third, first).Succeeded);
        var run = new PartyRun([
            new DungeonRunParticipant(leader.AccountId, leader.CharacterId, 0, true),
            new DungeonRunParticipant(member.AccountId, member.CharacterId, 1, false),
        ]) { PartyKey = party.FindParty(first).Key };
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(run.RunId, [leader, member]);
        var recipients = new DungeonPartyRecipients(
            party, new PartySessions([leader, member, townMember]), run, registry);

        Assert.Equal([leader, member], recipients.Find(leader, run.RunId));
        Assert.Equal([leader], new DungeonPartyRecipients(
            party, new PartySessions([leader, replacement]), run, registry)
            .Find(leader, run.RunId));
        Assert.True(party.LeaveParty(leader.CharacterId).Succeeded);
        Assert.Equal([member], recipients.Find(member, run.RunId));
        Assert.Empty(new DungeonPartyRecipients(
            party, new PartySessions([replacement]), run, registry)
            .Find(member, run.RunId));
    }

    [Fact]
    public async Task AFailureAfterOneParticipantWriteRetriesOnlyTheUnfinishedParticipant()
    {
        var leader = SelectedSessionIdentity.Create(
            new AccountId(1), new CharacterId(60), new NetworkConnectionId("leader"), 1, 10010);
        var member = SelectedSessionIdentity.Create(
            new AccountId(2), new CharacterId(61), new NetworkConnectionId("member"), 1, 10011);
        var run = new PartyRun(
        [
            new DungeonRunParticipant(
                leader.AccountId, leader.CharacterId, 0, IsLeader: true),
            new DungeonRunParticipant(
                member.AccountId, member.CharacterId, 1, IsLeader: false),
        ]);
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(run.RunId, [leader, member]);
        var bus = new PartyKillBus(member.CharacterId, leader.CharacterId);
        var sender = new PartyRecordingSender();
        var runtime = new PartyRuntimeSessions([leader, member]);
        var sessions = new PartySessions([leader, member]);
        var killRules = new OneClearedBossMonster();
        var settlement = new RecordingSettlementRules();
        var handler = new MonsterDeathHandler(
            sessions,
            runtime,
            new PartyCharacters([leader.CharacterId, member.CharacterId]),
            run,
            killRules,
            new DungeonClearDomainService(run, killRules),
            new DungeonClearProjectionDelivery(sessions, run, registry, sender),
            bus,
            new QuestOfferRefreshDomainService(
                new StubWorldContentEntryRuleService(), new RecordingQuestLogs(),
                Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain()),
            registry,
            sender,
            ExperienceDiagnostics.None,
            NullLogger<MonsterDeathHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await handler.HandleAsync(
                ContextOf(leader),
                new MonsterDeathRequest(GameMessageMetadata.Empty, DeadActor, leader.WireUserId),
                CancellationToken.None));
        await handler.HandleAsync(
            ContextOf(member),
            new MonsterDeathRequest(GameMessageMetadata.Empty, DeadActor, member.WireUserId),
            CancellationToken.None);

        Assert.Equal(
            [leader.CharacterId, member.CharacterId, member.CharacterId],
            bus.Commands.Select(command => command.CharacterId));
        Assert.Equal(1, run.CommitCount);
        Assert.Equal(1, run.ReleaseCount);
        Assert.Single(sender.For(leader.ConnectionId).OfType<MonsterDeathNotification>());
        Assert.Single(sender.For(member.ConnectionId).OfType<MonsterDeathNotification>());
        var leaderExperience = Assert.Single(
            sender.For(leader.ConnectionId).OfType<ExperienceNotification>());
        Assert.Equal((byte)(StartingLevel + 1), leaderExperience.Level);
        Assert.Equal(17u, leaderExperience.Lines[ExperienceBonusKind.PartyReward]);
        Assert.Single(sender.For(leader.ConnectionId).OfType<AvailableQuestListMessage>());
        Assert.Equal(StartingExperience + KillGain, leaderExperience.TotalExperience);
    }

    [Fact]
    public async Task ASoloBossClearUsesTheReportingSessionInsteadOfTheLegacyRunPlaceholder()
    {
        var settlement = new RecordingSettlementRules();
        var run = new PartyRun(
        [
            new DungeonRunParticipant(
                new AccountId(0), new CharacterId(60), Slot: 0,
                IsLeader: true),
        ]);

        var sent = await Kill(
            run: run,
            killRules: new OneClearedBossMonster(),
            settlementRules: settlement);

        Assert.Single(sent.OfType<EnableClearDungeonNotification>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enemy_APC_death_only_projects_clear_when_the_actual_APC_boss_dies(bool killBoss)
    {
        var runs = new DungeonRunDomainService();
        var layout = BossLayout();
        var other = checked((ushort)(DeadActor + 1));
        var primary = new DungeonRoomActor(killBoss ? 14003 : 10610, 60,
            killBoss ? (byte)8 : (byte)5) { IsHostile = true };
        var secondary = new DungeonRoomActor(killBoss ? 10610 : 14003, 60,
            killBoss ? (byte)5 : (byte)8) { IsHostile = true };
        runs.PreparePartyEntry([new(new(1), new(60), 0, true)], layout, 0,
            new DungeonCellSeed(layout.StartCell, layout.StartRoomMapId, DeadActor,
            [new(DeadActor, primary, true, false, [], IsMonster: false),
                new(other, secondary, true, false, [], IsMonster: false)], []),
            DungeonRunId.New(), DateTimeOffset.UnixEpoch);
        var sent = await Kill(run: runs, killRules: new MonsterKillDomainService());
        Assert.Equal(killBoss, runs.Find(new CharacterId(60))!.ClearOpen);
        Assert.Equal(killBoss ? 1 : 0, sent.OfType<EnableClearDungeonNotification>().Count());
        Assert.Equal(killBoss ? new[] { DeadActor, other } : new[] { DeadActor },
            sent.OfType<MonsterDeathNotification>().Select(message => message.ActorSequence));
    }

    [Fact]
    public async Task Boss_death_projects_remaining_monsters_as_empty_deaths_before_clear()
    {
        var runs = new DungeonRunDomainService();
        var layout = BossLayout();
        var character = new CharacterId(60);
        var other = checked((ushort)(DeadActor + 1));
        _ = runs.PreparePartyEntry(
            [new DungeonRunParticipant(new AccountId(1), character, 0, IsLeader: true)],
            layout, difficulty: 0,
            new DungeonCellSeed(
                layout.StartCell, layout.StartRoomMapId, DeadActor,
                [
                    new DungeonCellActorState(
                        DeadActor, new DungeonRoomActor(65303, 1, (byte)MonsterRank.Boss),
                        IsAiCharacter: false, IsDead: false, PendingLoot: []),
                    new DungeonCellActorState(
                        other, new DungeonRoomActor(65601, 1, 0),
                        IsAiCharacter: false, IsDead: false, PendingLoot: []),
                ], []),
            DungeonRunId.New(), DateTimeOffset.UnixEpoch);

        var sent = await Kill(run: runs, killRules: new OneClearedBossMonster());

        Assert.Equal([DeadActor, other], sent.OfType<MonsterDeathNotification>()
            .Select(notification => notification.ActorSequence));
        Assert.Empty(sent.OfType<MonsterDeathNotification>().Last().Drops);
        Assert.Single(sent.OfType<EnableClearDungeonNotification>());
    }

    [Fact]
    public async Task Party_boss_death_projects_the_same_remaining_monsters_to_both_players()
    {
        var leader = SelectedSessionIdentity.Create(
            new AccountId(1), new CharacterId(60), new NetworkConnectionId("leader"), 1, 10010);
        var member = SelectedSessionIdentity.Create(
            new AccountId(2), new CharacterId(61), new NetworkConnectionId("member"), 1, 10011);
        var layout = BossLayout();
        var other = checked((ushort)(DeadActor + 1));
        var runs = new DungeonRunDomainService();
        var run = runs.PreparePartyEntry(
            [
                new DungeonRunParticipant(leader.AccountId, leader.CharacterId, 0, IsLeader: true),
                new DungeonRunParticipant(member.AccountId, member.CharacterId, 1, IsLeader: false),
            ],
            layout, difficulty: 0,
            new DungeonCellSeed(layout.StartCell, layout.StartRoomMapId, DeadActor,
            [
                new DungeonCellActorState(
                    DeadActor, new DungeonRoomActor(65303, 1, (byte)MonsterRank.Boss),
                    IsAiCharacter: false, IsDead: false, PendingLoot: []),
                new DungeonCellActorState(
                    other, new DungeonRoomActor(65601, 1, 0),
                    IsAiCharacter: false, IsDead: false, PendingLoot: []),
            ], []),
            DungeonRunId.New(), DateTimeOffset.UnixEpoch).Run;
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(run.RunId, [leader, member]);
        var sessions = new PartySessions([leader, member]);
        var sender = new PartyRecordingSender();
        var killRules = new OneClearedBossMonster();
        var bus = new PartyKillBus();
        var handler = new MonsterDeathHandler(
            sessions,
            new PartyRuntimeSessions([leader, member]),
            new PartyCharacters([leader.CharacterId, member.CharacterId]),
            runs, killRules,
            new DungeonClearDomainService(runs, killRules),
            new DungeonClearProjectionDelivery(sessions, runs, registry, sender),
            bus,
            new QuestOfferRefreshDomainService(
                new StubWorldContentEntryRuleService(), new RecordingQuestLogs(),
                Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain()),
            registry, sender, ExperienceDiagnostics.None,
            NullLogger<MonsterDeathHandler>.Instance);

        await handler.HandleAsync(
            ContextOf(leader),
            new MonsterDeathRequest(GameMessageMetadata.Empty, DeadActor, leader.WireUserId),
            CancellationToken.None);

        Assert.Equal(2, bus.Commands.Count);
        Assert.True(runs.Find(member.CharacterId)!.IsCleared);
        foreach (var session in new[] { leader, member })
        {
            var received = sender.For(session.ConnectionId);
            Assert.Equal([DeadActor, other], received.OfType<MonsterDeathNotification>()
                .Select(notification => notification.ActorSequence));
            Assert.Single(received.OfType<EnableClearDungeonNotification>());
        }
    }

    [Fact]
    public async Task Death_send_failure_cannot_erase_the_committed_shared_clear_fact()
    {
        var session = SelectedSessionIdentity.Create(
            new AccountId(1),
            new CharacterId(60),
            new NetworkConnectionId("one"),
            connectionGeneration: 1,
            listenerPort: 10010);
        var runs = new DungeonRunDomainService();
        var layout = BossLayout();
        var run = runs.PreparePartyEntry(
            [new DungeonRunParticipant(
                session.AccountId, session.CharacterId, 0, IsLeader: true)],
            layout,
            difficulty: 0,
            new DungeonCellSeed(
                layout.StartCell,
                layout.StartRoomMapId,
                FirstActorSequence: DeadActor,
                Actors:
                [
                    new DungeonCellActorState(
                        DeadActor,
                        new DungeonRoomActor(3001, 0, 0),
                        IsAiCharacter: false,
                        IsDead: false,
                        PendingLoot: [],
                        IsMonster: true),
                ],
                GroundItems: []),
            DungeonRunId.New(),
            DateTimeOffset.UnixEpoch).Run;
        var sessions = new OneSession(session);
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(run.RunId, [session]);
        var sender = new ThrowingSender();
        var killRules = new OneClearedBossMonster();
        var handler = new MonsterDeathHandler(
            sessions,
            new OneRuntimeSession(ExperienceFrameEntry.At(
                StartingLevel, StartingExperience)),
            new OnePlayingCharacter(new SkillPointLedger()),
            runs,
            killRules,
            new DungeonClearDomainService(runs, killRules),
            new DungeonClearProjectionDelivery(sessions, runs, registry, sender),
            new FixedKillAward(leveledUp: false),
            new QuestOfferRefreshDomainService(
                new StubWorldContentEntryRuleService(),
                new RecordingQuestLogs(),
                CurrentCharacterStub.Domain()),
            registry,
            sender,
            ExperienceDiagnostics.None,
            NullLogger<MonsterDeathHandler>.Instance);

        await Assert.ThrowsAsync<IOException>(async () =>
            await handler.HandleAsync(
                ContextOf(session),
                new MonsterDeathRequest(
                    GameMessageMetadata.Empty, DeadActor, session.WireUserId),
                default));

        Assert.True(runs.Find(session.CharacterId)!.ClearOpen);
        Assert.Null(runs.TryOpenClear(session.CharacterId, run.RunId, run.RoomVersion));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Solo_payment_lost_participant_or_actor_publication_never_becomes_payable_again(int mode)
    {
        var run = new PartyRun([new(new(1),new(60),0,true)]) { RejectParticipantPublication = mode == 0, RejectActorPublication = mode == 1, ThrowActorPublication = mode == 2 };
        var bus = new FixedKillAward(false);
        var failures = new List<Exception>();
        await Kill(run: run, bus: bus, reports: 2, failures: failures);
        Assert.Equal(1, bus.TimesAsked);
        Assert.Equal(0, run.ReleaseCount);
        Assert.IsType<Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException>(Assert.Single(failures));
    }

    [Fact]
    public async Task Party_publication_failure_fences_paid_recipient_context_and_preserves_previous_member()
    {
        var leader = SelectedSessionIdentity.Create(new(1),new(60),new("leader"),1,10010);
        var member = SelectedSessionIdentity.Create(new(2),new(61),new("member"),1,10011);
        var run = new PartyRun([new(leader.AccountId,leader.CharacterId,0,true),new(member.AccountId,member.CharacterId,1,false)])
            { RejectParticipantCharacter = member.CharacterId };
        var registry = new DungeonRunDeliveryRegistry(); registry.Register(run.RunId,[leader,member]);
        var sessions = new PartySessions([leader,member]); var sender = new PartyRecordingSender();
        var bus = new PartyKillBus(); var killRules = new OneClearedBossMonster();
        var users = new RecipientConnections([leader,member]);
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(services);
        services.AddCommandDispatching();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<ISelectedSessionDomainService>(services,sessions);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService>(services,users);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var safety = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<SelectedSessionSafety>(provider);
        var handler = new MonsterDeathHandler(sessions,new PartyRuntimeSessions([leader,member]),new PartyCharacters([leader.CharacterId,member.CharacterId]),
            run,killRules,new DungeonClearDomainService(run,killRules),new DungeonClearProjectionDelivery(sessions,run,registry,sender),bus,
            new QuestOfferRefreshDomainService(new StubWorldContentEntryRuleService(),new RecordingQuestLogs(),CurrentCharacterStub.Domain()),
            registry,sender,ExperienceDiagnostics.None,NullLogger<MonsterDeathHandler>.Instance,safety:safety);
        await Assert.ThrowsAsync<Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException>(async () =>
            await handler.HandleAsync(new(users.Connections[leader.ConnectionId]),new MonsterDeathRequest(GameMessageMetadata.Empty,DeadActor,leader.WireUserId),default));
        Assert.True(safety.IsBlocked(users.Connections[member.ConnectionId]));
        Assert.False(safety.IsBlocked(users.Connections[leader.ConnectionId]));
        Assert.True(safety.IsBlocked(member)); Assert.False(safety.IsBlocked(leader));
        Assert.Equal(0,run.ReleaseCount);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await handler.HandleAsync(new(users.Connections[leader.ConnectionId]),new MonsterDeathRequest(GameMessageMetadata.Empty,DeadActor,leader.WireUserId),default));
        Assert.Equal(2,bus.Commands.Count);
    }
    private sealed class RecipientConnections : Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService
    {
        private readonly Dictionary<NetworkConnectionId,OnlineUserSnapshot> users;
        public Dictionary<NetworkConnectionId,NetworkConnectionContext> Connections { get; }
        public RecipientConnections(IReadOnlyList<SelectedSessionIdentity> sessions)
        {
            Connections = sessions.ToDictionary(x => x.ConnectionId,x => new NetworkConnectionContext(x.ConnectionId,NetworkEndpointKind.Game,x.ListenerPort));
            users = sessions.ToDictionary(x => x.ConnectionId,x => new OnlineUserSnapshot(OnlineUserId.New(),Connections[x.ConnectionId],x.AccountId,x.CharacterId,OnlineUserState.CharacterActive,99));
        }
        public OnlineUserSnapshot GetRequired(OnlineUserId id) => users.Values.Single(x => x.OnlineUserId == id);
        public bool TryGetByConnection(NetworkConnectionId id,out OnlineUserSnapshot? user) => users.TryGetValue(id,out user);
    }

    private static DungeonLayout BossLayout() => new(
        144,
        0,
        1,
        1,
        new MazeCell(0, 0),
        new MazeCell(0, 0),
        8153,
        7,
        new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(8153)});

    private static NetworkDispatchContext ContextOf(SelectedSessionIdentity session) =>
        new(new NetworkConnectionContext(
            session.ConnectionId, NetworkEndpointKind.Game, session.ListenerPort));

    private static async Task<IReadOnlyList<IGameServerMessage>> Kill(
        bool leveledUp = false,
        SkillPointLedger? ledger = null,
        FixedKillAward? bus = null,
        bool withLedger = true,
        StubWorldContentEntryRuleService? content = null,
        IDungeonRunDomainService? run = null,
        IMonsterKillDomainService? killRules = null,
        IDungeonSettlementRuleService? settlementRules = null, int reports = 1, List<Exception>? failures = null)
    {
        var session = SelectedSessionIdentity.Create(
            new AccountId(1),
            new CharacterId(60),
            new NetworkConnectionId("one"),
            connectionGeneration: 1,
            listenerPort: 10010);
        var sender = new RecordingSender();
        var effectiveRun = run ?? new OneRun();
        var effectiveKills = killRules ?? new OneDeclaredMonster();
        var effectiveSettlement = settlementRules ?? new AlwaysCompleteSettlementRules();
        var sessions = new OneSession(session);
        var registry = new DungeonRunDeliveryRegistry();
        if (effectiveRun.Find(session.CharacterId) is { } active && active.RunId != default)
            registry.Register(active.RunId, [session]);

        var handler = new MonsterDeathHandler(
            sessions,
            new OneRuntimeSession(ExperienceFrameEntry.At(StartingLevel, StartingExperience)),
            new OnePlayingCharacter(ledger ?? new SkillPointLedger(withLedger)),
            effectiveRun,
            effectiveKills,
            new DungeonClearDomainService(effectiveRun, effectiveKills),
            new DungeonClearProjectionDelivery(sessions, effectiveRun, registry, sender),
            bus ?? new FixedKillAward(leveledUp),
            new QuestOfferRefreshDomainService(
                content ?? new StubWorldContentEntryRuleService(), new RecordingQuestLogs(), Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain()),
            registry,
            sender,
            ExperienceDiagnostics.None,
            NullLogger<MonsterDeathHandler>.Instance);

        for (var report = 0; report < reports; report++)
            try
            {
                await handler.HandleAsync(new NetworkDispatchContext(new NetworkConnectionContext(
                    new NetworkConnectionId("one"), NetworkEndpointKind.Game, 10010)),
                    new MonsterDeathRequest(GameMessageMetadata.Empty, DeadActor, WireUserId: 1), CancellationToken.None);
            }
            catch (Exception failure) when (failures is not null && report + 1 < reports) { failures.Add(failure); }

        return sender.Sent;
    }

    /// <summary>A bus that answers the kill command with a fixed award.</summary>
    /// <remarks>
    /// Fixed rather than computed, because what a kill is worth is checked against the archive in
    /// <see cref="GrantKillExperienceCommandTests"/>. What is checked here is what the handler
    /// does with the answer.
    /// </remarks>
    private sealed class FixedKillAward(
        bool leveledUp,
        bool awarded = true,
        int failuresBeforeSuccess = 0,
        CommittedInventoryUpdateState? questItems = null,
        QuestProgressSnapshot? questProgress = null, ExperienceAward? overrideAward = null) : ICommandBus
    {
        private int _remainingFailures = failuresBeforeSuccess;

        /// <summary>How many times the kill was priced, which a repeated death would raise.</summary>
        public int TimesAsked { get; private set; }

        public int QuestCommands { get; private set; }

        public ApplyMonsterKillCommand? LastKillCommand { get; private set; }

        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            var kill = Assert.IsType<ApplyMonsterKillCommand>(command);
            Assert.Equal(SelectedSessionIdentity.Create(new(1), new(60), new("one"), 1, 10010), kill.Session);
            this.QuestCommands++;
            this.LastKillCommand = kill;
            this.TimesAsked++;
            if (this._remainingFailures-- > 0)
                throw new InvalidOperationException("injected durable kill failure");
            var result = new ApplyMonsterKillResult(
                awarded ? overrideAward ?? new ExperienceAward(
                    Breakdown: new ExperienceBreakdown(Base: KillGain),
                    Level: leveledUp ? (byte)(StartingLevel + 1) : StartingLevel,
                    Total: StartingExperience + KillGain,
                    LeveledUp: leveledUp) : null,
                [], questItems, questProgress);
            return ValueTask.FromResult((TResult)(object)result);
        }
    }

    private sealed class AlwaysCompleteSettlementRules : IDungeonSettlementRuleService
    {
        public ValueTask<DungeonSettlementRules> ForAsync(
            AccountId accountId,
            CharacterId characterId,
            DungeonRunState run,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DungeonSettlementRules(true, run.Layout.Purpose.IsQuestLinked));
    }

    /// <summary>A room that declares exactly the monster the client says it killed.</summary>
    private sealed class OneDeclaredMonster : IMonsterKillDomainService
    {
        public ValueTask<KillIdentification> IdentifyAsync(
            DungeonRunState run, ushort actorSequence, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                new KillIdentification(
                    new IdentifiedKill(Code: 3001, Level: 4, MonsterRank.Ordinary, IsMonster: true),
                    DeclaredCount: 1));

        /// <summary>Not reached: the kill in these cases is not in the boss room.</summary>
        public ValueTask<bool> IsCurrentRoomClearedAsync(
            DungeonRunState run, CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "These kills happen in the start room, which no exit hangs off.");
    }

    private sealed class OneDeclaredAiCharacter : IMonsterKillDomainService
    {
        public ValueTask<KillIdentification> IdentifyAsync(
            DungeonRunState run, ushort actorSequence, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                new KillIdentification(
                    new IdentifiedKill(
                        Code: 3001,
                        Level: 4,
                        Rank: MonsterRank.Ordinary,
                        IsMonster: false),
                    DeclaredCount: 1));

        public ValueTask<bool> IsCurrentRoomClearedAsync(
            DungeonRunState run, CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "These deaths happen in the start room, which no exit hangs off.");
    }

    /// <summary>
    /// A run that takes one report of one actor's death, exactly as the real service takes it.
    /// </summary>
    /// <remarks>
    /// The boss cell is deliberately not the one the character is standing in. Clearing the boss
    /// room opens the exit, which is a different decision with frames of its own; what is checked
    /// here is the kill itself.
    /// </remarks>
    private sealed class OneRun(bool apcDrop = false) : IDungeonRunDomainService
    {
        private bool _reported;
        private bool _committed;
        private long _claimToken;

        public int Restores { get; private set; }

        private static readonly DungeonLayout Layout = new(
            DungeonId: 144,
            MazeIndex: 0,
            Width: 2,
            Height: 1,
            StartCell: new MazeCell(0, 0),
            BossCell: new MazeCell(1, 0),
            StartRoomMapId: 1,
            Seed: 0,
            Cells: new Dictionary<MazeCell, DungeonCellDefinition> {
                [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(1),
                [new MazeCell(1, 0)] = DungeonCellDefinition.Fixed(2),
            },
            BasisLevel: 5,
            ExperienceWeight: 1.45);

        public DungeonRunState? TryReportActorDead(CharacterId characterId, ushort actorSequence)
        {
            if (this._reported)
                return null;

            this._reported = true;
            return this.Find(characterId);
        }

        public DungeonRunState? Find(CharacterId characterId) => new(
            Layout,
            new MazeCell(0, 0),
            CurrentRoomMapId: 1,
            VisitedCells: new HashSet<MazeCell>(),
            StartedAt: DateTimeOffset.UnixEpoch,
            CurrentCellState: new DungeonCellState(
                new MazeCell(0, 0),
                MapId: 1,
                Revision: this._committed ? 1 : 0,
                FirstActorSequence: DeadActor,
                Actors:
                [
                    new DungeonCellActorState(
                        DeadActor,
                        new DungeonRoomActor(3001, 4, apcDrop ? (byte)5 : (byte)0),
                        IsAiCharacter: apcDrop,
                        IsDead: this._committed,
                        PendingLoot: [], IsMonster: !apcDrop),
                ],
                GroundItems: apcDrop && this._committed
                    ? [new DungeonGroundItem(1, 1001, 1, 0, false, DungeonGroundItemSource.FromActor(DeadActor))]
                    : []));

        public DungeonActorDeathClaim? TryClaimActorDeath(
            CharacterId characterId,
            ushort actorSequence)
        {
            var run = this.TryReportActorDead(characterId, actorSequence);
            return run is null
                ? null
                : new DungeonActorDeathClaim(
                    characterId,
                    run,
                    new RoomActorId(run.CurrentCell, actorSequence),
                    ++this._claimToken);
        }

        public DungeonRunState? CommitActorDeath(DungeonActorDeathClaim claim)
        {
            this._committed = true;
            return this.Find(claim.CharacterId);
        }

        public bool ReleaseActorDeath(DungeonActorDeathClaim claim)
        {
            if (!this._reported || claim.Token != this._claimToken)
                return false;
            this._reported = false;
            this.Restores++;
            return true;
        }

        public DungeonRunState? TrySettle(CharacterId characterId) =>
            throw new NotSupportedException("This fixture only answers.");

        public DungeonRunState Begin(
            CharacterId characterId, DungeonLayout layout, DungeonDifficulty difficulty) =>
            throw new NotSupportedException("This fixture only answers.");

        public IReadOnlyList<MonsterDrop> AllocateMonsterDrops(
            CharacterId characterId,
            int declaredDropCount,
            ushort actorSequence,
            IReadOnlyList<RolledDrop> rolled) => [];

        public bool End(CharacterId characterId) =>
            throw new NotSupportedException("This fixture only answers.");

        public DungeonRunState? TryMove(CharacterId characterId, MazeCell target) =>
            throw new NotSupportedException("This fixture only answers.");

        public DungeonRunState? TryTakeGroundDrop(
            CharacterId characterId, ushort groundDropNumber) =>
            throw new NotSupportedException("This fixture only answers.");

        public DungeonRunState? FixClearRewardCards(
            CharacterId characterId,
            ClearRewardCardFace free,
            ClearRewardCardFace paid,
            int? paidFlipCost) =>
            throw new NotSupportedException("This fixture only answers.");

        public ClearRewardCardSlot? TryClaimClearRewardCard(
            CharacterId characterId, ClearRewardCardKind kind, int index) =>
            throw new NotSupportedException("This fixture only answers.");
    }

    private sealed class FixedPartyMemberStatus : IPartyMemberStatusDomainService
    {
        public ValueTask<PartyInviteDisplayState> InvitationOfAsync(
            PartyCharacterSnapshot character, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PartyInviteDisplayState(0, 0, 0, 0));

        public byte CurrentHpPercentOf(CharacterId characterId) => 100;
    }

    private sealed class NoPartyEndpoints : IPartyMemberSessionEndpointSource
    {
        public static NoPartyEndpoints Instance { get; } = new();

        public SessionEndpoint? FindThroughCurrentSession(CharacterId characterId) => null;
    }

    private sealed class PartyRun(
        IReadOnlyList<DungeonRunParticipant> participants,
        CharacterId? dropOwner = null)
        : IDungeonRunDomainService
    {
        public bool RejectParticipantPublication { get; init; }
        public CharacterId? RejectParticipantCharacter { get; init; }
        public bool RejectActorPublication { get; init; }
        public bool ThrowActorPublication { get; init; }
        private bool claimActive;
        private bool committed;
        private CharacterId? pendingEffect;
        private readonly HashSet<CharacterId> completedEffects = [];
        private readonly DungeonLayout layout = new(
            DungeonId: 144,
            MazeIndex: 0,
            Width: 1,
            Height: 1,
            StartCell: new MazeCell(0, 0),
            BossCell: new MazeCell(0, 0),
            StartRoomMapId: 1,
            Seed: 0,
            Cells: new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(1)},
            BasisLevel: 5,
            ExperienceWeight: 1.45);

        public DungeonRunId RunId { get; } = DungeonRunId.New();
        public PartyKey PartyKey { get; init; }
        public int CommitCount { get; private set; }
        public int ReleaseCount { get; private set; }

        public DungeonRunState? Find(DungeonRunId runId) =>
            runId == this.RunId ? this.Find(participants[0].CharacterId) : null;

        public DungeonRunState? Find(CharacterId characterId)
        {
            var participant = participants.SingleOrDefault(value => value.CharacterId == characterId);
            return participant is null ? null : new DungeonRunState(
                this.layout,
                new MazeCell(0, 0),
                CurrentRoomMapId: 1,
                VisitedCells: new HashSet<MazeCell>(),
                StartedAt: DateTimeOffset.UnixEpoch,
                RunId: this.RunId,
                Participant: participant,
                Participants: participants,
                CurrentCellState: new DungeonCellState(
                    new MazeCell(0, 0),
                    MapId: 1,
                    Revision: this.committed ? 1 : 0,
                    FirstActorSequence: DeadActor,
                    Actors:
                    [
                        new DungeonCellActorState(
                            DeadActor,
                            new DungeonRoomActor(3001, 4, 0),
                            IsAiCharacter: false,
                            IsDead: this.committed,
                            PendingLoot: []),
                    ],
                    GroundItems: this.committed
                        ?
                        [
                            new DungeonGroundItem(
                                Number: 1,
                                ItemId: 0,
                                Count: 44,
                                Durability: 0,
                                IsEquipment: false,
                                DungeonGroundItemSource.FromActor(DeadActor),
                                Kind: DropItemKind.Gold,
                                OwnerCharacterId: dropOwner),
                        ]
                        : [])) { PartyKey = this.PartyKey };
        }

        public DungeonActorDeathClaim? TryClaimActorDeath(
            CharacterId characterId, ushort actorSequence)
        {
            if (this.claimActive || this.committed || this.Find(characterId) is not { } run)
                return null;
            this.claimActive = true;
            return new DungeonActorDeathClaim(
                characterId,
                run,
                new RoomActorId(run.CurrentCell, actorSequence),
                Token: 1,
                this.RunId);
        }

        public DungeonRunState? CommitActorDeath(DungeonActorDeathClaim claim)
        {
            if (ThrowActorPublication) throw new OperationCanceledException("actor publication");
            if (RejectActorPublication) return null;
            this.claimActive = false;
            this.committed = true;
            this.CommitCount++;
            return this.Find(claim.CharacterId);
        }

        public bool ReleaseActorDeath(DungeonActorDeathClaim claim)
        {
            this.ReleaseCount++;
            this.claimActive = false;
            return true;
        }

        public DungeonParticipantKillEffectClaim? TryClaimParticipantKillEffect(
            DungeonActorDeathClaim death,
            CharacterId characterId)
        {
            if (!this.claimActive
                || this.pendingEffect is not null
                || this.completedEffects.Contains(characterId))
            {
                return null;
            }

            this.pendingEffect = characterId;
            return new DungeonParticipantKillEffectClaim(death, characterId, 1);
        }

        public bool CommitParticipantKillEffect(DungeonParticipantKillEffectClaim claim)
        {
            if (this.pendingEffect != claim.CharacterId)
                return false;
            this.pendingEffect = null;
            if (RejectParticipantPublication || RejectParticipantCharacter == claim.CharacterId) return false; // Production may remove the claim before refusing publication.
            return this.completedEffects.Add(claim.CharacterId);
        }

        public bool ReleaseParticipantKillEffect(DungeonParticipantKillEffectClaim claim)
        {
            if (this.pendingEffect != claim.CharacterId)
                return false;
            this.pendingEffect = null;
            return true;
        }

        public DungeonRunState Begin(CharacterId characterId, DungeonLayout layout, DungeonDifficulty difficulty) =>
            throw Unused();
        public DungeonRunState? TryMove(CharacterId characterId, MazeCell target) => throw Unused();
        public DungeonRunState? TryTakeGroundDrop(CharacterId characterId, ushort number) => throw Unused();
        public DungeonRunState? TryReportActorDead(CharacterId characterId, ushort actorSequence) =>
            throw Unused();
        public DungeonRunState? TrySettle(CharacterId characterId) => throw Unused();
        public IReadOnlyList<MonsterDrop> AllocateMonsterDrops(
            CharacterId characterId, int declaredDropCount, ushort actorSequence,
            IReadOnlyList<RolledDrop> rolled) => throw Unused();
        public DungeonRunState? FixClearRewardCards(
            CharacterId characterId, ClearRewardCardFace free, ClearRewardCardFace paid,
            int? paidFlipCost) => throw Unused();
        public ClearRewardCardSlot? TryClaimClearRewardCard(
            CharacterId characterId, ClearRewardCardKind kind, int index) => throw Unused();
        public bool End(CharacterId characterId) => throw Unused();

        private static NotSupportedException Unused() =>
            new("This fixture only models shared monster death.");
    }

    private sealed class PartySessions(IReadOnlyList<SelectedSessionIdentity> sessions)
        : ISelectedSessionDomainService
    {
        public SelectedSessionIdentity? Find(AccountId accountId) =>
            sessions.SingleOrDefault(value => value.AccountId == accountId);
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) =>
            sessions.SingleOrDefault(value => value.CharacterId == characterId);
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) =>
            sessions.SingleOrDefault(value => value.WireUserId == wireUserId);
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) =>
            sessions.SingleOrDefault(value => value.ConnectionId == connectionId);
        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => throw Unused();
        public bool ReleaseExact(SelectedSessionIdentity session) => throw Unused();
        private static NotSupportedException Unused() => new("This fixture only answers.");
    }

    private sealed class PartyRuntimeSessions : ILegacySessionSnapshotFixture
    {
        private readonly Dictionary<SelectedSessionIdentity, SelectCharacterResult> entries;

        public PartyRuntimeSessions(IReadOnlyList<SelectedSessionIdentity> sessions) =>
            this.entries = sessions.ToDictionary(
                session => session,
                _ => ExperienceFrameEntry.At(StartingLevel, StartingExperience));

        public bool TryGet(
            SelectedSessionIdentity session,
            out SelectCharacterResult? result,
            out bool shouldPersistPosition)
        {
            var found = this.entries.TryGetValue(session, out var entry);
            result = entry;
            shouldPersistPosition = false;
            return found;
        }

        public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult result) =>
            this.entries[session] = result;
        public SelectCharacterResult GetRequired(SelectedSessionIdentity session) =>
            this.entries[session];
        public void Publish(
            SelectedSessionIdentity session, SelectCharacterResult result,
            bool shouldPersistPosition) => throw Unused();
        public bool RemoveExact(SelectedSessionIdentity session) => throw Unused();
        private static NotSupportedException Unused() => new("This fixture only answers.");
    }

    private sealed class PartyCharacters(IReadOnlyList<CharacterId> characters)
        : ICharacterDomainService
    {
        private readonly Dictionary<CharacterId, SkillPointLedger> ledgers =
            characters.ToDictionary(id => id, _ => new SkillPointLedger());

        public ICharacter Get(CharacterId characterId) => this.ledgers[characterId];
        public ValueTask<AccountCharacterRoster> GetAccountRosterAsync(
            AccountId accountId, CancellationToken cancellationToken) => throw Unused();
        public ValueTask<AccountCharacterRoster> GetAccountRosterWithAppearanceAsync(
            AccountId accountId, CancellationToken cancellationToken) => throw Unused();
        public ValueTask<ActiveCharacterState> LoadForSelectionAsync(
            AccountId accountId, ushort slotIndex, CancellationToken cancellationToken) => throw Unused();
        public ValueTask<ActiveCharacterState> LoadByRosterPositionAsync(
            AccountId accountId, ushort rosterIndex, CancellationToken cancellationToken) => throw Unused();
        public ValueTask<CharacterNameStatus> CheckNameAsync(
            byte[] rawName, CancellationToken cancellationToken) => throw Unused();
        public ValueTask<CharacterCreationResult> CreateCharacterAsync(
            AccountId accountId, byte job, byte[] rawName,
            CancellationToken cancellationToken) => throw Unused();
        public ValueTask<CharacterDeletionResult> DeleteCharacterAsync(
            AccountId accountId, ushort slotIndex, byte[] rawName,
            CancellationToken cancellationToken) => throw Unused();
        private static NotSupportedException Unused() => new("This fixture only answers.");
    }

    private sealed class PartyKillBus(
        CharacterId? failFor = null,
        CharacterId? levelFor = null) : ICommandBus
    {
        private bool failed;
        public List<ApplyMonsterKillCommand> Commands { get; } = [];

        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            var kill = Assert.IsType<ApplyMonsterKillCommand>(command);
            this.Commands.Add(kill);
            if (!this.failed && kill.CharacterId == failFor)
            {
                this.failed = true;
                throw new InvalidOperationException("injected later participant failure");
            }
            var award = new ExperienceAward(
                Breakdown: new ExperienceBreakdown(
                    Base: kill.CharacterId == levelFor ? KillGain - 17 : KillGain,
                    Party: kill.CharacterId == levelFor ? 17u : 0u),
                Level: kill.CharacterId == levelFor
                    ? (byte)(StartingLevel + 1)
                    : StartingLevel,
                Total: StartingExperience + KillGain,
                LeveledUp: kill.CharacterId == levelFor);
            return ValueTask.FromResult((TResult)(object)new ApplyMonsterKillResult(award, []));
        }
    }

    private sealed class OneClearedBossMonster : IMonsterKillDomainService
    {
        public ValueTask<KillIdentification> IdentifyAsync(
            DungeonRunState run, ushort actorSequence, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new KillIdentification(
                new IdentifiedKill(3001, 4, MonsterRank.Boss, IsMonster: true), 1));

        public ValueTask<bool> IsCurrentRoomClearedAsync(
            DungeonRunState run, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }

    private sealed class RecordingSettlementRules : IDungeonSettlementRuleService
    {
        public AccountId? AccountId { get; private set; }
        public CharacterId? CharacterId { get; private set; }

        public ValueTask<DungeonSettlementRules> ForAsync(
            AccountId accountId,
            CharacterId characterId,
            DungeonRunState run,
            CancellationToken cancellationToken)
        {
            this.AccountId = accountId;
            this.CharacterId = characterId;
            return ValueTask.FromResult(new DungeonSettlementRules(true, QuestMaze: true));
        }
    }

    private sealed class PartyRecordingSender : IGameMessageSender
    {
        private readonly Dictionary<NetworkConnectionId, List<IGameServerMessage>> sent = [];

        public IReadOnlyList<IGameServerMessage> For(NetworkConnectionId connectionId) =>
            this.sent.TryGetValue(connectionId, out var frames) ? frames : [];

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId, IGameCmdMessage message,
            CancellationToken cancellationToken) => throw Unused();
        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId, IGameNotiMessage message,
            CancellationToken cancellationToken) => throw Unused();

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            this.Add(requiredDirectConnectionId, delivery.RequiredDirectMessages);
            foreach (var target in delivery.BestEffortTargets)
                this.Add(target.ConnectionId, target.OrderedMessages);
            return ValueTask.CompletedTask;
        }

        private void Add(
            NetworkConnectionId connectionId, IReadOnlyList<IGameServerMessage> frames)
        {
            if (!this.sent.TryGetValue(connectionId, out var received))
                this.sent[connectionId] = received = [];
            received.AddRange(frames);
        }

        private static NotSupportedException Unused() =>
            new("This fixture records only explicit deliveries.");
    }

    private sealed class ThrowingSender : IGameMessageSender
    {
        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId,
            IGameCmdMessage message,
            CancellationToken cancellationToken) => throw new IOException("send failed");
        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId,
            IGameNotiMessage message,
            CancellationToken cancellationToken) => throw new IOException("send failed");
        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken) => throw new IOException("send failed");
    }
}
