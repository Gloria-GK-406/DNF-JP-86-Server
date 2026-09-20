using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// A dungeon run in progress: the maze it was given, and which of its rooms the character is in.
/// </summary>
/// <param name="VisitedCells">
/// The cells entered so far. A room the character has been in before is announced as a revisit,
/// which the client distinguishes from a first entry.
/// </param>
/// <param name="ClearRewardCards">
/// What this clear's cards turned out to be, and which of their positions have been claimed. Null
/// until the clear fixes them, which is what "this run has no cards yet" is said with.
/// </param>
public sealed record DungeonRunState(
    DungeonLayout Layout,
    MazeCell CurrentCell,
    int CurrentRoomMapId,
    IReadOnlySet<MazeCell> VisitedCells,
    DateTimeOffset StartedAt,
    byte Difficulty = 0,
    bool IsSettled = false,
    ClearRewardCardSet? ClearRewardCards = null,
    DungeonRunId RunId = default,
    long RoomVersion = 0,
    DungeonRunParticipant? Participant = null,
    IReadOnlyList<DungeonRunParticipant>? Participants = null,
    long EntryRevision = 0,
    IReadOnlySet<CharacterId>? ReadyParticipants = null,
    bool ClearOpen = false,
    long RuntimeRevision = 0,
    IReadOnlyList<DungeonCardSelection>? CardSelections = null,
    bool CardLayoutOpen = false,
    DungeonCellState? CurrentCellState = null)
{
    /// <summary>Stable party identity bound to this run, independent of the client's party number.</summary>
    public PartyKey PartyKey { get; init; }

    /// <summary>The entry-time quest purpose owner, not an authority for current membership.</summary>
    public DungeonRunParticipant? EntryPurposeOwner { get; init; }
    /// <summary>The authoritative runtime mirror of the room this view stands in.</summary>
    public DungeonCellState CurrentCellState { get; init; } = CurrentCellState
        ?? throw new ArgumentNullException(
            nameof(CurrentCellState),
            "A dungeon run cannot exist without a complete current Cell snapshot.");

    /// <summary>Current party members who also entered this run, ordered by current slots.</summary>
    public IReadOnlyList<DungeonRunParticipant> Participants { get; init; } = Participants ?? [];

    /// <summary>The participants whose current client finished loading this room.</summary>
    public IReadOnlySet<CharacterId> ReadyParticipants { get; init; } =
        ReadyParticipants ?? ImmutableHashSet<CharacterId>.Empty;

    /// <summary>The card-panel ownership already released to the party.</summary>
    public IReadOnlyList<DungeonCardSelection> CardSelections { get; init; } =
        CardSelections ?? [];

    /// <summary>Every monster death committed by this run, across all visited cells.</summary>
    public ImmutableArray<DungeonKillRecord> Kills { get; init; } = [];

    /// <summary>Counts derived from <see cref="Kills"/> without becoming a second source.</summary>
    public DungeonKillSummary KillSummary => DungeonKillSummary.From(this.Kills);

    /// <summary>The current in-run party size used by run-scoped reward rules.</summary>
    public int PartyMemberCount => Math.Max(1, this.Participants.Count);

    /// <summary>Whether every actor this room declares has been reported dead.</summary>
    /// <remarks>
    /// AI characters are not counted. They fight beside the player rather than against them, and a
    /// room whose clearing waited on the tutorial's own escort would never clear at all.
    /// </remarks>
    public bool IsCleared => this.CurrentCellState.IsCleared;

    /// <summary>What the room the character is standing in is for.</summary>
    public DungeonRoomKind CurrentRoomKind => Layout.KindOf(CurrentCell);

    /// <summary>Whether this room is the one the run's boss stands in.</summary>
    public bool IsBossRoom => CurrentRoomKind == DungeonRoomKind.Boss;

    /// <summary>
    /// Whether the character may walk out of the room they are in.
    /// </summary>
    /// <remarks>
    /// A live session on 2026-08-26 walked out of the boss room after clearing it and back through
    /// rooms it had already emptied, which the game does not allow. The rule is the room's, not the
    /// run's: the boss room has no way back out of it, and a dungeon that later grows a room with
    /// its own answer brings that answer with it rather than adding a case here.
    /// </remarks>
    public bool MayLeaveCurrentRoom => CurrentRoomKind != DungeonRoomKind.Boss;

}

