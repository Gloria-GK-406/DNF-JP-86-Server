using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.QuestCompletion;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.QuestCompletion;

/// <summary>
/// What answering a quest handed in does with the character's standing.
/// </summary>
/// <remarks>
/// Three frames, in the reference server's order: the acknowledgement, the experience frame
/// carrying the new total, and the acceptable-quest list without the quest just finished. What is
/// checked here is the middle one, which nothing checked before: it carries the character's
/// remaining skill points beside the total, and handing a quest in can cross a level -- so a
/// figure read off the session's entry-time projection tells a player who just levelled that their
/// new level bought them nothing.
/// </remarks>
public sealed class FinishQuestHandlerTests
{
    /// <summary>The quest handed in, and where the character stands either side of it.</summary>
    private const ushort QuestId = 101;

    private const byte StartingLevel = 1;
    private const uint StartingExperience = 900;
    private const byte FinishedLevel = 2;
    private const uint FinishedTotal = 1147;

    /// <summary>
    /// The points reported are the ones the completion left behind, not the ones the session holds.
    /// </summary>
    /// <remarks>
    /// <b>The criterion this file exists for.</b> Two things have to be true and neither is
    /// visible from the frame alone: the figure has to come from the character rather than from
    /// the session, and it has to be asked for after the completion has been written. The ledger
    /// counts how many times it was asked, so a slice that read the session asks nobody; the bus
    /// records what that count stood at when the completion ran, so a slice that asked first
    /// records one rather than none.
    /// <para>
    /// Once, and only once. Nothing else a completion does can move the ledger, so a second ask
    /// would be a second walk through the archive for an answer that cannot have changed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThePointsReportedAreTheOnesTheCompletionLeftBehind()
    {
        var ledger = new SkillPointLedger();
        var bus = new FixedCompletion(ledger);

        var sent = await Finish(ledger, bus);

        Assert.Equal(1, ledger.TimesAsked);
        Assert.Equal(0, bus.LedgerAsksWhenTheCompletionRan);

        var experience = sent.OfType<ExperienceNotification>().Single();
        Assert.Equal(SkillPointLedger.BeforeTheLevel, experience.SkillPoints);
        Assert.NotEqual(SkillPointLedger.StaleEntryMirror, experience.SkillPoints);
    }

    /// <summary>The three frames the reference server sends, carrying the new standing.</summary>
    /// <remarks>
    /// The order is the one confirmed against its 2026-08-27 capture. The list changes here where
    /// accepting a quest leaves it alone, and that is not a contradiction: the list is computed
    /// from cleared quests among other things, and finishing one is the only thing on this path
    /// that changes them.
    /// </remarks>
    [Fact]
    public async Task TheCompletionReportsTheStandingItLeftBehind()
    {
        var ledger = new SkillPointLedger();
        var content = new StubWorldContentEntryRuleService();

        var sent = await Finish(ledger, new FixedCompletion(ledger), content);

        Assert.Equal(
            [typeof(FinishQuestAcknowledgement), typeof(ExperienceNotification),
             typeof(AvailableQuestListMessage)],
            sent.Select(frame => frame.GetType()));

        var experience = sent.OfType<ExperienceNotification>().Single();
        Assert.Equal(FinishedLevel, experience.Level);
        Assert.Equal(FinishedTotal, experience.TotalExperience);
        Assert.Equal(FinishedLevel, content.LastSubject?.Level);
    }

    /// <summary>
    /// A completion the character has no ledger for stands, and sends no experience frame.
    /// </summary>
    /// <remarks>
    /// The frame carries the remaining points as well as the total, and inventing a figure would
    /// tell the client it had points it does not. The completion itself still stands -- the quest
    /// is handed in and the list refreshed -- because the write has already happened by the time
    /// the ledger is asked.
    /// <para>
    /// This is the other end of the same seam: a slice reading the session would find a figure
    /// there whatever the character can answer, and would send a frame that has no business
    /// existing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ACompletionWithNoLedgerSendsNoExperienceFrame()
    {
        var ledger = new SkillPointLedger(present: false);

        var sent = await Finish(ledger, new FixedCompletion(ledger));

        Assert.Equal(
            [typeof(FinishQuestAcknowledgement), typeof(AvailableQuestListMessage)],
            sent.Select(frame => frame.GetType()));
    }

