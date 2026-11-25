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
    /// <summary>
    /// From the start: 70Reference <c>start_pvp</c> sets every fighter fighting at once, and an A21 client in an
    /// ordinary room reports neither loading nor linking (spec SA-1).
    /// </summary>
    Fighting,
    AwaitRank,
    AwaitResult,
}

/// <summary>How a room came to exist.</summary>
public enum PvpRoomOrigin : byte
{
    PlayerCreated,
    Matchmaking,
}

/// <summary>The side a seated fighter is on.</summary>
/// <remarks>The values are the side numbers a room carries; an individual fighter is a side of their own.</remarks>
public enum PvpTeam : byte
{
    Individual = 0,
    First = 1,
    Second = 2,
}

public enum PvpObservation : byte
{
    Standard = 3,
    Alternate = 4,
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
        public PvpObservation? Observation { get; init; }
    }
}

/// <summary>One fighter of a started battle.</summary>
/// <remarks>
/// Who fights, from which seat, on which side and through which session is frozen when the battle starts. What
/// the fighter has done since changes with the battle, and stops changing once it ends.
/// </remarks>
public sealed record PvpFighter(int Seat, CharacterId CharacterId, SelectedSessionIdentity Session, PvpTeam Team)
{
    public bool Alive { get; init; } = true;
    public int Kills { get; init; }
    public int Deaths { get; init; }

    /// <summary>Left the battle before it ended; settles as a loss whatever the result.</summary>
    public bool Fled { get; init; }

    /// <summary>No longer takes part: the battle neither waits for its reports nor reads its heartbeat.</summary>
    public bool Departed { get; init; }

    public PvpFighterReports Reports { get; init; }

    /// <summary>The last heartbeat the fighter sent while fighting; set when the battle starts.</summary>
    public DateTimeOffset LastHeartbeat { get; init; }
}

/// <summary>What a battle mode keeps for itself while a battle runs; the first phase's modes keep nothing.</summary>
public abstract record PvpMatchModeState;

/// <summary>Current fighters and the next scheduled turn in a relay battle.</summary>
public sealed record PvpRelayBattle(int? FirstSeat, int? SecondSeat, DateTimeOffset? TurnDue,
    ImmutableHashSet<int> Requests) : PvpMatchModeState
{
    public bool IsActive(int seat) => seat == FirstSeat || seat == SecondSeat;
}

/// <summary>A started battle.</summary>
/// <param name="Fighters">The competing seats at the start; later seat changes do not touch this roster.</param>
/// <param name="Map">The map index the battle is played on; never the random choice.</param>
/// <param name="PhaseDeadline">When the room's current phase is due to be advanced by the server.</param>
public sealed record PvpMatch(
    PvpMatchId Id,
    ImmutableArray<PvpFighter> Fighters,
    int Map,
    DateTimeOffset StartedAt,
    DateTimeOffset PhaseDeadline,
    PvpMatchModeState? ModeState = null)
{
    /// <summary>The battle's result, frozen when it ended; null while it runs.</summary>
    public PvpMatchTerminal? Terminal { get; init; }

    /// <summary>Every fighter's settled result, once settlement wrote them; empty before.</summary>
    public ImmutableArray<PvpFighterResult> Results { get; init; } = [];

    public ImmutableHashSet<int> ObserverResultAcknowledgements { get; init; } = [];

    public ImmutableDictionary<int, ImmutableDictionary<ushort, int>> DailyHealthReports { get; init; } =
        ImmutableDictionary<int, ImmutableDictionary<ushort, int>>.Empty;

    public ImmutableDictionary<int, int> DailyColorlessSkills { get; init; } = ImmutableDictionary<int, int>.Empty;
    public ImmutableDictionary<int, byte> DailyWinPoses { get; init; } = ImmutableDictionary<int, byte>.Empty;

    /// <summary>The index of this exact session's fighter, if it is one.</summary>
    public int? FighterOf(SelectedSessionIdentity session)
    {
        for (var index = 0; index < Fighters.Length; index++)
            if (Fighters[index].Session == session)
                return index;
        return null;
    }
}

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

    public PvpDuelBusiness Business => PvpDuelBusinessRoute.Of(Channel, Origin, Mode);

    public int ListenerPort => Channel.ListenerPort;

    public bool IsPractice => Mode == PvpBattleMode.Practice;
    public bool IsMatchmaking => Origin == PvpRoomOrigin.Matchmaking;
    public bool IsPublicLobbyRoom => !IsPractice && !IsMatchmaking;
    public PvpMatchAdmission? Admission { get; init; }
    public ImmutableDictionary<int, Guid> ColosseumRuns { get; init; } = ImmutableDictionary<int, Guid>.Empty;

    /// <summary>The taken seats in seat order.</summary>
    public IEnumerable<(int Seat, PvpSeat.Taken Member)> Members =>
        Seats.Select((seat, index) => (index, seat)).Where(x => x.seat is PvpSeat.Taken)
            .Select(x => (x.index, (PvpSeat.Taken)x.seat));

    public IEnumerable<(int Seat, PvpSeat.Taken Member)> PlayingMembers =>
        Members.Where(entry => entry.Member.Observation is null);

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
    /// <summary>The room is not in the battle phase the report belongs to: a late or early report.</summary>
    WrongPhase,
    /// <summary>The session is not a fighter still taking part in the room's battle.</summary>
    NotFighter,
    /// <summary>The fighter reporting its death is already dead.</summary>
    NotAlive,
    /// <summary>A time-out reported before the battle's time limit, less the allowed lead, had passed.</summary>
    TooEarly,
    AlreadyReported,
    /// <summary>The server is stopping: no battle starts any more.</summary>
    ShuttingDown,
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
    Heartbeat,
    RelayRequestChanged,
    TotalMatchChoiceChanged,
    Died,
    TimedOut,
    RankAcknowledged,
    /// <summary>The battle's results were written; the fighters wait on the result screen.</summary>
    Settled,
    ResultAcknowledged,
    /// <summary>A member's session ended, or a fighter stopped sending heartbeats.</summary>
    Departed,
    /// <summary>The server advanced a phase whose deadline passed.</summary>
    DeadlinePassed,
    CancelledForShutdown,
    /// <summary>Settlement kept failing past the give-up time; the battle is dropped unsettled and the room waits again.</summary>
    SettlementAbandoned,
    MatchPaired,
    MatchAdmissionChanged,
    MatchAdmissionCancelled,
}

