using System.Collections.Immutable;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Pvp;

/// <summary>A duel room's identity, unique across restarts; the number a lobby shows is only a label.</summary>
public readonly record struct PvpRoomId(Guid Value)
{
    public static PvpRoomId New() => new(Guid.NewGuid());
}

/// <summary>One battle's identity, from its start until the room waits again; unique across restarts.</summary>
public readonly record struct PvpMatchId(Guid Value)
{
    public static PvpMatchId New() => new(Guid.NewGuid());
}

/// <summary>Where a room stands between being created and its next battle.</summary>
public enum PvpRoomPhase : byte
{
    Waiting,
    Loading,
    PeerLinking,
    Fighting,
    AwaitRank,
    AwaitResult,
}

/// <summary>How a room came to exist; every room of the first phase is opened by a player.</summary>
public enum PvpRoomOrigin : byte
{
    PlayerCreated,
}

/// <summary>The side a seated fighter is on.</summary>
/// <remarks>The values are the side numbers a room carries; an individual fighter is a side of their own.</remarks>
public enum PvpTeam : byte
{
    Individual = 0,
    First = 1,
    Second = 2,
}

/// <summary>One of a room's eight seats.</summary>
public abstract record PvpSeat
{
    private PvpSeat()
    {
    }

    public static PvpSeat OpenSeat { get; } = new Open();
    public static PvpSeat ClosedSeat { get; } = new Closed();

    /// <summary>Empty, and anyone may take it.</summary>
    public sealed record Open : PvpSeat;

    /// <summary>Empty, and closed by the owner.</summary>
    public sealed record Closed : PvpSeat;

    /// <param name="Session">The exact session that took the seat; a later session of the same character is not it.</param>
    public sealed record Taken(SelectedSessionIdentity Session, PvpTeam Team, bool Ready) : PvpSeat
    {
        public CharacterId CharacterId => Session.CharacterId;
    }
}

/// <summary>One fighter of a started battle, as frozen when it started.</summary>
public sealed record PvpFighter(int Seat, CharacterId CharacterId, SelectedSessionIdentity Session, PvpTeam Team);

/// <summary>What a battle mode keeps for itself while a battle runs; the first phase's modes keep nothing.</summary>
public abstract record PvpMatchModeState;

/// <summary>A started battle.</summary>
/// <param name="Fighters">Every seat that was taken when the battle started, frozen; later seat changes do not touch it.</param>
/// <param name="Map">The map index the battle is played on; never the random choice.</param>
/// <param name="PhaseDeadline">When the room's current phase is due to be advanced by the server.</param>
public sealed record PvpMatch(
    PvpMatchId Id,
    ImmutableArray<PvpFighter> Fighters,
    int Map,
    DateTimeOffset StartedAt,
    DateTimeOffset PhaseDeadline,
    PvpMatchModeState? ModeState = null);

/// <summary>A committed duel room, as everyone reads it.</summary>
/// <param name="Number">The number shown for the room on its listener; it is a label, not the identity.</param>
/// <param name="Channel">The duel channel the room was opened on.</param>
/// <param name="HasPassword">Whether entering needs a password. The password itself never leaves the room owner.</param>
/// <param name="MapIndex">The chosen map order index; 0 is the random choice.</param>
/// <param name="OwnerSeat">The seat of the room's owner.</param>
/// <param name="Seats">Always eight.</param>
/// <param name="Version">Advances by one with every committed change.</param>
/// <param name="Match">The battle under way, from its start until the room waits again.</param>
public sealed record PvpRoomSnapshot(
    PvpRoomId Id,
    ushort Number,
    ChannelEntryIdentity Channel,
    byte NameType,
    ImmutableArray<byte> Name,
    bool HasPassword,
    int MapIndex,
    PvpBattleMode Mode,
    int OwnerSeat,
    ImmutableArray<PvpSeat> Seats,
    PvpRoomPhase Phase,
    long Version,
    PvpRoomOrigin Origin,
    PvpMatch? Match)
{
    public const int SeatCount = 8;

    public int ListenerPort => Channel.ListenerPort;

    /// <summary>The taken seats in seat order.</summary>
    public IEnumerable<(int Seat, PvpSeat.Taken Member)> Members =>
        Seats.Select((seat, index) => (index, seat)).Where(x => x.seat is PvpSeat.Taken)
            .Select(x => (x.index, (PvpSeat.Taken)x.seat));

    public int MemberCount => Seats.Count(seat => seat is PvpSeat.Taken);

    public PvpSeat.Taken Owner => (PvpSeat.Taken)Seats[OwnerSeat];

    /// <summary>The seat the character holds here, or null.</summary>
    public int? SeatOf(CharacterId characterId)
    {
        for (var index = 0; index < Seats.Length; index++)
            if (Seats[index] is PvpSeat.Taken taken && taken.CharacterId == characterId)
                return index;
        return null;
    }
}

