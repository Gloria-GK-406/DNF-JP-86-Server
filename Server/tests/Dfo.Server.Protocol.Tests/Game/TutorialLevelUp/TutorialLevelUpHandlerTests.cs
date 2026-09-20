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
using Dfo.Server.Protocol.Game.TutorialLevelUp;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.TutorialLevelUp;

/// <summary>
/// What granting the tutorial's own level does with the character's standing.
/// </summary>
/// <remarks>
/// <b>The one place in the tutorial where a level is actually gained.</b> The kills before it
/// report where the character stands without moving it; this hands out level two outright. That
/// makes it the frame that most needs the skill points worked out again -- and it is the frame
/// where reading the session's entry-time projection is guaranteed to be wrong rather than merely
/// likely to be, because the projection was made at level one and the character is now level two
/// by construction.
/// <para>
/// The grant is a <i>set</i>, not a gain: the command writes the target level and the threshold
/// that level starts at rather than adding anything. So there is no award to read a new standing
/// off, and nothing on this path can tell the right figure from the stale one except asking the
/// character after the write.
/// </para>
/// </remarks>
public sealed class TutorialLevelUpHandlerTests
{
    /// <summary>Where the tutorial finds the character.</summary>
    private const byte StartingLevel = 1;

    /// <summary>What it holds on the way in, which the grant replaces outright.</summary>
    private const uint StartingExperience = 40;

    /// <summary>The threshold level two starts at, as the grant hands it back.</summary>
    private const uint GrantedExperience = 172;

    /// <summary>
    /// The points reported are the ones the granted level bought, not the ones the session holds.
    /// </summary>
    /// <remarks>
    /// <b>The criterion this file exists for.</b> Two things have to be true and neither shows on
    /// the frame: the figure has to come from the character rather than from the session, and it
    /// has to be asked for after the level has been written. The ledger counts how many times it
    /// was asked, so a slice that read the session asks nobody; the bus records what that count
    /// stood at when the grant ran, so a slice that asked first records one rather than none.
    /// <para>
    /// Once, and only once. The grant is the only thing on this path that moves the ledger, so a
    /// second ask would be a second walk through the archive for an answer that cannot have
    /// changed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThePointsReportedAreTheOnesTheGrantedLevelBought()
    {
        var ledger = new SkillPointLedger();
        var bus = new FixedGrant(ledger);

        var sent = await AskForTheLevel(ledger, bus);

        Assert.Equal(1, ledger.TimesAsked);
        Assert.Equal(0, bus.LedgerAsksWhenTheGrantRan);

        var experience = sent.OfType<ExperienceNotification>().Single();
        Assert.Equal(SkillPointLedger.BeforeTheLevel, experience.SkillPoints);
        Assert.NotEqual(SkillPointLedger.StaleEntryMirror, experience.SkillPoints);
    }

    /// <summary>
    /// The response reports the level the grant returned.
    /// </summary>
    /// <remarks>
    /// The reference server's own order, from its 2026-08-23 capture of the tutorial: the
    /// experience frame, the quest list for the new level, then the acknowledgement. The list is
    /// worked out again rather than edited, because the level is one of its inputs and its first
    /// byte IS the level -- a session that skipped this would go on drawing the old one.
    /// </remarks>
    [Fact]
    public async Task TheTutorialReportsTheLevelItGranted()
    {
        var ledger = new SkillPointLedger();
        var bus = new FixedGrant(ledger);
        var content = new StubWorldContentEntryRuleService();

        var sent = await AskForTheLevel(ledger, bus, content);

        Assert.Equal(
            [typeof(ExperienceNotification), typeof(AvailableQuestListMessage),
             typeof(TutorialLevelUpResponse)],
            sent.Select(frame => frame.GetType()));

        var experience = sent.OfType<ExperienceNotification>().Single();
        Assert.Equal(GrantTutorialLevelCommandHandler.TargetLevel, experience.Level);
        Assert.Equal(GrantedExperience, experience.TotalExperience);
        Assert.Equal(
            GrantTutorialLevelCommandHandler.TargetLevel, content.LastSubject?.Level);
        Assert.Equal(
            TutorialLevelUpResponse.Granted,
            sent.OfType<TutorialLevelUpResponse>().Single().Result);
    }