/// <summary>A process-local identity shared by every participant view of one dungeon run.</summary>
public readonly record struct DungeonRunId(Guid Value)
{
    public static DungeonRunId New() => new(Guid.NewGuid());
}

/// <summary>A transport-free in-run participant projection; slot and leader may change.</summary>
public sealed record DungeonRunParticipant(
    AccountId AccountId,
    CharacterId CharacterId,
    byte Slot,
    bool IsLeader);

/// <summary>A committed entry-state transition emitted by one shared dungeon instance.</summary>
public sealed record DungeonEntryChange(
    DungeonEntryChangeKind Kind,
    DungeonRunState Run,
    long Revision,
    IReadOnlyList<CharacterId> Recipients);

public enum DungeonEntryChangeKind
{
    Prepared,
    ParticipantReady,
    Snapshot,
}

/// <summary>One position of one of a clear's two faces.</summary>
/// <remarks>
/// The kind belongs in the key rather than beside it. The free face and the paid face are two
/// separate faces, so position two of one has nothing to do with position two of the other, and a
/// key that held only the index would have claiming one close the other.
/// </remarks>
public readonly record struct ClearRewardCardId(ClearRewardCardKind Kind, int Index);

/// <summary>
/// What one clear's cards came to, and which of their positions have been claimed.
/// </summary>
/// <remarks>
/// Both faces together, because both are decided at the same moment and neither may be decided
/// twice. Held rather than worked out again on demand: what a face is drawn for includes facts
/// this record does not carry -- see <see cref="ClearRewardOccasion"/> -- so a face redrawn at
/// flip time could be drawn for a different occasion than the one it was fixed at, and the card
/// the player is looking at would change under them.
/// </remarks>
/// <param name="Claimed">
/// Which positions have been taken. Positions rather than faces: a flip takes one position and
/// leaves the rest of that face where they were.
/// <para>
/// The concrete immutable set rather than <c>IReadOnlySet</c>, because claiming a position
/// produces the next set from this one and needs <c>Add</c> to do it. Declared as the interface,
/// the only way to get that back is a cast, and a caller who built this record from an ordinary
/// <c>HashSet</c> would compile and then throw at the first flip. The type says what is required
/// so that the compiler refuses the wrong set instead of the run failing on it.
/// </para>
/// </param>
/// <param name="PaidFlipCost">
/// What a paid flip of this clear costs, or null when this clear names no price for one.
/// <b>Fixed here, with the faces, and never worked out again.</b>
/// <para>
/// <b>Null has two causes and they are not kept apart here.</b> The archive prices a flip by level
/// and answers nothing for a level it does not list; and the dungeon the clear was fought in may
/// have switched its own paid card off, which leaves the clear with no flip for a price to be
/// about. What put the null here is <see cref="ICardRewardDomainService.SettleAsync"/>, which
/// composes both into one absence -- so nothing downstream of this record can tell which happened,
/// and nothing downstream should read it as either one.
/// </para>
/// <para>
/// It is a copy of an archive answer and could in principle outlive the file it came from, which
/// is a real cost and was the argument for asking the file again on every flip instead. It loses
/// to consistency, and not narrowly: the level a clear settles at is taken before the clear's own
/// experience is paid, and the level a flip happens at is taken after -- so a clear that levelled
/// the character across a band of the cost table would announce one price on the result screen
/// and take another at the click. A copy that may go stale within one run is better than a server
/// that quotes one figure and charges a different one, and the run ends long before the file can
/// change under it.
/// </para>
/// <para>
/// <b>That hazard has since gone, and the fixing has stayed.</b> The price is reached by the
/// dungeon's own <c>[basis level]</c> rather than by the character's level, and a dungeon does not
/// level, so there is no second figure left for the two to disagree about. The paragraph above is
/// kept as the argument that put the figure here, not as a danger still being guarded against; the
/// same correction is made on <see cref="ClearRewardSettlementResult"/>.
/// </para>
/// <para>
/// Null rather than zero, all the way through: zero is a price -- a flip given away -- and neither
/// an unpriced level nor a switched-off dungeon is a free one. See
/// <see cref="IClearRewardRuleService.PaidFlipCost"/> and
/// <see cref="IClearRewardRuleService.OffersPaidFlipAsync"/>.
/// </para>
/// </param>
public sealed record ClearRewardCardSet(
    ClearRewardCardFace Free,
    ClearRewardCardFace Paid,
    ImmutableHashSet<ClearRewardCardId> Claimed,
    int? PaidFlipCost)
{
    /// <summary>The face of that kind.</summary>
    public ClearRewardCardFace FaceOf(ClearRewardCardKind kind) =>
        kind == ClearRewardCardKind.Paid ? this.Paid : this.Free;

    /// <summary>
    /// What sits at that position, or null when the face has no position under that number.
    /// </summary>
    /// <remarks>
    /// A position out of range answers null rather than throwing: the number comes off the wire,
    /// and a client that names a fifth card is asking for something that does not exist rather
    /// than breaking the clear.
    /// </remarks>
    public ClearRewardCardSlot? SlotAt(ClearRewardCardKind kind, int index)
    {
        var slots = this.FaceOf(kind).Slots;
        return index >= 0 && index < slots.Count ? slots[index] : null;
    }

    /// <summary>Whether that position has already been taken.</summary>
    public bool IsClaimed(ClearRewardCardKind kind, int index) =>
        this.Claimed.Contains(new ClearRewardCardId(kind, index));
}