    [Fact]
    public async Task TheWireSelectionAndCompletionCountEnterTheCommand()
    {
        var ledger = new SkillPointLedger();
        var bus = new FixedCompletion(ledger);

        _ = await Finish(ledger, bus, rewardSelection: 7, completionCount: 3);

        Assert.Equal((ushort)7, bus.LastCommand?.RewardSelection);
        Assert.Equal((ushort)3, bus.LastCommand?.CompletionCount);
    }

    [Fact]
    public async Task ASettlementExceptionBecomesOnlyTheReferenceRefusal()
    {
        var sent = await Finish(new SkillPointLedger(), new RejectedCompletion());

        var acknowledgement = Assert.IsType<FinishQuestAcknowledgement>(Assert.Single(sent));
        Assert.False(acknowledgement.Finished);
        Assert.Equal((byte)QuestFinishRefusal.Unusable, acknowledgement.RefusalCode);
    }

    [Fact]
    public async Task A_stale_seeking_objective_refusal_sends_the_corrected_progress()
    {
        var sent = await Finish(new SkillPointLedger(), new RefusedWithProgress());

        Assert.Equal([typeof(FinishQuestAcknowledgement), typeof(AcceptedQuestProgressMessage)],
            sent.Select(message => message.GetType()));
        Assert.False(Assert.IsType<FinishQuestAcknowledgement>(sent[0]).Finished);
        var progress = Assert.IsType<AcceptedQuestProgressMessage>(sent[1]);
        Assert.Equal(1u, Assert.Single(progress.Quests).TriggerValue);
    }