    /// <summary>
    /// A granted level the character has no ledger for still stands, and sends no experience frame.
    /// </summary>
    /// <remarks>
    /// The frame carries the remaining points as well as the total, and inventing a figure would
    /// tell the client it had points it does not. The level itself still stands, and so does the
    /// acknowledgement the tutorial is waiting on -- the write happened before the ledger was
    /// asked.
    /// <para>
    /// The other end of the same seam: a slice reading the session would find a figure there
    /// whatever the character can answer, and would send a frame that has no business existing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AGrantWithNoLedgerSendsNoExperienceFrame()
    {
        var ledger = new SkillPointLedger(present: false);

        var sent = await AskForTheLevel(ledger, new FixedGrant(ledger));

        Assert.Equal(
            [typeof(AvailableQuestListMessage), typeof(TutorialLevelUpResponse)],
            sent.Select(frame => frame.GetType()));
        Assert.Equal(
            TutorialLevelUpResponse.Granted,
            sent.OfType<TutorialLevelUpResponse>().Single().Result);
    }

    private static async Task<IReadOnlyList<IGameServerMessage>> AskForTheLevel(
        SkillPointLedger ledger,
        FixedGrant bus,
        StubWorldContentEntryRuleService? content = null)
    {
        var session = SelectedSessionIdentity.Create(
            new AccountId(1),
            new CharacterId(60),
            new NetworkConnectionId("one"),
            connectionGeneration: 1,
            listenerPort: 10010);
        var sender = new RecordingSender();

        var handler = new TutorialLevelUpHandler(
            new OneSession(session),
            new OneRuntimeSession(ExperienceFrameEntry.At(StartingLevel, StartingExperience)),
            new OnePlayingCharacter(ledger),
            new InARun(),
            bus,
            sender,
            new QuestOfferRefreshDomainService(
                content ?? new StubWorldContentEntryRuleService(), new RecordingQuestLogs(), Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain()),
            NullLogger<TutorialLevelUpHandler>.Instance);

        await handler.HandleAsync(
            new NetworkDispatchContext(
                new NetworkConnectionContext(
                    new NetworkConnectionId("one"), NetworkEndpointKind.Game, 10010)),
            new TutorialLevelUpRequest(GameMessageMetadata.Empty),
            CancellationToken.None);

        return sender.Sent;
    }

    /// <summary>
    /// A bus that grants the level, and remembers what the ledger had been asked by then.
    /// </summary>
    /// <remarks>
    /// The second half is what makes the ordering checkable. A slice that asked the character
    /// before the level was written would be asking about level one, which is the same wrong
    /// answer the session holds and just as invisible on the frame.
    /// </remarks>
    private sealed class FixedGrant(SkillPointLedger ledger) : ICommandBus
    {
        /// <summary>What the ledger's count stood at when the level was written.</summary>
        public int LedgerAsksWhenTheGrantRan { get; private set; } = -1;


        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            var grant = Assert.IsType<GrantTutorialLevelCommand>(command);
            this.LedgerAsksWhenTheGrantRan = ledger.TimesAsked;
            var result = new TutorialLevelUpResult(
                TutorialLevelUpStatus.Granted,
                GrantTutorialLevelCommandHandler.TargetLevel,
                GrantedExperience);
            return ValueTask.FromResult((TResult)(object)result);
        }
    }

    /// <summary>
    /// A character standing in a dungeon, which is where the tutorial asks from.
    /// </summary>
    /// <remarks>
    /// The reference server requires it, and so does this: the tutorial asks for its level from
    /// inside its own run, and a request from town is the two sides disagreeing about where the
    /// character is.
    /// </remarks>
    private sealed class InARun : IDungeonRunDomainService
    {
        private static readonly DungeonLayout Layout = new(
            DungeonId: 60,
            MazeIndex: 0,
            Width: 1,
            Height: 1,
            StartCell: new MazeCell(0, 0),
            BossCell: new MazeCell(0, 0),
            StartRoomMapId: 1,
            Seed: 0,
            Rooms: new Dictionary<MazeCell, int> { [new MazeCell(0, 0)] = 1 },
            BasisLevel: 1,
            ExperienceWeight: 1.0);

        public DungeonRunState? Find(CharacterId characterId) => new(
            Layout,
            new MazeCell(0, 0),
            CurrentRoomMapId: 1,
            VisitedCells: new HashSet<MazeCell>(),
            StartedAt: DateTimeOffset.UnixEpoch,
            CurrentCellState: DungeonCellSeed.CompleteEmpty(
                new MazeCell(0, 0), 1).Snapshot());

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

        public DungeonRunState? TryReportActorDead(CharacterId characterId, ushort actorSequence) =>
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