/// <summary>One actor of one room, under the running number the client names it by.</summary>
/// <remarks>
/// Keyed by cell for the same reason a ground drop is: a maze may lead through the same map twice,
/// and what the player killed in one of those rooms they have not killed in the other.
/// </remarks>
public readonly record struct RoomActorId(MazeCell Cell, ushort Sequence);

/// <summary>One monster death committed by one dungeon run.</summary>
/// <remarks>
/// <paramref name="Actor"/> is the identity that makes the fact idempotent. Code, level and rank
/// are the frozen Cell facts used to derive per-monster and per-rank totals later; no reward rule
/// is embedded in the record.
/// </remarks>
public readonly record struct DungeonKillRecord(
    RoomActorId Actor,
    int MonsterCode,
    int MonsterLevel,
    MonsterRank Rank);

/// <summary>One immutable projection of a run's exact kill records.</summary>
public sealed record DungeonKillSummary(
    int Total,
    int Ordinary,
    int Champion,
    int SuperChampion,
    int Boss,
    ImmutableDictionary<int, int> ByMonsterCode)
{
    public static DungeonKillSummary From(ImmutableArray<DungeonKillRecord> kills)
    {
        if (kills.IsDefaultOrEmpty)
        {
            return new DungeonKillSummary(
                0, 0, 0, 0, 0, ImmutableDictionary<int, int>.Empty);
        }

        return new DungeonKillSummary(
            kills.Length,
            kills.Count(kill => kill.Rank == MonsterRank.Ordinary),
            kills.Count(kill => kill.Rank == MonsterRank.Champion),
            kills.Count(kill => kill.Rank == MonsterRank.SuperChampion),
            kills.Count(kill => kill.Rank == MonsterRank.Boss),
            kills.GroupBy(kill => kill.MonsterCode)
                .ToImmutableDictionary(group => group.Key, group => group.Count()));
    }
}