    [Theory]
    [InlineData((byte)4, null)]
    [InlineData((byte)17, (byte)1)]
    [InlineData((byte)33, (byte)2)]
    public async Task Advancement_sends_new_skills_before_ack_and_projects_the_new_identity_afterwards(byte grow, byte? awakening)
    {
        var ledger = new SkillPointLedger();
        var before = ExperienceFrameEntry.At(StartingLevel, StartingExperience);
        var after = before with
        {
            Character = before.Character with { Identity = before.Character.Identity with { GrowType = grow } },
            CharacterEntry = before.CharacterEntry with
            {
                Features = new([], null!, null!, null!, []),
                Skills = before.CharacterEntry.Skills with
                {
                    Pages = [new(0, [new(5, 100, 1, [])]), new(1, [new(5, 100, 1, [])])],
                },
            },
        };
        var sent = await Finish(ledger, new FixedCompletion(ledger, grow, awakening), views: new ChangedView(before, after));
        Assert.IsType<SkillStateMessage>(sent[0]);
        Assert.Equal(100u, Assert.Single(Assert.IsType<SkillStateMessage>(sent[0]).Skills.Pages[0].Skills).SkillId);
        Assert.Equal((byte?)grow, Assert.IsType<FinishQuestAcknowledgement>(sent[1]).GrowType);
        Assert.Equal(awakening, Assert.IsType<FinishQuestAcknowledgement>(sent[1]).Awakening);
        Assert.Equal(after.CharacterEntry.Skills.Pages, Assert.IsType<FinishQuestAcknowledgement>(sent[1]).SkillPages);
        Assert.Equal(grow, Assert.Single(sent.OfType<CurrentUserStateMessage>()).Character.Identity.GrowType);
        Assert.Equal(grow, Assert.Single(sent.OfType<ExtendedUserStateMessage>()).Character.Identity.GrowType);
        Assert.Single(sent.OfType<InventorySnapshotMessage>());
        Assert.Equal(28, Assert.IsType<WeaknessProjectionNotification>(sent[^1]).Value);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, true)]
    public async Task Career_publication_only_reaches_current_visible_or_admitted_generations(bool dungeon, bool replaced, bool privateRoom, bool sourceReplaced)
    {
        var actor = SelectedSessionIdentity.Create(new(1), new(60), new("one"), 1, 10010);
        var peer = SelectedSessionIdentity.Create(new(2), new(61), new("peer-old"), 1, 10010);
        var selectedPeer = replaced ? peer with { ConnectionGeneration = 2, ConnectionId = new("peer-new") } : peer;
        var identities = new List<SelectedSessionIdentity> { actor, selectedPeer };
        var sessions = new PeerSessions(identities);
        ITownAreaDomainService areas = new Dfo.Server.Application.Sessions.SessionPresenceDomainService();
        var runs = new Dfo.Server.Application.World.DungeonRunDomainService();
        var generations = new DungeonRunDeliveryRegistry();
        if (dungeon)
        {
            var cell = new MazeCell(0, 0);
            var layout = new DungeonLayout(10000, 0, 1, 1, cell, cell, 61000, 1,
                new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(61000)});
            var id = DungeonRunId.New();
            runs.PreparePartyEntry([new(actor.AccountId, actor.CharacterId, 0, true), new(peer.AccountId, peer.CharacterId, 1, false)],
                layout, DungeonDifficulty.Normal, DungeonCellSeed.CompleteEmpty(cell, 61000), id, DateTimeOffset.UnixEpoch);
            generations.Register(id, [actor, peer]);
        }
        else
        {
            var area = new TownAreaKey(1, 0, 0);
            var position = new CharacterPositionState(0, 0, 0, 0, 0);
            areas.Enter(new(actor.CharacterId, actor.TownAreaLease(), area, position, !privateRoom));
            areas.Enter(new(peer.CharacterId, peer.TownAreaLease(), area, position, true));
        }
        var entry = ExperienceFrameEntry.At(StartingLevel, StartingExperience);
        entry = entry with { CharacterEntry = entry.CharacterEntry with { Features = new([], null!, null!, null!, []) } };
        var ledger = new SkillPointLedger();
        var sender = new RecordingSender();
        await Finish(ledger, new FixedCompletion(ledger, 4), views: new ChangedView(entry, entry, () =>
            {
                if (sourceReplaced) identities[0] = actor with { ConnectionGeneration = 2, ConnectionId = new("one-new") };
            }),
            sessions: sessions, areas: areas, runs: runs, generations: generations, recording: sender);
        if (replaced || privateRoom || sourceReplaced) Assert.Empty(sender.Targets);
        else
        {
            var target = Assert.Single(sender.Targets);
            Assert.Equal(peer.ConnectionId, target.ConnectionId);
            Assert.IsType<CurrentUserStateMessage>(Assert.Single(target.OrderedMessages));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tower_career_publication_reaches_only_current_exact_run_members(bool sourceReplaced)
    {
        var actor = SelectedSessionIdentity.Create(new(1), new(60), new("one"), 1, 10010);
        var peer = SelectedSessionIdentity.Create(new(2), new(61), new("peer"), 1, 10010);
        var stale = SelectedSessionIdentity.Create(new(3), new(62), new("stale-old"), 1, 10010);
        var replacement = stale with { ConnectionGeneration = 2, ConnectionId = new("stale-new") };
        var townPeer = SelectedSessionIdentity.Create(new(4), new(63), new("town-peer"), 1, 10010);
        var identities = new List<SelectedSessionIdentity> { actor, peer, replacement, townPeer };
        var sessions = new PeerSessions(identities);
        var tower = new TowerDomainStub(actor, peer, stale);
        ITownAreaDomainService areas = new Dfo.Server.Application.Sessions.SessionPresenceDomainService();
        var area = new TownAreaKey(1, 0, 0);
        var position = new CharacterPositionState(0, 0, 0, 0, 0);
        areas.Enter(new(actor.CharacterId, actor.TownAreaLease(), area, position, true));
        areas.Enter(new(townPeer.CharacterId, townPeer.TownAreaLease(), area, position, true));
        var entry = ExperienceFrameEntry.At(StartingLevel, StartingExperience);
        entry = entry with { CharacterEntry = entry.CharacterEntry with { Features = new([], null!, null!, null!, []) } };
        var ledger = new SkillPointLedger();
        var sender = new RecordingSender();

        await Finish(ledger, new FixedCompletion(ledger, 4),
            views: new ChangedView(entry, entry, () =>
            {
                if (sourceReplaced)
                    identities[0] = actor with { ConnectionGeneration = 2, ConnectionId = new("one-new") };
            }),
            sessions: sessions, areas: areas, recording: sender, towers: tower);

        if (sourceReplaced)
            Assert.Empty(sender.Targets);
        else
        {
            var target = Assert.Single(sender.Targets);
            Assert.Equal(peer.ConnectionId, target.ConnectionId);
            Assert.IsType<CurrentUserStateMessage>(Assert.Single(target.OrderedMessages));
        }
    }

    private sealed class PeerSessions(IReadOnlyList<SelectedSessionIdentity> values) : ISelectedSessionDomainService
    {
        public SelectedSessionIdentity? FindByCharacter(CharacterId id) => values.SingleOrDefault(s => s.CharacterId == id);
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId id) => values.SingleOrDefault(s => s.ConnectionId == id);
        public SelectedSessionIdentity? Find(AccountId id) => values.SingleOrDefault(s => s.AccountId == id);
        public SelectedSessionIdentity? FindByWireUserId(ushort id) => values.SingleOrDefault(s => s.WireUserId == id);
        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => throw new NotSupportedException();
        public bool ReleaseExact(SelectedSessionIdentity session) => throw new NotSupportedException();
    }

    private sealed class WeaknessState : IWeaknessDomainService, IWeakness
    {
        public byte Value => 28;
        public IWeakness For(CharacterId id) => this;
        public TimeSpan RecoveryInterval => TimeSpan.FromMinutes(1);
        public IReadOnlyList<CharacterId> AdvanceRecovery() => [];
        public ValueTask<int> RecoveryGoldCost(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask SetWeakness() => throw new NotSupportedException();
        public void Recover() => throw new NotSupportedException();
        public void PauseRecovery() => throw new NotSupportedException();
        public void ResumeRecovery() => throw new NotSupportedException();
    }

    private sealed class ChangedView(SelectCharacterResult before, SelectCharacterResult after, Action? afterReload = null)
        : ISelectedCharacterCurrentViewDomainService
    {
        private int reads;
        public ValueTask<SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity session, CancellationToken cancellationToken)
        {
            if (reads++ == 0) return ValueTask.FromResult<SelectCharacterResult?>(before);
            afterReload?.Invoke();
            return ValueTask.FromResult<SelectCharacterResult?>(after);
        }
    }

    private static async Task<IReadOnlyList<IGameServerMessage>> Finish(
        SkillPointLedger ledger,
        ICommandBus bus,
        StubWorldContentEntryRuleService? content = null,
        ushort rewardSelection = FinishQuestRequest.NoRewardSelection,
        ushort completionCount = 1,
        ISelectedCharacterCurrentViewDomainService? views = null,
        ISelectedSessionDomainService? sessions = null,
        ITownAreaDomainService? areas = null,
        IDungeonRunDomainService? runs = null,
        DungeonRunDeliveryRegistry? generations = null,
        RecordingSender? recording = null,
        TowerDomainStub? towers = null)
    {
        var session = SelectedSessionIdentity.Create(
            new AccountId(1),
            new CharacterId(60),
            new NetworkConnectionId("one"),
            connectionGeneration: 1,
            listenerPort: 10010);
        var sender = recording ?? new RecordingSender();
        sessions ??= new OneSession(session);

        var handler = new FinishQuestHandler(
            sessions,
            views ?? new OneRuntimeSession(ExperienceFrameEntry.At(StartingLevel, StartingExperience)),
            new OnePlayingCharacter(ledger),
            bus,
            sender,
            new QuestOfferRefreshDomainService(
                content ?? new StubWorldContentEntryRuleService(), new RecordingQuestLogs(), Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain()),
            new FixedClock(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000)),
            NullLogger<FinishQuestHandler>.Instance,
            areas ?? new Dfo.Server.Application.Sessions.SessionPresenceDomainService(),
            runs ?? new Dfo.Server.Application.World.DungeonRunDomainService(),
            new WeaknessProjectionDelivery(new WeaknessState(), sessions, sender),
            generations ?? new DungeonRunDeliveryRegistry(),
            towers);

        await handler.HandleAsync(
            new NetworkDispatchContext(
                new NetworkConnectionContext(
                    new NetworkConnectionId("one"), NetworkEndpointKind.Game, 10010)),
            new FinishQuestRequest(
                GameMessageMetadata.Empty,
                QuestId,
                rewardSelection,
                completionCount),
            CancellationToken.None);

        return sender.Sent;
    }

    /// <summary>
    /// A bus that hands the quest in, and remembers what the ledger had been asked by then.
    /// </summary>
    /// <remarks>
    /// The second half is what makes the ordering checkable. A slice that asked the character
    /// before the completion was written would be asking about the level the character had
    /// walked in with, which is the same wrong answer the session holds and just as invisible on
    /// the frame.
    /// </remarks>
    private sealed class FixedCompletion(SkillPointLedger ledger, byte? growType = null, byte? awakening = null) : ICommandBus
    {
        /// <summary>What the ledger's count stood at when the completion was written.</summary>
        public int LedgerAsksWhenTheCompletionRan { get; private set; } = -1;

        public FinishQuestCommand? LastCommand { get; private set; }

        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            this.LastCommand = Assert.IsType<FinishQuestCommand>(command);
            this.LedgerAsksWhenTheCompletionRan = ledger.TimesAsked;
            var outcome = new QuestFinishOutcome(
                Finished: true,
                QuestId,
                ObjectiveKind: 2,
                Experience: FinishedTotal - StartingExperience,
                Items: [],
                NewLevel: FinishedLevel,
                NewExperience: FinishedTotal, GrowType: growType) { Awakening = awakening };
            return ValueTask.FromResult((TResult)(object)outcome);
        }
    }

    [Fact]
    public async Task Wrapped_unknown_completion_never_sends_rollback_failure_ack()
    {
        var sender = new RecordingSender();
        await Assert.ThrowsAsync<QuestFinishRejectedException>(async () => await Finish(new SkillPointLedger(),new UnknownCompletion(),recording: sender));
        Assert.Empty(sender.Sent);
    }
    private sealed class UnknownCompletion : ICommandBus
    {
        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken=default) where TResult:notnull =>
            throw new QuestFinishRejectedException(QuestId,"wrapper",new AggregateException(new IOException("other"),
                new Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException(
                    Dfo.Server.Abstractions.Transactions.TransactionUncertaintyReason.RollbackFailure,"unknown",new IOException("rollback"))));
    }

    private sealed class RejectedCompletion : ICommandBus
    {
        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull =>
            throw new QuestFinishRejectedException(QuestId, "settlement failed");
    }

    private sealed class RefusedWithProgress : ICommandBus
    {
        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull =>
            ValueTask.FromResult((TResult)(object)QuestFinishOutcome.Refuse(QuestId,
                new QuestProgressSnapshot([new CarriedQuest(0, QuestId, 1)])));
    }

    /// <summary>A clock that does not move, because nothing here is about when.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