public enum PvpSeatReleaseReason
{
    Left,
    Kicked,
    /// <summary>The member's session ended; nobody is sent back to town.</summary>
    Departed,
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

    public sealed record Ignored : PvpRoomOutcome;

    /// <param name="Before">The room before the change; null when the change created it.</param>
    /// <param name="After">The room after the change; null when the change destroyed it.</param>
    /// <param name="Released">Members taken off their seats by this change.</param>
    /// <remarks>
    /// Phase changes are read from <see cref="Before"/> and <see cref="After"/>: a battle that started, was
    /// cancelled, ended or went back to waiting shows as their phases and matches differing.
    /// </remarks>
    public sealed record Changed(
        PvpRoomChangeKind Kind,
        PvpRoomSnapshot? Before,
        PvpRoomSnapshot? After,
        IReadOnlyList<PvpSeatRelease> Released,
        IPvpTownMoves Town) : PvpRoomOutcome
    {
        /// <summary>The death this change recorded, if it recorded one.</summary>
        public PvpDeath? Death { get; init; }

        /// <summary>The requester whose existing ready flag must be confirmed without a seat change.</summary>
        public int? ReadyConfirmationSeat { get; init; }

        public int? RelayRequestSeat { get; init; }

        /// <summary>The battle end this change froze; it outlives the room when the room is gone.</summary>
        public PvpMatchConclusion? Concluded { get; init; }

        /// <summary>
        /// An ended battle nobody is left to acknowledge: the caller settles it in the same transaction through
        /// <see cref="IPvpSettlementDomainService"/>. Left unsettled, the deadline scheduler settles it.
        /// </summary>
        public PvpMatchId? SettlementDue { get; init; }
    }
}

/// <summary>An invitation into a waiting room.</summary>
public sealed record PvpInvitation(
    Guid Token,
    PvpRoomId RoomId,
    SelectedSessionIdentity Inviter,
    SelectedSessionIdentity Invitee,
    DateTimeOffset ExpiresAt)
{
    /// <summary>The peer value the inviter's client sent with the invitation; the invitee's acceptance echoes it.</summary>
    public int PeerToken { get; init; }
}

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
    Observe,
    ObserveAlternate,
}

/// <summary>The duel rooms, as everyone else reads them.</summary>
/// <remarks>Reads inside a writable use case see that use case's own uncommitted changes.</remarks>
public interface IPvpRoomQueryDomainService
{
    /// <summary>The committed public lobby rooms on a listener, by number.</summary>
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

    ValueTask<PvpRoomOutcome> QuickJoinAsync(SelectedSessionIdentity session, CancellationToken cancellationToken);

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
    /// allows it, freezing the fighters and entering <see cref="PvpRoomPhase.Fighting"/>.
    /// </summary>
    ValueTask<PvpRoomOutcome> SetReadyAsync(
        SelectedSessionIdentity actor, bool ready, CancellationToken cancellationToken);