/// <summary>An exact, retryable claim over one actor death in one run.</summary>
public sealed record DungeonActorDeathClaim(
    CharacterId CharacterId,
    DungeonRunState Run,
    RoomActorId Actor,
    long Token,
    DungeonRunId RunId = default,
    DungeonCellActorState? ActorState = null);

/// <summary>The ordered Cell change produced by one committed actor death.</summary>
public sealed record DungeonActorDeathChange(
    DungeonRunState Run,
    RoomActorId Actor,
    long Revision,
    long CellRevision,
    IReadOnlyList<DungeonGroundItem> AddedGroundItems,
    bool IsCellCleared,
    IReadOnlyList<CharacterId> Recipients)
{
    /// <summary>Other monsters removed with a boss death, without kill rewards or drops.</summary>
    public IReadOnlyList<ushort> AutoKilledActorSequences { get; init; } = [];
}

/// <summary>An exact claim over one participant's durable effects for one actor death.</summary>
public sealed record DungeonParticipantKillEffectClaim(
    DungeonActorDeathClaim Death,
    CharacterId CharacterId,
    long Token);

/// <summary>An exact, retryable claim over one participant's dungeon settlement.</summary>
public sealed record DungeonSettlementClaim(
    CharacterId CharacterId,
    DungeonRunState Run,
    long Token,
    DungeonRunId RunId = default);

/// <summary>An exact, retryable claim over one numbered drop in one run room.</summary>
public sealed record DungeonGroundDropClaim(
    CharacterId CharacterId,
    DungeonRunState Run,
    GroundDropId Drop,
    long Token,
    DungeonRunId RunId = default,
    DungeonGroundItem? Item = null,
    long RoomVersion = 0,
    long CellRevision = 0);

/// <summary>What kind of durable result consumed one shared ground drop.</summary>
public enum DungeonPickupKind
{
    Item,
    Gold,
}

/// <summary>A committed shared pickup, expressed without transport identities.</summary>
public sealed record DungeonPickupChange(
    DungeonRunId RunId,
    long Revision,
    CharacterId Picker,
    GroundDropId Drop,
    DungeonPickupKind Kind,
    IReadOnlyList<CharacterId> Recipients,
    long RoomVersion = 0,
    long CellRevision = 0);

/// <summary>A shared run opening its clear exit for its current semantic participants.</summary>
public sealed record DungeonClearChange(
    DungeonRunId RunId,
    long Revision,
    IReadOnlyList<CharacterId> Recipients);

/// <summary>One displayed card column owned by one current dungeon participant.</summary>
public sealed record DungeonCardSelection(
    ClearRewardCardKind Kind,
    int Index,
    CharacterId CharacterId,
    byte Seat);

/// <summary>A participant's card panel becoming available.</summary>
public sealed record DungeonCardLayoutChange(
    DungeonRunId RunId,
    long Revision,
    CharacterId CharacterId,
    IReadOnlyList<CharacterId> Recipients);

/// <summary>A committed personal confirmation; selections are empty until reveal is ready.</summary>
public sealed record DungeonCardSelectionChange(
    DungeonRunId RunId,
    long Revision,
    CharacterId Actor,
    IReadOnlyList<DungeonCardSelection> Selections,
    IReadOnlyList<CharacterId> Recipients);

/// <summary>
/// A dungeon pickup publication whose value becomes available only after the surrounding domain
/// transaction commits.
/// </summary>
/// <remarks>
/// Persistent inventory and quest writes commit before the process-local run is allowed to
/// consume the floor piece. Keeping the receipt in the command result lets the projection read
/// that final change after the command bus has settled the transaction without publishing an
/// uncommitted runtime fact.
/// </remarks>
public sealed class DungeonPickupCommit(Func<DungeonPickupChange?> read, bool isDeferred = true)
{
    public DungeonPickupChange? Change => read();

    public bool IsDeferred { get; } = isDeferred;

