using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// One piece still lying on a room's floor, with what kind it is already read.
/// </summary>
/// <remarks>
/// The flag travels with the piece for the same reason an actor's does: the frame that announces the
/// floor needs it per piece, and which kind counts as equipment is a fact about items that a frame
/// has no business holding.
/// </remarks>
public readonly record struct FloorDropDescription(GroundDrop Drop, bool IsEquipment);

/// <summary>
/// What a character finds on walking into a room.
/// </summary>
/// <param name="FirstEntry">
/// False when the character has stood in this room before, in which case it is described as empty.
/// </param>
public sealed record DungeonRoomVisit(
    DungeonRunState Run,
    bool FirstEntry,
    IReadOnlyList<RoomActorDescription> Actors,
    IReadOnlyList<FloorDropDescription> FloorDrops,
    ushort FirstActorSequence);

/// <summary>A dungeon entered: the layout it was given, and its first room.</summary>
public sealed record DungeonEntry(DungeonLayout Layout, DungeonRoomVisit Visit);

/// <summary>One resolved layout and the participant-specific views of its shared first room.</summary>
public sealed record DungeonPartyEntry(
    DungeonLayout Layout,
    IReadOnlyDictionary<CharacterId, DungeonRoomVisit> Visits,
    DungeonEntryChange? Change = null)
{
    public IReadOnlyList<DungeonEntryInventoryChange> InventoryChanges { get; init; } = [];
}

public enum DungeonEntryFailure { DungeonNotFound, DifficultyNotUnlocked, InsufficientFatigue, AlreadyInDungeon,
    HellUnavailable, HellQuestNotCleared, MissingHellTicket, HellConfigurationInvalid,
    MissingRequiredItem, LevelTooLow, MissingPrerequisite, DailyEntryLimitReached, MemberEntryLimitReached,
    ConfigurationInvalid }

public sealed record DungeonEntryAttempt(DungeonPartyEntry? Entry, DungeonEntryFailure? Failure, byte? BlockedSlot = null,
    string? Detail = null)
{
    public static DungeonEntryAttempt Accepted(DungeonPartyEntry entry) => new(entry, null);
    public static DungeonEntryAttempt Refused(DungeonEntryFailure failure, string? detail = null) => new(null, failure, Detail: detail);
}

public enum DungeonMoveFailure { StaleRun, CellNotFound, Disconnected, NoCompatibleMap, StateRejected }
public sealed record DungeonMoveAttempt(DungeonRoomVisit? Visit, DungeonMoveFailure? Failure);

/// <summary>
/// Entering a dungeon and walking between its rooms.
/// </summary>
/// <remarks>
/// The use case behind two frames, and the reason it is a service rather than two handlers: entering
/// resolves a layout from quest state, remembers a run against it, and describes the room the
/// character lands in — three things that have to happen together and in that order, because the
/// layout cannot be recomputed later and still describe the run the client is in.
/// <para>
/// It orchestrates: the run is world state and lives in <see cref="IDungeonRunDomainService"/>,
/// while what a dungeon's maze is, what a room holds and what kind a piece is are rules. Nothing
/// about the wire is decided here; a caller is handed values and builds its own frames.
/// </para>
/// </remarks>
public interface IDungeonVisitDomainService
{
    ValueTask<DungeonEntryAttempt> EnterGrantedPartyAsync(IReadOnlyList<DungeonRunParticipant> participants,
        DungeonLayout layout, DungeonEntryFacts facts, IDungeonAdmissionReceipt receipt, CancellationToken ct) =>
        throw new NotSupportedException("This visit service cannot start a paid entry.");

    ValueTask<DungeonEntryAttempt> TryEnterPreparedPartyAsync(
        IReadOnlyList<DungeonRunParticipant> participants, DungeonLayout layout,
        DungeonEntryFacts facts, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This visit service does not accept a prepared layout.");

    /// <summary>
    /// Whether the selection screen's state is deferred to this character's first entry.
    /// </summary>
    /// <remarks>
    /// Asked by two frames built in two slices -- the screen's own burst and the answer to
    /// entering -- which is why it is here rather than in either of them.
    /// </remarks>
    bool DefersSelectionState(byte characterLevel, bool tutorialCompleted = false);

    /// <summary>
    /// Begins a run and describes its first room, or null when the archive declares no enterable
    /// dungeon under that identifier.
    /// </summary>
    /// <remarks>
    /// The facts are handed in rather than found. Collecting them is the caller's job -- it is the
    /// one holding the lease while it asks -- and this service stays a function of what it is
    /// given, which is what keeps a run reproducible from its record.
    /// </remarks>
    ValueTask<DungeonEntry?> EnterAsync(
        CharacterId characterId,
        int dungeonId,
        DungeonDifficulty difficulty,
        DungeonEntryFacts facts,
        CancellationToken cancellationToken);

    async ValueTask<DungeonEntryAttempt> TryEnterPartyAsync(
        IReadOnlyList<DungeonRunParticipant> participants, int dungeonId,
        DungeonEntryFacts facts, CancellationToken cancellationToken)
    {
        var entry = await EnterPartyAsync(participants, dungeonId, facts.Difficulty, facts, cancellationToken)
            .ConfigureAwait(false);
        return entry is null ? DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound)
            : DungeonEntryAttempt.Accepted(entry);
    }

    /// <summary>Resolves one layout and atomically begins one run for all frozen participants.</summary>
    async ValueTask<DungeonPartyEntry?> EnterPartyAsync(
        IReadOnlyList<DungeonRunParticipant> participants,
        int dungeonId,
        DungeonDifficulty difficulty,
        DungeonEntryFacts facts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participants);
        if (participants.Count != 1)
        {
            throw new NotSupportedException(
                "This dungeon-visit service does not implement shared party entry.");
        }

        var entered = await EnterAsync(
            participants[0].CharacterId,
            dungeonId,
            difficulty,
            facts,
            cancellationToken).ConfigureAwait(false);
        return entered is null
            ? null
            : new DungeonPartyEntry(
                entered.Layout,
                new Dictionary<CharacterId, DungeonRoomVisit>
                {
                    [participants[0].CharacterId] = entered.Visit,
                });
    }

    /// <summary>
    /// Walks the character into an adjoining cell and describes what is there, or null when the
    /// maze declares no room at that cell.
    /// </summary>
    /// <param name="current">
    /// The run as the caller found it. Handed in rather than looked up again, so that whether this
    /// is a first entry is decided against the same run the caller checked the room's exits on.
    /// </param>
    ValueTask<DungeonRoomVisit?> MoveAsync(
        CharacterId characterId,
        DungeonRunState current,
        MazeCell target,
        CancellationToken cancellationToken);

    async ValueTask<DungeonMoveAttempt> TryMoveAsync(CharacterId characterId, DungeonRunState current,
        MazeCell target, CancellationToken cancellationToken)
    {
        var visit = await MoveAsync(characterId, current, target, cancellationToken).ConfigureAwait(false);
        return new(visit, visit is null ? DungeonMoveFailure.StateRejected : null);
    }
}
