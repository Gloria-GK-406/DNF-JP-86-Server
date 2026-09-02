using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

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
    IReadOnlySet<GroundDropId> TakenGroundDrops,
    IReadOnlySet<RoomActorId> DeadActors,
    DateTimeOffset StartedAt,
    byte Difficulty = 0,
    bool IsSettled = false,
    IReadOnlyList<AllocatedMonsterDrop>? MonsterDrops = null,
    ClearRewardCardSet? ClearRewardCards = null)
{
    /// <summary>What the run's kills have put on the floor, in the order they were numbered.</summary>
    /// <remarks>
    /// Held rather than derived, which is the difference between these and the drops a room
    /// declares. A room's floor can be described again from the archive at any time; what a
    /// monster dropped exists nowhere else, so if this were lost the piece would be unclaimable.
    /// </remarks>
    public IReadOnlyList<AllocatedMonsterDrop> MonsterDrops { get; init; } = MonsterDrops ?? [];

    /// <summary>Whether every actor this room declares has been reported dead.</summary>
    /// <remarks>
    /// AI characters are not counted. They fight beside the player rather than against them, and a
    /// room whose clearing waited on the tutorial's own escort would never clear at all.
    /// </remarks>
    public bool IsCleared(IReadOnlyList<DungeonRoomActor> declared, ushort firstSequence)
    {
        ArgumentNullException.ThrowIfNull(declared);
        for (var index = 0; index < declared.Count; index++)
        {
            if (declared[index].ActorType > LastHostileActorType)
                continue;
            var sequence = unchecked((ushort)(firstSequence + index));
            if (!DeadActors.Contains(new RoomActorId(CurrentCell, sequence)))
                return false;
        }

        return true;
    }

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

    /// <summary>
    /// Everything above this is an ally, not a monster. Type five is the tutorial's own escort.
    /// </summary>
    private const byte LastHostileActorType = 3;

    /// <summary>
    /// What of a room's declared drops is still on its floor, each under the number the client
    /// names it by when it picks one up.
    /// </summary>
    /// <remarks>
    /// The number is the drop's position among everything the room declares, counted from one, and
    /// it is derived rather than allocated: a room re-described mid-run has to name the same piece
    /// the same way, or the client's request would arrive under a number this server had given to
    /// something else. Which is also why the taken ones are removed after the numbering rather
    /// than before -- dropping one first would renumber the rest.
    /// </remarks>
    public IReadOnlyList<GroundDrop> RemainingGroundDrops(IReadOnlyList<DungeonRoomDrop> declared)
    {
        ArgumentNullException.ThrowIfNull(declared);
        return
        [
            .. declared
                .Select((drop, index) => new GroundDrop(checked((ushort)(index + 1)), drop))
                .Where(candidate => !TakenGroundDrops.Contains(
                    new GroundDropId(CurrentCell, candidate.Number))),
        ];
    }

    /// <summary>
    /// What this room still has lying under that number, or null when it has nothing there.
    /// </summary>
    public GroundDrop? FindGroundDrop(IReadOnlyList<DungeonRoomDrop> declared, ushort number) =>
        RemainingGroundDrops(declared)
            .FirstOrDefault(candidate => candidate.Number == number);

    /// <summary>
    /// The next number a piece dropped in this room may be given.
    /// </summary>
    /// <remarks>
    /// Above everything the room declares and above everything already dropped in it. The room's
    /// own pieces are numbered from one by their declaration order and are renumbered every time
    /// the room is described, so a dropped piece that took one of those numbers would be shadowed
    /// by a box the moment the player walked back in.
    /// <para>
    /// Per cell rather than per run, because that is how the numbering already works here: a maze
    /// may lead through one map twice and the two rooms number from one independently.
    /// <b>This deviates from live capture, not from the reference server:</b> six captured frames
    /// number 1 through 6 straight across a run without resetting per room. Both are
    /// collision-free, and this server's declared floor pieces are already numbered per room, so
    /// numbering dropped pieces per run would have needed the declared ones to change with them.
    /// </para>
    /// </remarks>
    public ushort NextMonsterDropNumber(int declaredDropCount)
    {
        var highest = declaredDropCount;
        foreach (var allocated in MonsterDrops)
        {
            if (allocated.Cell == CurrentCell && allocated.Drop.Number > highest)
                highest = allocated.Drop.Number;
        }

        return checked((ushort)(highest + 1));
    }

    /// <summary>What a kill dropped in this room and nobody has taken, under its number.</summary>
    /// <remarks>
    /// The whole record rather than the piece, because what identifies the piece to the client is
    /// derived from the kill that produced it and not from its number.
    /// </remarks>
    public AllocatedMonsterDrop? FindMonsterDrop(ushort number)
    {
        foreach (var allocated in MonsterDrops)
        {
            if (allocated.Cell == CurrentCell
                && allocated.Drop.Number == number
                && !TakenGroundDrops.Contains(new GroundDropId(CurrentCell, number)))
            {
                return allocated;
            }
        }

        return null;
    }
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

/// <summary>One piece a kill put on a room's floor, under the number it was given.</summary>
/// <param name="ActorSequence">
/// Which kill produced it. Kept because the value that tells one piece of equipment from another
/// is derived from the kill rather than from the piece: every piece of one kill carries the same
/// one, which is what the captured frames show, and the piece a player ends up holding has to
/// carry the value its arrival announced.
/// </param>
public readonly record struct AllocatedMonsterDrop(
    MazeCell Cell, ushort ActorSequence, MonsterDrop Drop);

/// <summary>One actor of one room, under the running number the client names it by.</summary>
/// <remarks>
/// Keyed by cell for the same reason a ground drop is: a maze may lead through the same map twice,
/// and what the player killed in one of those rooms they have not killed in the other.
/// </remarks>
public readonly record struct RoomActorId(MazeCell Cell, ushort Sequence);

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
/// Deliberately small, and deliberately not the aggregate described in
/// <c>Docs/领域结构重构设计.md</c>. It exists because walking between rooms cannot be answered
/// without remembering which maze the character was given, and that remembering had to start
/// somewhere. It keeps only what the next frame needs.
/// <para>
/// In memory only. There is no reconnect in this game -- a character who drops in a dungeon
/// reappears in town -- so a run has nothing to survive and nothing to restore.
/// </para>
/// <para>
/// Keyed by character rather than by party because parties are not modelled yet. When they are,
/// entry becomes a party-grained transition and this key moves with it.
/// </para>
/// </remarks>
public interface IDungeonRunDomainService
{
    /// <summary>
    /// Starts a run in its layout's start room, replacing whatever that character was in.
    /// </summary>
    /// <remarks>
    /// Replacing rather than refusing: a character who selects a dungeon has left whatever they
    /// were in, and the client has already been told so. Refusing here would leave the server
    /// describing a run the client has abandoned.
    /// </remarks>
    /// <param name="difficulty">
    /// What the character chose on the way in. It is a fact about this run rather than about the
    /// dungeon, so it is not on the layout: the same maze entered twice at two difficulties is two
    /// runs whose kills are worth different amounts.
    /// </param>
    DungeonRunState Begin(CharacterId characterId, DungeonLayout layout, byte difficulty);

    /// <summary>The run this character is in, or null when they are not in one.</summary>
    DungeonRunState? Find(CharacterId characterId);

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

    /// <summary>
    /// Takes one piece off the floor of the room the character is standing in.
    /// </summary>
    /// <remarks>
    /// Answers the run as it stands afterwards, or null when that piece was already gone -- which
    /// is what a second request for the same number is. Refusing the second is the whole point:
    /// the drops a room declares are read back out of the archive every time the room is
    /// described, so without a record of what has been taken a player could walk out of a room and
    /// back in and be handed the same item again.
    /// </remarks>
    DungeonRunState? TryTakeGroundDrop(CharacterId characterId, ushort groundDropNumber);

    /// <summary>
    /// Records that one of the room's actors is dead.
    /// </summary>
    /// <remarks>
    /// Answers the run as it stands afterwards, or null when that actor was already down -- which
    /// is what a repeated report is. What this is for is knowing when a room has been emptied: the
    /// dungeon's exit does not open until the boss room's last monster is reported dead, and the
    /// client will not ask to leave until it has been told the exit is open.
    /// </remarks>
    DungeonRunState? TryReportActorDead(CharacterId characterId, ushort actorSequence);

    /// <summary>
    /// Records that this run has been settled.
    /// </summary>
    /// <remarks>
    /// Answers the run as it stands afterwards, or null when it had already been settled -- which
    /// is what a repeated result report is. The run itself is not ended: what ends it is the
    /// character leaving, and between the result screen and the door the run is still theirs.
    /// <para>
    /// This is the same guard a kill has in <see cref="TryReportActorDead"/>, and it is needed for
    /// the same reason. A settlement pays experience, and it is the client that says when one
    /// happens; without a record of having paid, a repeated report is a repeated payment. That
    /// only became true on 2026-08-29, when the settlement started paying anything at all.
    /// </para>
    /// </remarks>
    DungeonRunState? TrySettle(CharacterId characterId);

    /// <summary>
    /// Puts what a kill produced on the floor of the room the character is standing in, numbering
    /// each piece.
    /// </summary>
    /// <remarks>
    /// Numbering is an allocation and not a derivation, which is what makes this a mutation rather
    /// than a query: nothing anywhere declares that a monster dropped anything, so a number given
    /// out here cannot be worked out again and has to be remembered.
    /// <para>
    /// Answers what was numbered, or an empty list when the character is not in a run. The caller
    /// supplies how many pieces the room itself declares, because those hold the low numbers.
    /// </para>
    /// </remarks>
    IReadOnlyList<MonsterDrop> AllocateMonsterDrops(
        CharacterId characterId,
        int declaredDropCount,
        ushort actorSequence,
        IReadOnlyList<RolledDrop> rolled);

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

    /// <summary>Ends the run. False when the character was not in one.</summary>
    bool End(CharacterId characterId);
}