    public static DungeonPickupCommit Completed(DungeonPickupChange? change) =>
        new(() => change, isDeferred: false);
}

/// <summary>A card selection publication made visible only after its durable reward commits.</summary>
public sealed class DungeonCardSelectionCommit(
    Func<DungeonCardSelectionChange?> read,
    bool isDeferred = true)
{
    public DungeonCardSelectionChange? Change => read();

    public bool IsDeferred { get; } = isDeferred;

    public static DungeonCardSelectionCommit Completed(DungeonCardSelectionChange? change) =>
        new(() => change, isDeferred: false);
}

/// <summary>An exact, retryable reservation of one clear-card position.</summary>
public sealed record DungeonClearRewardCardClaim(
    CharacterId CharacterId,
    DungeonRunState Run,
    ClearRewardCardId Card,
    ClearRewardCardSlot Slot,
    long Token,
    DungeonRunId RunId = default);

/// <summary>One piece lying on a room's floor, under the number the client names it by.</summary>
public sealed record GroundDrop(ushort Number, DungeonRoomDrop Drop);

/// <summary>
/// Which piece of floor a drop was on, so that two rooms numbering from one stay apart.
/// </summary>
/// <remarks>
/// The cell rather than the map identifier: a maze may lead through the same map twice, and the
/// player who cleared one of those rooms has not thereby cleared the other.
/// </remarks>
public readonly record struct GroundDropId(MazeCell Cell, ushort Number);

/// <summary>The dungeon runs the server currently has going, and who is in them.</summary>
/// <remarks>
/// This is the process-local dungeon-instance boundary. It owns the frozen participants, layout,
/// current Cell reference and the set of already-created Cells. Each Cell exclusively owns its
/// Actors, pending loot, ground items, clear state and Cell revision; the Run does not rebuild
/// those facts in parallel collections.
/// <para>
/// In memory only. There is no reconnect in this game -- a character who drops in a dungeon
/// reappears in town -- so a run has nothing to survive and nothing to restore.
/// </para>
/// <para>
/// A shared run has its own identifier and a frozen participant set. Character ids are secondary
/// lookup keys into that aggregate so existing command paths can resolve the caller's participant
/// view without using a connection or a mutable party as the domain identity.
/// </para>
/// </remarks>
public interface IDungeonRunDomainService
{
    /// <summary>Publishes a shared run together with its already-complete initial cell.</summary>
    DungeonEntryChange PreparePartyEntry(
        IReadOnlyList<DungeonRunParticipant> participants,
        DungeonLayout layout,
        byte difficulty,
        DungeonCellSeed initialCell,
        DungeonRunId runId,
        DateTimeOffset startedAt) => throw new NotSupportedException(
            "This dungeon-run service does not implement complete Cell publication.");

    /// <summary>Marks one participant ready only for the exact shared room they loaded.</summary>
    DungeonEntryChange? TryMarkParticipantReady(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion) => null;

    /// <summary>Reads the latest entry projection state visible to one active participant.</summary>
    DungeonEntryChange? FindEntryChange(CharacterId characterId)
    {
        var current = Find(characterId);
        return current is null
            ? null
            : new DungeonEntryChange(
                DungeonEntryChangeKind.Snapshot,
                current,
                current.EntryRevision,
                current.Participants.OrderBy(value => value.Slot)
                    .Select(value => value.CharacterId).ToArray());
    }

    /// <summary>
    /// Atomically abandons an entry preparation only while the named revision is still current.
    /// </summary>
    bool TryAbortEntry(
        DungeonRunId expectedRunId,
        long expectedEntryRevision,
        IReadOnlyList<CharacterId> expectedRecipients) => false;

    /// <summary>The run this character is in, or null when they are not in one.</summary>
    DungeonRunState? Find(CharacterId characterId);

    /// <summary>Finds one active participant view of an exact shared run.</summary>
    DungeonRunState? Find(DungeonRunId runId) => null;