/// <summary>Why a room operation was refused. Wire error codes are the protocol's mapping of these.</summary>
public enum PvpRoomRefusal
{
    /// <summary>The acting session is no longer the one in the world.</summary>
    SessionNotCurrent,
    NotDuelChannel,
    InParty,
    AlreadySeated,
    NotSeated,
    RoomMissing,
    WrongPassword,
    InvalidPassword,
    Full,
    NotWaiting,
    NotOwner,
    UnsupportedMode,
    InvalidMap,
    InvalidSeat,
    InvalidTeam,
    /// <summary>Closing the seat would leave a lone member with no seat anyone could join.</summary>
    LastOpenSeat,
    TooFewMembers,
    NotAllReady,
    UnbalancedTeams,
    /// <summary>The invited character is not in the world on the inviter's listener.</summary>
    TargetUnavailable,
    TargetNotInLobby,
    TargetSeated,
    TargetInParty,
    /// <summary>Either side keeps the other on its blacklist.</summary>
    Blacklisted,
    InviteMissing,
    InviteExpired,
}

public enum PvpRoomChangeKind
{
    Created,
    Entered,
    /// <summary>Entered through an accepted invitation; the entry has no command reply of its own.</summary>
    InviteAccepted,
    TeamChanged,
    Left,
    Kicked,
    SeatClosed,
    SeatOpened,
    ModeChanged,
    MapChanged,
    ReadyChanged,
    MatchStarted,
}

public enum PvpSeatReleaseReason
{
    Left,
    Kicked,
}

/// <summary>A member taken off a seat by a change.</summary>
public sealed record PvpSeatRelease(int Seat, SelectedSessionIdentity Session, PvpSeatReleaseReason Reason);

/// <summary>The town moves a room change makes; they are made only when the change commits.</summary>
/// <remarks>
/// Entering a room takes a character out of its town area, leaving a room stands it back where it left.
/// Neither move can be taken back, so both wait for the room change's commit; the results are filled in
/// then and read by whoever projects the change, after the commit.
/// </remarks>
public interface IPvpTownMoves
{
    /// <summary>Whether the change committed and its moves were made.</summary>
    bool Applied { get; }

    IReadOnlyList<TownDepartureResult> Departures { get; }

    IReadOnlyList<TownReturnAdmission> Returns { get; }
}

/// <summary>What a room operation did.</summary>
public abstract record PvpRoomOutcome
{
    private PvpRoomOutcome()
    {
    }

    public sealed record Refused(PvpRoomRefusal Reason) : PvpRoomOutcome;

    /// <param name="Before">The room before the change; null when the change created it.</param>
    /// <param name="After">The room after the change; null when the change destroyed it.</param>
    /// <param name="Released">Members taken off their seats by this change.</param>
    public sealed record Changed(
        PvpRoomChangeKind Kind,
        PvpRoomSnapshot? Before,
        PvpRoomSnapshot? After,
        IReadOnlyList<PvpSeatRelease> Released,
        IPvpTownMoves Town) : PvpRoomOutcome;
}

/// <summary>An invitation into a waiting room.</summary>
public sealed record PvpInvitation(
    Guid Token,
    PvpRoomId RoomId,
    SelectedSessionIdentity Inviter,
    SelectedSessionIdentity Invitee,
    DateTimeOffset ExpiresAt);

public abstract record PvpInviteOutcome
{
    private PvpInviteOutcome()
    {
    }

    public sealed record Refused(PvpRoomRefusal Reason) : PvpInviteOutcome;

