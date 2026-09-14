using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
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
                killRules: new OneDeclaredAiCharacter(),
                dropService: new DropsMustNotBeRolled()));
        var sent = await Kill(
            bus: bus,
            run: run,
            killRules: new OneDeclaredAiCharacter(),
            dropService: new DropsMustNotBeRolled());
        await Kill(
            bus: bus,
            run: run,
            killRules: new OneDeclaredAiCharacter(),
            dropService: new DropsMustNotBeRolled());

        Assert.Equal(1, run.Restores);
        Assert.Equal(2, bus.TimesAsked);
        Assert.Equal(HuntEnemyTargetTypes.AiCharacter, bus.LastKillCommand?.EnemyType);
        Assert.Single(sent.OfType<MonsterDeathNotification>());
    }

    private static async Task<IReadOnlyList<IGameServerMessage>> Kill(
        bool leveledUp = false,
        SkillPointLedger? ledger = null,
        FixedKillAward? bus = null,
        bool withLedger = true,
        StubWorldContentEntryRuleService? content = null,
        OneRun? run = null,
        IMonsterKillDomainService? killRules = null,
        IMonsterDropService? dropService = null)
    {
        var session = SelectedSessionIdentity.Create(
            new AccountId(1),
            new CharacterId(60),
            new NetworkConnectionId("one"),
            connectionGeneration: 1,
            listenerPort: 10010);
        var sender = new RecordingSender();

        var handler = new MonsterDeathHandler(
            new OneSession(session),
            new OneRuntimeSession(ExperienceFrameEntry.At(StartingLevel, StartingExperience)),
            new OnePlayingCharacter(ledger ?? new SkillPointLedger(withLedger)),
            run ?? new OneRun(),
            killRules ?? new OneDeclaredMonster(),
            dropService ?? new NothingDropped(),
            new AlwaysCompleteSettlementRules(),
            bus ?? new FixedKillAward(leveledUp),
            new QuestOfferRefreshDomainService(
                content ?? new StubWorldContentEntryRuleService(), new RecordingQuestLogs(), Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain()),
            sender,
            ExperienceDiagnostics.None,
            NullLogger<MonsterDeathHandler>.Instance);

        await handler.HandleAsync(
            new NetworkDispatchContext(
                new NetworkConnectionContext(
                    new NetworkConnectionId("one"), NetworkEndpointKind.Game, 10010)),
            new MonsterDeathRequest(GameMessageMetadata.Empty, DeadActor, WireUserId: 1),
            CancellationToken.None);

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
        int failuresBeforeSuccess = 0) : ICommandBus
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
            this.QuestCommands++;
            this.LastKillCommand = kill;
            this.TimesAsked++;
            if (this._remainingFailures-- > 0)
                throw new InvalidOperationException("injected durable kill failure");
            var result = new ApplyMonsterKillResult(
                awarded ? new ExperienceAward(
                    Base: KillGain,
                    Contributions: [],
                    Gained: KillGain,
                    Level: leveledUp ? (byte)(StartingLevel + 1) : StartingLevel,
                    Total: StartingExperience + KillGain,
                    LeveledUp: leveledUp) : null,
                []);
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

    /// <summary>A kill that put nothing on the floor, which is what most kills are.</summary>
    private sealed class NothingDropped : IMonsterDropService
    {
        public ValueTask<IReadOnlyList<MonsterDrop>> ForKillAsync(
            MonsterKill kill, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<MonsterDrop>>([]);
    }

    private sealed class DropsMustNotBeRolled : IMonsterDropService
    {
        public ValueTask<IReadOnlyList<MonsterDrop>> ForKillAsync(
            MonsterKill kill, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("AI character deaths must not roll drops.");
    }

    /// <summary>
    /// A run that takes one report of one actor's death, exactly as the real service takes it.
    /// </summary>
    /// <remarks>
    /// The boss cell is deliberately not the one the character is standing in. Clearing the boss
    /// room opens the exit, which is a different decision with frames of its own; what is checked
    /// here is the kill itself.
    /// </remarks>
    private sealed class OneRun : IDungeonRunDomainService
    {
        private bool _reported;
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
            Rooms: new Dictionary<MazeCell, int>
            {
                [new MazeCell(0, 0)] = 1,
                [new MazeCell(1, 0)] = 2,
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
            TakenGroundDrops: new HashSet<GroundDropId>(),
            DeadActors: new HashSet<RoomActorId>(),
            StartedAt: DateTimeOffset.UnixEpoch);

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

        public DungeonRunState? CommitActorDeath(DungeonActorDeathClaim claim) => claim.Run;

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
            CharacterId characterId, DungeonLayout layout, byte difficulty) =>
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
}