    /// <summary>
    /// Moves the character into an adjoining cell, or answers null when the maze declares no room
    /// there.
    /// </summary>
    /// <remarks>
    /// A cell outside the maze is refused rather than clamped, which is what the reference server
    /// does with a move it cannot resolve. The client asks for a cell it can see a door to, so a
    /// refusal means the two disagree about the maze, and moving somewhere else would hide that.
    /// </remarks>
    DungeonRunState? TryMove(CharacterId characterId, MazeCell target);

    /// <summary>Moves only when the caller still names the current shared room version.</summary>
    DungeonRunState? TryMove(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion,
        MazeCell target)
    {
        var current = Find(characterId);
        return current is not null
            && current.RunId == expectedRunId
            && current.RoomVersion == expectedRoomVersion
                ? TryMove(characterId, target)
                : null;
    }

    /// <summary>
    /// Moves into an existing cell, or atomically publishes <paramref name="preparedCell"/> and
    /// moves into it when the target has not been visited before.
    /// </summary>
    DungeonRunState? TryMove(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion,
        MazeCell target,
        DungeonCellSeed? preparedCell) =>
        TryMove(characterId, expectedRunId, expectedRoomVersion, target);

    /// <summary>
    /// Reserves one drop from the exact run room without consuming it until durable delivery
    /// succeeds.
    /// </summary>
    DungeonGroundDropClaim? TryClaimGroundDrop(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion,
        ushort groundDropNumber) => null;

    /// <summary>
    /// Consumes an exact drop reservation after durable delivery succeeds and publishes the
    /// shared runtime fact to the run's active semantic participants.
    /// </summary>
    DungeonPickupChange? CommitGroundDrop(
        DungeonGroundDropClaim claim,
        DungeonPickupKind kind) =>
        new(
            claim.RunId,
            0,
            claim.CharacterId,
            claim.Drop,
            kind,
            claim.Run.Participants.Select(value => value.CharacterId).ToArray());

    /// <summary>
    /// Stages consumption of a reserved floor piece. Production applies it only after durable
    /// transaction participants commit; simple domain doubles may use the completed default.
    /// </summary>
    DungeonPickupCommit StageGroundDrop(
        DungeonGroundDropClaim claim,
        DungeonPickupKind kind) =>
        DungeonPickupCommit.Completed(CommitGroundDrop(claim, kind));

    /// <summary>Adds source-neutral ground items to the current Cell at an exact revision.</summary>
    DungeonGroundItemsAddedChange? TryAddGroundItems(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion,
        long expectedCellRevision,
        IReadOnlyList<DungeonGroundItemDraft> items) => null;

    /// <summary>
    /// Opens the exact run room's clear exit once after authoritative rules have approved it.
    /// </summary>
    DungeonClearChange? TryOpenClear(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion)
    {
        var run = Find(characterId);
        return run is null
            || run.RunId != expectedRunId
            || run.RoomVersion != expectedRoomVersion
            || run.ClearOpen
                ? null
                : new DungeonClearChange(
                    run.RunId,
                    run.RuntimeRevision + 1,
                    run.Participants.Select(value => value.CharacterId).ToArray());
    }

    /// <summary>Releases an exact drop reservation after a refused or failed delivery.</summary>
    bool ReleaseGroundDrop(DungeonGroundDropClaim claim) => false;

    /// <summary>
    /// Claims one actor death before its durable effects are attempted. A duplicate or pending
    /// report returns null.
    /// </summary>
    DungeonActorDeathClaim? TryClaimActorDeath(
        CharacterId characterId,
        ushort actorSequence) => null;

    /// <summary>Finalizes a successful durable kill and returns the current run.</summary>
    DungeonRunState? CommitActorDeath(DungeonActorDeathClaim claim) => claim.Run;