    public sealed record Sent(PvpInvitation Invitation, PvpRoomSnapshot Room) : PvpInviteOutcome;
}

/// <summary>What creating a room asks for.</summary>
/// <param name="Password">Null for an open room.</param>
public sealed record PvpRoomCreation(
    SelectedSessionIdentity Owner,
    byte NameType,
    ImmutableArray<byte> Name,
    ImmutableArray<byte>? Password,
    int MapIndex,
    PvpBattleMode Mode);

public enum PvpSeatAction
{
    /// <summary>The member moves to another team.</summary>
    ChangeTeam,
    /// <summary>The owner takes another member off their seat.</summary>
    Kick,
    /// <summary>The member leaves the room.</summary>
    Leave,
    /// <summary>The owner closes an open seat.</summary>
    Close,
    /// <summary>The owner opens a closed seat.</summary>
    Open,
}

/// <summary>The duel rooms, as everyone else reads them.</summary>
/// <remarks>Reads inside a writable use case see that use case's own uncommitted changes.</remarks>
public interface IPvpRoomQueryDomainService
{
    /// <summary>The committed rooms on a listener, by number.</summary>
    IReadOnlyList<PvpRoomSnapshot> RoomsOn(int listenerPort);

    PvpRoomSnapshot? Find(PvpRoomId id);

    /// <summary>The room the character holds a seat in, or null.</summary>
    PvpRoomSnapshot? FindSeated(CharacterId characterId);

    bool IsSeated(CharacterId characterId);
}

/// <summary>The single owner of duel rooms and of invitations into them.</summary>
/// <remarks>
/// Every write joins the caller's writable transaction and refuses without one; nothing it stages is
/// visible to anyone else, and no town move is made, until that transaction commits.
/// </remarks>
public interface IPvpRoomDomainService : IPvpRoomQueryDomainService
{
    ValueTask<PvpRoomOutcome> CreateAsync(PvpRoomCreation request, CancellationToken cancellationToken);

    /// <param name="password">The password offered, or null when none was.</param>
    ValueTask<PvpRoomOutcome> EnterAsync(
        SelectedSessionIdentity session, ushort number, ImmutableArray<byte>? password, CancellationToken cancellationToken);

    /// <param name="seat">The seat acted on: the actor's own for a team change or leaving.</param>
    /// <param name="team">The team to move to; only read for <see cref="PvpSeatAction.ChangeTeam"/>.</param>
    ValueTask<PvpRoomOutcome> SetSeatAsync(
        SelectedSessionIdentity actor, int seat, PvpSeatAction action, PvpTeam team, CancellationToken cancellationToken);

    ValueTask<PvpRoomOutcome> SetModeAsync(
        SelectedSessionIdentity actor, PvpBattleMode mode, CancellationToken cancellationToken);

    ValueTask<PvpRoomOutcome> SetMapAsync(
        SelectedSessionIdentity actor, int mapIndex, CancellationToken cancellationToken);

    /// <summary>
    /// A member marks themselves ready or not; the owner marking ready starts the battle when the room
    /// allows it, freezing the fighters and entering <see cref="PvpRoomPhase.Loading"/>.
    /// </summary>
    ValueTask<PvpRoomOutcome> SetReadyAsync(
        SelectedSessionIdentity actor, bool ready, CancellationToken cancellationToken);

    ValueTask<PvpInviteOutcome> InviteAsync(
        SelectedSessionIdentity inviter, SelectedSessionIdentity invitee, CancellationToken cancellationToken);

    /// <summary>The invitee accepts; the room is entered as <see cref="EnterAsync"/> would, without a password.</summary>
    ValueTask<PvpRoomOutcome> AcceptInviteAsync(
        SelectedSessionIdentity invitee, CharacterId inviter, CancellationToken cancellationToken);
}

/// <summary>Wakes the duel deadline scheduler to rescan the rooms' deadlines.</summary>
public interface IPvpDeadlineWake
{
    void Wake();
}

/// <summary>
/// Checks, before any listener starts, that every duel channel the server carries has entitlements and that
/// the duel content loads. Any failure fails startup.
/// </summary>
public interface IPvpStartupVerification
{
    /// <returns>The number of duel channels checked.</returns>
    ValueTask<int> VerifyAsync(CancellationToken cancellationToken);
}