    /// <param name="peerToken">The peer value the inviter's client sent; an acceptance must echo it.</param>
    ValueTask<PvpInviteOutcome> InviteAsync(
        SelectedSessionIdentity inviter, SelectedSessionIdentity invitee, CancellationToken cancellationToken, int peerToken = 0);

    /// <summary>The invitee accepts; the room is entered as <see cref="EnterAsync"/> would, without a password.</summary>
    /// <param name="peerToken">
    /// The peer value the acceptance echoed; one that is not the invitation's finds no invitation. Null skips the check.
    /// </param>
    ValueTask<PvpRoomOutcome> AcceptInviteAsync(
        SelectedSessionIdentity invitee, CharacterId inviter, CancellationToken cancellationToken, int? peerToken = null);

    ValueTask<PvpRoomOutcome> HeartbeatAsync(SelectedSessionIdentity session, CancellationToken cancellationToken);

    /// <summary>A living reserve fighter toggles the request to be selected for the next relay turn.</summary>
    ValueTask<PvpRoomOutcome> RequestRelayFightAsync(SelectedSessionIdentity session, CancellationToken cancellationToken);

    ValueTask<PvpRoomOutcome> SelectTotalMatchCandidateAsync(
        SelectedSessionIdentity session, byte ordinal, CancellationToken cancellationToken);

    /// <summary>
    /// The reporting fighter died. The kill counts only for a killer of this battle who is alive and on another
    /// side; the battle ends when its mode says nobody is left to fight.
    /// </summary>
    /// <param name="killer">The character the client names as the killer, if any.</param>
    ValueTask<PvpRoomOutcome> ReportDeathAsync(
        SelectedSessionIdentity session, CharacterId? killer, CancellationToken cancellationToken);

    /// <summary>
    /// A fighter reports that an ordinary battle ran out of time. The first report once the time limit is close
    /// enough ends the battle in a draw. Relay turns time out through the client's death reports.
    /// </summary>
    /// <param name="clientValues">What the client sent with it; only logged.</param>
    ValueTask<PvpRoomOutcome> ReportTimeoutAsync(
        SelectedSessionIdentity session, ImmutableArray<byte> clientValues, CancellationToken cancellationToken);

    /// <summary>A fighter saw the ranking; once nobody is left to see it, the change asks for settlement.</summary>
    ValueTask<PvpRoomOutcome> AcknowledgeRankAsync(SelectedSessionIdentity session, CancellationToken cancellationToken);

    /// <summary>A fighter closed the result screen; once everyone has, the room waits again.</summary>
    ValueTask<PvpRoomOutcome> AcknowledgeResultAsync(SelectedSessionIdentity session, CancellationToken cancellationToken);

    /// <summary>
    /// A member leaves, by how its battle stands: a fight (from the start on) counts it dead and fled, the
    /// ranking and result screens stop waiting for it, a waiting room lets it go. The member
    /// gives up its seat; a fighter that only stopped sending heartbeats is the deadline's, not a departure.
    /// </summary>
    ValueTask<PvpRoomOutcome> DepartAsync(
        SelectedSessionIdentity session, PvpDepartureReason reason, CancellationToken cancellationToken);

    /// <summary>The rooms and settlements whose deadlines have passed, and the next deadline after them.</summary>
    PvpDeadlineScan Deadlines(DateTimeOffset now);

    /// <summary>Advances one room whose deadline has passed; null when nothing in it is due.</summary>
    ValueTask<PvpRoomOutcome.Changed?> AdvanceDueAsync(
        PvpRoomId room, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The ended battles not settled yet, whether or not their rooms still stand.</summary>
    /// <summary>Records daily HP threshold reports from the selected fighter of an active ranked match.</summary>
    bool RecordDailyHealth(SelectedSessionIdentity session, IReadOnlyList<ushort> missionIds);
    bool RecordDailyWinPose(SelectedSessionIdentity session, ushort missionId, byte ordinal);
    ValueTask<bool> RecordDailySkillAsync(SelectedSessionIdentity session, ushort skillId, CancellationToken cancellationToken);

    IReadOnlyList<PvpMatchConclusion> PendingConclusions();

    /// <summary>
    /// Cancels every battle that has not ended and returns its room to waiting; ended battles are left to
    /// settlement. Nobody's record changes, and from this commit on no battle starts.
    /// </summary>
    ValueTask<IReadOnlyList<PvpRoomOutcome.Changed>> CancelForShutdownAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Drops an ended battle whose settlement kept failing past the give-up time: nobody's record changes, and its
    /// room, when it still waits on that battle, waits again. Null when the battle is no longer waiting.
    /// </summary>
    ValueTask<PvpRoomOutcome.Changed?> AbandonSettlementAsync(PvpMatchId match, CancellationToken cancellationToken);
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