    /// <summary>Finalizes a durable kill and exposes the exact ordered Cell change.</summary>
    DungeonActorDeathChange? CommitActorDeathChange(DungeonActorDeathClaim claim)
    {
        var run = CommitActorDeath(claim);
        if (run is null)
            return null;
        var added = run.CurrentCellState.GroundItems
            .Where(item => item.Source.Kind == DungeonGroundItemSourceKind.ActorDeath
                && item.Source.ActorSequence == claim.Actor.Sequence)
            .OrderBy(item => item.Number)
            .ToArray();
        return new DungeonActorDeathChange(
            run,
            claim.Actor,
            run.RuntimeRevision,
            run.CurrentCellState.Revision,
            added,
            run.CurrentCellState.IsCleared,
            run.Participants.OrderBy(participant => participant.Slot)
                .Select(participant => participant.CharacterId).ToArray());
    }

    /// <summary>Releases exactly this failed claim so the client may retry it.</summary>
    bool ReleaseActorDeath(DungeonActorDeathClaim claim) => false;

    /// <summary>
    /// Claims the durable kill effects of one frozen participant. Null means that participant's
    /// effects were already committed (or are being attempted by another caller).
    /// </summary>
    DungeonParticipantKillEffectClaim? TryClaimParticipantKillEffect(
        DungeonActorDeathClaim death,
        CharacterId characterId) =>
        death.Run.Participants.Count == 0
        || death.Run.Participants.Any(participant => participant.CharacterId == characterId)
            ? new DungeonParticipantKillEffectClaim(death, characterId, 0)
            : null;

    /// <summary>Marks one participant's transactional kill effects complete.</summary>
    bool CommitParticipantKillEffect(DungeonParticipantKillEffectClaim claim) => true;

    /// <summary>Releases exactly one failed participant-effect attempt for retry.</summary>
    bool ReleaseParticipantKillEffect(DungeonParticipantKillEffectClaim claim) => false;

    /// <summary>
    /// Records that this run has been settled.
    /// </summary>
    /// <remarks>
    /// Answers the run as it stands afterwards, or null when it had already been settled -- which
    /// is what a repeated result report is. The run itself is not ended: what ends it is the
    /// character leaving, and between the result screen and the door the run is still theirs.
    /// <para>
    /// This is the same one-shot guard used by actor-death claims, and it is needed for the same
    /// reason. A settlement pays experience, and it is the client that says when one
    /// happens; without a record of having paid, a repeated report is a repeated payment. That
    /// only became true on 2026-08-29, when the settlement started paying anything at all.
    /// </para>
    /// </remarks>
    DungeonRunState? TrySettle(CharacterId characterId);

    /// <summary>Claims settlement without spending it until durable work has completed.</summary>
    DungeonSettlementClaim? TryClaimSettlement(CharacterId characterId) =>
        TrySettle(characterId) is { } run
            ? new DungeonSettlementClaim(characterId, run, 0, run.RunId)
            : null;

    /// <summary>Claims settlement only when the character is still in the named run.</summary>
    DungeonSettlementClaim? TryClaimSettlement(
        CharacterId characterId,
        DungeonRunId expectedRunId)
    {
        var current = Find(characterId);
        return current is not null && current.RunId == expectedRunId
            ? TryClaimSettlement(characterId)
            : null;
    }

    /// <summary>Commits a successfully persisted settlement.</summary>
    DungeonRunState? CommitSettlement(DungeonSettlementClaim claim) => claim.Run;

    /// <summary>Releases exactly this failed settlement attempt for retry.</summary>
    bool ReleaseSettlement(DungeonSettlementClaim claim) => false;

    /// <summary>Opens one settled participant's card panel on the named run exactly once.</summary>
    DungeonCardLayoutChange? TryOpenCardLayout(
        CharacterId characterId,
        DungeonRunId expectedRunId) => null;

    /// <summary>
    /// Fixes what this clear's two faces hold, and answers the run as it stands afterwards.
    /// </summary>
    /// <remarks>
    /// <b>Idempotent, and that is the point of it.</b> A run whose faces are already fixed keeps
    /// the ones it has and is answered with them; what was passed in is dropped. A repeated result
    /// report is the ordinary way to arrive here a second time, and the one thing that must not
    /// happen is the player being shown a different card than the one they were shown a moment
    /// ago.
    /// <para>
    /// This is why it is not shaped like <see cref="TrySettle"/>, which answers null on a repeat.
    /// A second settle has nothing to give back, whereas a second fix has exactly what the caller
    /// asked for -- the cards of this clear -- and refusing would make every caller ask again.
    /// </para>
    /// <para>
    /// The price of a paid flip is fixed with the faces and by the same rule: a run that already
    /// has one keeps it. It is here rather than asked for again later because the two have to
    /// agree -- see <see cref="ClearRewardCardSet.PaidFlipCost"/> for what that costs and why it
    /// is worth it.
    /// </para>
    /// <para>
    /// Null only when the character is in no run at all.
    /// </para>
    /// </remarks>
    DungeonRunState? FixClearRewardCards(
        CharacterId characterId,
        ClearRewardCardFace free,
        ClearRewardCardFace paid,
        int? paidFlipCost);

    /// <summary>Fixes cards only on the named run, never on a replacement run.</summary>
    DungeonRunState? FixClearRewardCards(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        ClearRewardCardFace free,
        ClearRewardCardFace paid,
        int? paidFlipCost)
    {
        var current = Find(characterId);
        return current is not null && current.RunId == expectedRunId
            ? FixClearRewardCards(characterId, free, paid, paidFlipCost)
            : null;
    }

    /// <summary>
    /// Takes one position of one of this clear's faces.
    /// </summary>
    /// <remarks>
    /// Answers what was behind it, or null when there was nothing to take: no run, no faces fixed
    /// yet, no position under that number, or that position already claimed. The last of those is
    /// what a repeated flip is, and refusing it is the whole reason claiming is recorded -- the
    /// faces are fixed and can be read again at any time, so without a record of what has been
    /// taken one card could be handed over as often as it was asked for.
    /// <para>
    /// A position that was claimed successfully but holds nothing answers a slot rather than null.
    /// An empty position is an ordinary outcome of the draw, and it has to stay distinguishable
    /// from a refusal.
    /// </para>
    /// </remarks>
    ClearRewardCardSlot? TryClaimClearRewardCard(
        CharacterId characterId, ClearRewardCardKind kind, int index);

    /// <summary>Reserves one card on the named run without consuming it before durable delivery.</summary>
    DungeonClearRewardCardClaim? TryReserveClearRewardCard(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        ClearRewardCardKind kind,
        int index)
    {
        var current = Find(characterId);
        var slot = current?.RunId == expectedRunId
            ? TryClaimClearRewardCard(characterId, kind, index)
            : null;
        return slot is null
            ? null
            : new DungeonClearRewardCardClaim(
                characterId,
                current!,
                new ClearRewardCardId(kind, index),
                slot,
                0,
                expectedRunId);
    }

    /// <summary>
    /// Consumes a reserved position after durable delivery and returns the full shared panel.
    /// </summary>
    DungeonCardSelectionCommit CommitClearRewardCard(
        DungeonClearRewardCardClaim claim) => DungeonCardSelectionCommit.Completed(null);

    /// <summary>Releases a reserved clear-card position after durable delivery fails.</summary>
    bool ReleaseClearRewardCard(DungeonClearRewardCardClaim claim) => false;

    /// <summary>Ends the run. False when the character was not in one.</summary>
    bool End(CharacterId characterId);

    /// <summary>Ends only the named run, leaving a replacement run untouched.</summary>
    bool End(CharacterId characterId, DungeonRunId expectedRunId) =>
        Find(characterId) is { } current
        && current.RunId == expectedRunId
        && End(characterId);
}
