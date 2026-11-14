using System.Collections.Immutable;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Pvp;

/// <summary>The duel rooms and the invitations into them, committed with the caller's transaction.</summary>
/// <remarks>
/// Every write is staged in this transaction's <see cref="Changes"/>: reads inside the same writable
/// transaction see it, nobody else does, and it is applied only when the transaction commits -- so a
/// database write that fails in the same use case leaves the rooms, their numbers and the invitations as
/// they were. The town moves a change implies are made at that commit too, never before it.
/// </remarks>
internal sealed class PvpRoomDomainService(
    IDomainTransactionAccessor transactions,
    ISelectedSessionDomainService sessions,
    IChannelEntryDomainService channels,
    IPartyService parties,
    ICharacterRelationQueryDomainService relations,
    IPvpLobbyDomainService lobby,
    ITownTransitDomainService transit,
    IOnlineCharacterRuntimeDomainService online,
    IPvpRoomRuleService rules,
    IPvpSettlementRuleService battles,
    IPvpDeadlineWake deadlines,
    TimeProvider clock,
    ILogger<PvpRoomDomainService> logger) : IPvpRoomDomainService
{
    // A due item still waiting after a failed attempt is retried after a quarter of how long it has been
    // overdue, within these bounds: neither forgotten nor spun on.
    private static readonly TimeSpan RetryFloor = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryCeiling = TimeSpan.FromSeconds(30);

    /// <summary>Set when shutdown cancelled the running battles: from then on no battle starts.</summary>
    private bool closing;

    private readonly Lock gate = new();
    private readonly Dictionary<PvpRoomId, Room> rooms = [];
    private readonly Dictionary<CharacterId, PvpRoomId> seated = [];
    private readonly Dictionary<InvitationKey, PvpInvitation> invitations = [];
    private readonly Dictionary<PvpMatchId, PvpMatchConclusion> conclusions = [];

    /// <summary>A committed room and the password only this owner compares.</summary>
    private sealed record Room(PvpRoomSnapshot Snapshot, ImmutableArray<byte>? Password);

    private readonly record struct InvitationKey(CharacterId Inviter, CharacterId Invitee);

    private Changes Pending
    {
        get
        {
            var transaction = transactions.Current
                ?? throw new InvalidOperationException("Duel room changes require the caller's transaction.");
            if (transaction.IsReadOnly)
                throw new InvalidOperationException("Duel room changes require a writable transaction.");
            return transaction.Enlist(this, () => new Changes(this));
        }
    }

    private Changes? Staged => transactions.Current is { IsReadOnly: false } ? Pending : null;

    public IReadOnlyList<PvpRoomSnapshot> RoomsOn(int listenerPort)
    {
        lock (gate)
            return [.. rooms.Values.Select(room => room.Snapshot).Where(room => room.ListenerPort == listenerPort)
                .OrderBy(room => room.Number)];
    }

    public PvpRoomSnapshot? Find(PvpRoomId id)
    {
        if (Staged is { } staged) return staged.Find(id)?.Snapshot;
        lock (gate) return rooms.GetValueOrDefault(id)?.Snapshot;
    }

    public PvpRoomSnapshot? FindSeated(CharacterId characterId)
    {
        if (Staged is { } staged) return staged.FindSeated(characterId)?.Snapshot;
        lock (gate) return seated.TryGetValue(characterId, out var id) ? rooms[id].Snapshot : null;
    }

    public bool IsSeated(CharacterId characterId) => FindSeated(characterId) is not null;

    public async ValueTask<PvpRoomOutcome> CreateAsync(PvpRoomCreation request, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(CreateAsync));
        ArgumentNullException.ThrowIfNull(request);
        var pending = Pending;
        var owner = request.Owner;
        if (!sessions.IsInWorld(owner)) return Refuse(PvpRoomRefusal.SessionNotCurrent);
        var channel = await channels.ResolveAsync(owner.ListenerPort, cancellationToken).ConfigureAwait(false);
        if (!channel.IsPvp) return Refuse(PvpRoomRefusal.NotDuelChannel);
        if (!Enum.IsDefined(request.Mode) || !PvpBattleModeCapabilities.Of(request.Mode).Supported)
            return Refuse(PvpRoomRefusal.UnsupportedMode);
        if (!rules.AcceptsMap(request.MapIndex)) return Refuse(PvpRoomRefusal.InvalidMap);
        var password = request.Password is { Length: > 0 } offered ? offered : (ImmutableArray<byte>?)null;
        if (password is { } chosen && !rules.AcceptsPassword(chosen)) return Refuse(PvpRoomRefusal.InvalidPassword);
        if (pending.FindSeated(owner.CharacterId) is not null) return Refuse(PvpRoomRefusal.AlreadySeated);
        if (InParty(owner.CharacterId)) return Refuse(PvpRoomRefusal.InParty);

        var seats = Enumerable.Repeat(PvpSeat.OpenSeat, PvpRoomSnapshot.SeatCount).ToImmutableArray()
            .SetItem(0, new PvpSeat.Taken(owner, rules.TeamForNewcomer(request.Mode, []), Ready: false));
        var snapshot = new PvpRoomSnapshot(
            PvpRoomId.New(), pending.NextNumber(channel.ListenerPort), channel, request.NameType,
            request.Name.IsDefault ? [] : request.Name,
            password is not null, request.MapIndex, request.Mode, OwnerSeat: 0, seats, PvpRoomPhase.Waiting,
            Version: 0, PvpRoomOrigin.PlayerCreated, Match: null);
        var town = new TownMoves(channel.ChannelId, leaving: [owner], returning: []);
        var created = pending.Put(snapshot.Id, null, new Room(snapshot, password), town)!;
        return new PvpRoomOutcome.Changed(PvpRoomChangeKind.Created, null, created.Snapshot, [], town);
    }

    public ValueTask<PvpRoomOutcome> EnterAsync(
        SelectedSessionIdentity session, ushort number, ImmutableArray<byte>? password, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(EnterAsync));
        ArgumentNullException.ThrowIfNull(session);
        var pending = Pending;
        if (!sessions.IsInWorld(session)) return Refused(PvpRoomRefusal.SessionNotCurrent);
        if (pending.FindByNumber(session.ListenerPort, number) is not { } room) return Refused(PvpRoomRefusal.RoomMissing);
        return ValueTask.FromResult(Admit(pending, room, session, PvpRoomChangeKind.Entered, password, checkPassword: true));
    }

    public ValueTask<PvpRoomOutcome> SetSeatAsync(
        SelectedSessionIdentity actor, int seat, PvpSeatAction action, PvpTeam team, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(SetSeatAsync));
        ArgumentNullException.ThrowIfNull(actor);
        var pending = Pending;
        if (Acting(pending, actor) is not (var room, var own)) return Refused(ActingRefusal(actor));
        var snapshot = room.Snapshot;
        if (seat is < 0 or >= PvpRoomSnapshot.SeatCount) return Refused(PvpRoomRefusal.InvalidSeat);
        if (snapshot.Phase != PvpRoomPhase.Waiting) return Refused(PvpRoomRefusal.NotWaiting);
        var isOwner = own == snapshot.OwnerSeat;
        var target = snapshot.Seats[seat];
        switch (action)
        {
            case PvpSeatAction.ChangeTeam:
                if (seat != own) return Refused(PvpRoomRefusal.InvalidSeat);
                if (!rules.AcceptsTeam(snapshot.Mode, team)) return Refused(PvpRoomRefusal.InvalidTeam);
                return Changed(pending, room, snapshot with
                {
                    Seats = snapshot.Seats.SetItem(seat, (PvpSeat.Taken)target with { Team = team }),
                }, PvpRoomChangeKind.TeamChanged);
            case PvpSeatAction.Leave:
                if (seat != own) return Refused(PvpRoomRefusal.InvalidSeat);
                return ValueTask.FromResult(Release(pending, room, seat, PvpSeatReleaseReason.Left));
            case PvpSeatAction.Kick:
                if (!isOwner) return Refused(PvpRoomRefusal.NotOwner);
                if (seat == own || target is not PvpSeat.Taken) return Refused(PvpRoomRefusal.InvalidSeat);
                return ValueTask.FromResult(Release(pending, room, seat, PvpSeatReleaseReason.Kicked));
            case PvpSeatAction.Close:
                if (!isOwner) return Refused(PvpRoomRefusal.NotOwner);
                if (target is not PvpSeat.Open) return Refused(PvpRoomRefusal.InvalidSeat);
                if (!rules.AllowsClosing(snapshot.MemberCount, snapshot.Seats.Count(x => x is PvpSeat.Open)))
                    return Refused(PvpRoomRefusal.LastOpenSeat);
                return Changed(pending, room, snapshot with { Seats = snapshot.Seats.SetItem(seat, PvpSeat.ClosedSeat) },
                    PvpRoomChangeKind.SeatClosed);
            case PvpSeatAction.Open:
                if (!isOwner) return Refused(PvpRoomRefusal.NotOwner);
                if (target is not PvpSeat.Closed) return Refused(PvpRoomRefusal.InvalidSeat);
                return Changed(pending, room, snapshot with { Seats = snapshot.Seats.SetItem(seat, PvpSeat.OpenSeat) },
                    PvpRoomChangeKind.SeatOpened);
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    public ValueTask<PvpRoomOutcome> SetModeAsync(
        SelectedSessionIdentity actor, PvpBattleMode mode, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(SetModeAsync));
        ArgumentNullException.ThrowIfNull(actor);
        var pending = Pending;
        if (OwnerActing(pending, actor) is not { } room) return Refused(OwnerRefusal(pending, actor));
        var snapshot = room.Snapshot;
        if (snapshot.Phase != PvpRoomPhase.Waiting) return Refused(PvpRoomRefusal.NotWaiting);
        if (!Enum.IsDefined(mode) || !PvpBattleModeCapabilities.Of(mode).Supported) return Refused(PvpRoomRefusal.UnsupportedMode);
        var teams = rules.Reassign(mode, snapshot.MemberCount);
        var seats = snapshot.Seats;
        var next = 0;
        foreach (var (index, member) in snapshot.Members)
            seats = seats.SetItem(index, member with { Team = teams[next++] });
        return Changed(pending, room, snapshot with { Mode = mode, Seats = seats }, PvpRoomChangeKind.ModeChanged);
    }

    public ValueTask<PvpRoomOutcome> SetMapAsync(
        SelectedSessionIdentity actor, int mapIndex, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(SetMapAsync));
        ArgumentNullException.ThrowIfNull(actor);
        var pending = Pending;
        if (OwnerActing(pending, actor) is not { } room) return Refused(OwnerRefusal(pending, actor));
        if (room.Snapshot.Phase != PvpRoomPhase.Waiting) return Refused(PvpRoomRefusal.NotWaiting);
        if (!rules.AcceptsMap(mapIndex)) return Refused(PvpRoomRefusal.InvalidMap);
        return Changed(pending, room, room.Snapshot with { MapIndex = mapIndex }, PvpRoomChangeKind.MapChanged);
    }

    public ValueTask<PvpRoomOutcome> SetReadyAsync(
        SelectedSessionIdentity actor, bool ready, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(SetReadyAsync));
        ArgumentNullException.ThrowIfNull(actor);
        var pending = Pending;
        if (Acting(pending, actor) is not (var room, var own)) return Refused(ActingRefusal(actor));
        var snapshot = room.Snapshot;
        if (snapshot.Phase != PvpRoomPhase.Waiting) return Refused(PvpRoomRefusal.NotWaiting);
        var self = (PvpSeat.Taken)snapshot.Seats[own];
        if (own != snapshot.OwnerSeat || !ready)
            return Changed(pending, room, snapshot with { Seats = snapshot.Seats.SetItem(own, self with { Ready = ready }) },
                PvpRoomChangeKind.ReadyChanged);

        var members = snapshot.Members.ToArray();
        // Spec RQ-042: a battle started after shutdown cancelled the others could not end before the connections go.
        if (pending.Closing) return Refused(PvpRoomRefusal.ShuttingDown);
        if (rules.RefuseStart(snapshot.Mode, [.. members.Select(x => new PvpRoomMemberFacts(
                x.Member.Team, x.Member.Ready, x.Seat == snapshot.OwnerSeat))]) is { } refusal)
            return Refused(refusal);

        var match = PvpMatchId.New();
        var startedAt = clock.GetUtcNow();
        return Changed(pending, room, snapshot with
        {
            Seats = snapshot.Seats.SetItem(own, self with { Ready = true }),
            Phase = PvpRoomPhase.Loading,
            Match = new PvpMatch(
                match,
                [.. members.Select(x => new PvpFighter(x.Seat, x.Member.CharacterId, x.Member.Session, x.Member.Team))],
                rules.StartMap(snapshot.MapIndex, match.Value.GetHashCode()),
                startedAt,
                startedAt + rules.LoadingTimeout),
        }, PvpRoomChangeKind.MatchStarted);
    }

    public async ValueTask<PvpInviteOutcome> InviteAsync(
        SelectedSessionIdentity inviter, SelectedSessionIdentity invitee, CancellationToken cancellationToken, int peerToken = 0)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(InviteAsync));
        ArgumentNullException.ThrowIfNull(inviter);
        ArgumentNullException.ThrowIfNull(invitee);
        var pending = Pending;
        if (Acting(pending, inviter) is not (var room, _)) return new PvpInviteOutcome.Refused(ActingRefusal(inviter));
        if (room.Snapshot.Phase != PvpRoomPhase.Waiting) return new PvpInviteOutcome.Refused(PvpRoomRefusal.NotWaiting);
        if (invitee.CharacterId == inviter.CharacterId || invitee.ListenerPort != inviter.ListenerPort
            || !sessions.IsInWorld(invitee))
            return new PvpInviteOutcome.Refused(PvpRoomRefusal.TargetUnavailable);
        if (pending.FindSeated(invitee.CharacterId) is not null) return new PvpInviteOutcome.Refused(PvpRoomRefusal.TargetSeated);
        if (InParty(invitee.CharacterId)) return new PvpInviteOutcome.Refused(PvpRoomRefusal.TargetInParty);
        if (!lobby.ReadyOn(invitee.ListenerPort, inviter.CharacterId)
                .Any(x => x.CharacterId == invitee.CharacterId && (x.Session is null || x.Session == invitee)))
            return new PvpInviteOutcome.Refused(PvpRoomRefusal.TargetNotInLobby);
        if (!room.Snapshot.Seats.Any(seat => seat is PvpSeat.Open)) return new PvpInviteOutcome.Refused(PvpRoomRefusal.Full);
        if (await relations.ExistsAsync(inviter.CharacterId, invitee.CharacterId, CharacterRelationKind.Blacklist, cancellationToken)
                .ConfigureAwait(false)
            || await relations.ExistsAsync(invitee.CharacterId, inviter.CharacterId, CharacterRelationKind.Blacklist, cancellationToken)
                .ConfigureAwait(false))
            return new PvpInviteOutcome.Refused(PvpRoomRefusal.Blacklisted);

        var invitation = new PvpInvitation(Guid.NewGuid(), room.Snapshot.Id, inviter, invitee,
            clock.GetUtcNow() + rules.InvitationLifetime) { PeerToken = peerToken };
        pending.PutInvitation(new(inviter.CharacterId, invitee.CharacterId), invitation);
        return new PvpInviteOutcome.Sent(invitation, room.Snapshot);
    }

    public ValueTask<PvpRoomOutcome> AcceptInviteAsync(
        SelectedSessionIdentity invitee, CharacterId inviter, CancellationToken cancellationToken, int? peerToken = null)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(AcceptInviteAsync));
        ArgumentNullException.ThrowIfNull(invitee);
        var pending = Pending;
        if (!sessions.IsInWorld(invitee)) return Refused(PvpRoomRefusal.SessionNotCurrent);
        var key = new InvitationKey(inviter, invitee.CharacterId);
        if (pending.FindInvitation(key) is not { } invitation) return Refused(PvpRoomRefusal.InviteMissing);
        // Upstream HandleRoomInviteResponseAsync: an acceptance echoing another token answers no invitation, and
        // leaves the one there is standing.
        if (peerToken is { } echoed && echoed != invitation.PeerToken) return Refused(PvpRoomRefusal.InviteMissing);
        if (clock.GetUtcNow() >= invitation.ExpiresAt)
        {
            pending.PutInvitation(key, null);
            return Refused(PvpRoomRefusal.InviteExpired);
        }
        // Both sides must still be the exact sessions the invitation was made between.
        if (invitation.Invitee != invitee
            || !sessions.TryGetInWorld(inviter, out var current) || current != invitation.Inviter)
            return Refused(PvpRoomRefusal.SessionNotCurrent);
        if (pending.Find(invitation.RoomId) is not { } room) return Refused(PvpRoomRefusal.RoomMissing);
        if (room.Snapshot.SeatOf(inviter) is not { } seat
            || ((PvpSeat.Taken)room.Snapshot.Seats[seat]).Session != invitation.Inviter)
            return Refused(PvpRoomRefusal.NotSeated);
        var outcome = Admit(pending, room, invitee, PvpRoomChangeKind.InviteAccepted, null, checkPassword: false);
        if (outcome is PvpRoomOutcome.Changed) pending.PutInvitation(key, null);
        return ValueTask.FromResult(outcome);
    }

    private PvpRoomOutcome Admit(
        Changes pending, Room room, SelectedSessionIdentity session, PvpRoomChangeKind kind,
        ImmutableArray<byte>? password, bool checkPassword)
    {
        if (pending.FindSeated(session.CharacterId) is not null) return Refuse(PvpRoomRefusal.AlreadySeated);
        if (InParty(session.CharacterId)) return Refuse(PvpRoomRefusal.InParty);
        var snapshot = room.Snapshot;
        if (snapshot.Phase != PvpRoomPhase.Waiting) return Refuse(PvpRoomRefusal.NotWaiting);
        if (checkPassword && room.Password is { } expected
            && !(password is { } offered && offered.AsSpan().SequenceEqual(expected.AsSpan())))
            return Refuse(PvpRoomRefusal.WrongPassword);
        var seat = snapshot.Seats.IndexOf(PvpSeat.OpenSeat);
        if (seat < 0) return Refuse(PvpRoomRefusal.Full);
        var team = rules.TeamForNewcomer(snapshot.Mode, [.. snapshot.Members.Select(x => x.Member.Team)]);
        var town = new TownMoves(snapshot.Channel.ChannelId, leaving: [session], returning: []);
        var after = pending.Put(snapshot.Id, room,
            room with { Snapshot = snapshot with { Seats = snapshot.Seats.SetItem(seat, new PvpSeat.Taken(session, team, false)) } },
            town)!;
        return new PvpRoomOutcome.Changed(kind, snapshot, after.Snapshot, [], town);
    }

    /// <summary>Takes a member off a seat; the owner's seat passes on, and the last one out ends the room.</summary>
    private PvpRoomOutcome Release(Changes pending, Room room, int seat, PvpSeatReleaseReason reason) =>
        Stage(pending, room, room.Snapshot,
            reason == PvpSeatReleaseReason.Left ? PvpRoomChangeKind.Left : PvpRoomChangeKind.Kicked, (seat, reason));

    public ValueTask<PvpRoomOutcome> ReportLoadedAsync(SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(ReportLoadedAsync));
        ArgumentNullException.ThrowIfNull(session);
        return Report(Pending, session, PvpRoomPhase.Loading, PvpFighterReports.Loaded, PvpRoomChangeKind.Loaded,
            next => next with
            {
                Phase = PvpRoomPhase.PeerLinking,
                Match = next.Match! with { PhaseDeadline = Now + rules.PeerLinkTimeout },
            });
    }

    public ValueTask<PvpRoomOutcome> ReportPeerLinkedAsync(SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(ReportPeerLinkedAsync));
        ArgumentNullException.ThrowIfNull(session);
        return Report(Pending, session, PvpRoomPhase.PeerLinking, PvpFighterReports.PeerLinked, PvpRoomChangeKind.PeerLinked,
            next =>
            {
                var now = Now;
                var match = next.Match!;
                return next with
                {
                    Phase = PvpRoomPhase.Fighting,
                    Match = match with
                    {
                        FightingSince = now,
                        PhaseDeadline = now + battles.BattleTimeLimit(next.Mode) + rules.FightOverrun,
                        Fighters = [.. match.Fighters.Select(fighter => fighter with { LastHeartbeat = now })],
                    },
                };
            });
    }

    public ValueTask<PvpRoomOutcome> HeartbeatAsync(SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(HeartbeatAsync));
        ArgumentNullException.ThrowIfNull(session);
        return Battle(Pending, session, PvpRoomPhase.Fighting, nameof(HeartbeatAsync), (pending, room, match, index) =>
            Stage(pending, room, room.Snapshot with
            {
                Match = match with { Fighters = match.Fighters.SetItem(index, match.Fighters[index] with { LastHeartbeat = Now }) },
            }, PvpRoomChangeKind.Heartbeat));
    }

    public ValueTask<PvpRoomOutcome> ReportDeathAsync(
        SelectedSessionIdentity session, CharacterId? killer, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(ReportDeathAsync));
        ArgumentNullException.ThrowIfNull(session);
        return Battle(Pending, session, PvpRoomPhase.Fighting, nameof(ReportDeathAsync), (pending, room, match, index) =>
        {
            var victim = match.Fighters[index];
            if (!victim.Alive) return Ignore(nameof(ReportDeathAsync), session, PvpRoomRefusal.NotAlive);
            var fighters = match.Fighters.SetItem(index, victim with { Alive = false, Deaths = victim.Deaths + 1 });
            int? credited = null;
            // Only a live enemy of this battle earns the kill; anything else the client names is a death alone.
            var by = -1;
            for (var candidate = 0; candidate < fighters.Length; candidate++)
                if (killer is { } named && fighters[candidate].CharacterId == named)
                    by = candidate;
            if (by >= 0 && by != index && fighters[by] is { Alive: true, Departed: false } scorer
                && (scorer.Team == PvpTeam.Individual || scorer.Team != victim.Team))
            {
                fighters = fighters.SetItem(by, scorer with { Kills = scorer.Kills + 1 });
                credited = scorer.Seat;
            }
            return Stage(pending, room, room.Snapshot with { Match = match with { Fighters = fighters } }, PvpRoomChangeKind.Died)
                with { Death = new PvpDeath(victim.Seat, credited) };
        });
    }

    public ValueTask<PvpRoomOutcome> ReportTimeoutAsync(
        SelectedSessionIdentity session, ImmutableArray<byte> clientValues, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(ReportTimeoutAsync));
        ArgumentNullException.ThrowIfNull(session);
        return Battle(Pending, session, PvpRoomPhase.Fighting, nameof(ReportTimeoutAsync), (pending, room, match, _) =>
        {
            var now = Now;
            if (match.FightingSince is not { } since
                || now - since < battles.BattleTimeLimit(room.Snapshot.Mode) - rules.TimeoutReportLead)
                return Ignore(nameof(ReportTimeoutAsync), session, PvpRoomRefusal.TooEarly);
            // The first phase draws every timed-out battle; what the client counted is kept for the record only.
            logger.LogInformation("Duel battle {Match} timed out on character {Character}'s report {Values}; drawn",
                match.Id.Value, session.CharacterId.Value, clientValues.IsDefault ? "" : Convert.ToHexString(clientValues.AsSpan()));
            return Stage(pending, room, Conclude(room.Snapshot, new PvpMatchTerminal(PvpMatchOutcomeKind.Draw, null, now)),
                PvpRoomChangeKind.TimedOut);
        });
    }

    public ValueTask<PvpRoomOutcome> AcknowledgeRankAsync(SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(AcknowledgeRankAsync));
        ArgumentNullException.ThrowIfNull(session);
        return Report(Pending, session, PvpRoomPhase.AwaitRank, PvpFighterReports.RankAcknowledged,
            PvpRoomChangeKind.RankAcknowledged, null);
    }

    public ValueTask<PvpRoomOutcome> AcknowledgeResultAsync(SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(AcknowledgeResultAsync));
        ArgumentNullException.ThrowIfNull(session);
        return Report(Pending, session, PvpRoomPhase.AwaitResult, PvpFighterReports.ResultAcknowledged,
            PvpRoomChangeKind.ResultAcknowledged, null);
    }

    public ValueTask<PvpRoomOutcome> DepartAsync(
        SelectedSessionIdentity session, PvpDepartureReason reason, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(DepartAsync));
        ArgumentNullException.ThrowIfNull(session);
        var pending = Pending;
        if (pending.FindSeated(session.CharacterId) is not { } room
            || room.Snapshot.SeatOf(session.CharacterId) is not { } seat
            || ((PvpSeat.Taken)room.Snapshot.Seats[seat]).Session != session)
            return Refused(PvpRoomRefusal.NotSeated);
        var snapshot = room.Snapshot;
        var keepsSeat = reason == PvpDepartureReason.HeartbeatLost;
        if (keepsSeat && snapshot.Phase != PvpRoomPhase.Fighting) return Refused(PvpRoomRefusal.WrongPhase);
        var next = snapshot;
        if (snapshot.Match is { } match && match.FighterOf(session) is { } index && !match.Fighters[index].Departed)
        {
            var fighter = match.Fighters[index];
            next = snapshot.Phase switch
            {
                // Spec RQ-040: a battle still loading or linking cannot go on without one of its fighters.
                PvpRoomPhase.Loading or PvpRoomPhase.PeerLinking => Waiting(snapshot),
                PvpRoomPhase.Fighting => snapshot with { Match = match with { Fighters = match.Fighters.SetItem(index, Flee(fighter)) } },
                // The result is frozen: leaving now only stops the screens from waiting for this fighter.
                PvpRoomPhase.AwaitRank or PvpRoomPhase.AwaitResult =>
                    snapshot with { Match = match with { Fighters = match.Fighters.SetItem(index, fighter with { Departed = true }) } },
                PvpRoomPhase.Waiting => throw new InvalidOperationException("A waiting duel room has no battle."),
            };
        }
        else if (keepsSeat) return Refused(PvpRoomRefusal.NotFighter);
        return ValueTask.FromResult<PvpRoomOutcome>(keepsSeat
            ? Stage(pending, room, next, PvpRoomChangeKind.Departed)
            : Stage(pending, room, next, PvpRoomChangeKind.Departed, (seat, PvpSeatReleaseReason.Departed)));
    }

    public PvpDeadlineScan Deadlines(DateTimeOffset now)
    {
        List<(PvpRoomId Id, DateTimeOffset At)> roomDeadlines;
        List<PvpMatchConclusion> ended;
        lock (gate)
        {
            roomDeadlines = [.. rooms.Values.Select(room => (room.Snapshot.Id, At: DueOf(room.Snapshot)))
                .Where(x => x.At is not null).Select(x => (x.Id, x.At!.Value))];
            ended = [.. conclusions.Values];
        }
        var giveUp = rules.SettlementGiveUp;
        PvpRoomId[] dueRooms = [.. roomDeadlines.Where(x => x.At <= now).Select(x => x.Id)];
        PvpMatchId[] abandoned = [.. ended.Where(x => x.SettleBy + giveUp <= now).OrderBy(x => x.SettleBy).Select(x => x.MatchId)];
        PvpMatchId[] dueSettlements = [.. ended.Where(x => x.SettleBy <= now && x.SettleBy + giveUp > now)
            .OrderBy(x => x.SettleBy).Select(x => x.MatchId)];
        var ahead = roomDeadlines.Select(x => x.At).Concat(ended.Select(x => x.SettleBy)).Where(at => at > now).ToList();
        var overdue = roomDeadlines.Select(x => x.At).Concat(ended.Select(x => x.SettleBy)).Where(at => at <= now).ToArray();
        if (overdue.Length > 0)
        {
            var retry = (now - overdue.Min()) / 4;
            ahead.Add(now + (retry < RetryFloor ? RetryFloor : retry > RetryCeiling ? RetryCeiling : retry));
        }
        return new PvpDeadlineScan(dueRooms, dueSettlements, abandoned, ahead.Count == 0 ? null : ahead.Min());
    }

    public ValueTask<PvpRoomOutcome.Changed?> AbandonSettlementAsync(PvpMatchId match, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(AbandonSettlementAsync));
        var pending = Pending;
        if (pending.FindConclusion(match) is not { } conclusion) return ValueTask.FromResult<PvpRoomOutcome.Changed?>(null);
        // Whether an earlier attempt wrote the results is not known here (its commit may have been uncertain); the store's
        // once-per-battle rule keeps a later retry harmless either way, so the log claims only what this step does.
        logger.LogError("Duel battle {Match} in room {Room} still unsettled since {SettleBy}; no longer retried, and its room, if still on its ranking, waits again. "
            + "Check pvp_matches for it: an earlier attempt whose outcome was uncertain may have written it",
            match.Value, conclusion.RoomNumber, conclusion.SettleBy);
        pending.PutConclusion(match, null);
        if (pending.Find(conclusion.RoomId) is not { Snapshot: { Phase: PvpRoomPhase.AwaitRank, Match: { } current } } room
            || current.Id != match)
            return ValueTask.FromResult<PvpRoomOutcome.Changed?>(null);
        return ValueTask.FromResult<PvpRoomOutcome.Changed?>(
            Stage(pending, room, Waiting(room.Snapshot), PvpRoomChangeKind.SettlementAbandoned));
    }

    public ValueTask<PvpRoomOutcome.Changed?> AdvanceDueAsync(PvpRoomId room, DateTimeOffset now, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(AdvanceDueAsync));
        var pending = Pending;
        if (pending.Find(room) is not { Snapshot.Match: { } match } current) return ValueTask.FromResult<PvpRoomOutcome.Changed?>(null);
        var snapshot = current.Snapshot;
        PvpRoomOutcome.Changed? Advanced(PvpRoomSnapshot next) => Stage(pending, current, next, PvpRoomChangeKind.DeadlinePassed);
        var changed = snapshot.Phase switch
        {
            PvpRoomPhase.Loading or PvpRoomPhase.PeerLinking or PvpRoomPhase.AwaitResult =>
                match.PhaseDeadline <= now ? Advanced(Waiting(snapshot)) : null,
            PvpRoomPhase.Fighting => AdvanceFight(snapshot, match, now) is { } next ? Advanced(next) : null,
            // A battle waiting for its ranking is due through its conclusion, which settlement takes.
            PvpRoomPhase.AwaitRank or PvpRoomPhase.Waiting => null,
        };
        return ValueTask.FromResult(changed);
    }

    /// <summary>A fight past its deadlines: the silent fighters have fled, and an overrun fight is drawn.</summary>
    private PvpRoomSnapshot? AdvanceFight(PvpRoomSnapshot snapshot, PvpMatch match, DateTimeOffset now)
    {
        var fighters = match.Fighters;
        var silent = false;
        for (var index = 0; index < fighters.Length; index++)
            if (!fighters[index].Departed && fighters[index].LastHeartbeat + rules.HeartbeatTimeout <= now)
            {
                fighters = fighters.SetItem(index, Flee(fighters[index]));
                silent = true;
            }
        var overrun = match.PhaseDeadline <= now;
        if (!silent && !overrun) return null;
        var next = snapshot with { Match = match with { Fighters = fighters } };
        return overrun && TerminalOf(next.Mode, fighters) is null
            ? Conclude(next, new PvpMatchTerminal(PvpMatchOutcomeKind.Draw, null, Now))
            : next;
    }

    public IReadOnlyList<PvpMatchConclusion> PendingConclusions()
    {
        if (Staged is { } staged) return staged.Conclusions();
        lock (gate) return [.. conclusions.Values];
    }

    public ValueTask<IReadOnlyList<PvpRoomOutcome.Changed>> CancelForShutdownAsync(CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(CancelForShutdownAsync));
        var pending = Pending;
        pending.Close();
        var cancelled = pending.Rooms()
            .Where(room => room.Snapshot.Match is { Terminal: null })
            .Select(room => Stage(pending, room, Waiting(room.Snapshot), PvpRoomChangeKind.CancelledForShutdown))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<PvpRoomOutcome.Changed>>(cancelled);
    }

    /// <summary>The ended battle waiting for settlement, as this transaction sees it.</summary>
    internal PvpMatchConclusion? FindConclusion(PvpMatchId match)
    {
        if (Staged is { } staged) return staged.FindConclusion(match);
        lock (gate) return conclusions.GetValueOrDefault(match);
    }

    /// <summary>
    /// Settlement wrote the battle: it no longer waits, and its room, when it still stands on that battle, moves to
    /// the result screen.
    /// </summary>
    internal PvpRoomOutcome.Changed? CompleteSettlement(PvpMatchConclusion conclusion, IReadOnlyList<PvpFighterResult> results)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(CompleteSettlement));
        ArgumentNullException.ThrowIfNull(conclusion);
        var pending = Pending;
        pending.PutConclusion(conclusion.MatchId, null);
        if (pending.Find(conclusion.RoomId) is not { Snapshot: { Phase: PvpRoomPhase.AwaitRank, Match: { } match } } room
            || match.Id != conclusion.MatchId)
            return null;
        return Stage(pending, room, room.Snapshot with
        {
            Phase = PvpRoomPhase.AwaitResult,
            Match = match with { PhaseDeadline = Now + rules.ResultTimeout, Results = [.. results] },
        }, PvpRoomChangeKind.Settled);
    }

    /// <summary>A phase report: recorded once per fighter, and <paramref name="whenAll"/> applied once every fighter left in the battle made it.</summary>
    private ValueTask<PvpRoomOutcome> Report(Changes pending, SelectedSessionIdentity session, PvpRoomPhase phase,
        PvpFighterReports report, PvpRoomChangeKind kind, Func<PvpRoomSnapshot, PvpRoomSnapshot>? whenAll) =>
        Battle(pending, session, phase, kind.ToString(), (_, room, match, index) =>
        {
            var fighter = match.Fighters[index];
            if (fighter.Reports.HasFlag(report)) return Ignore(kind.ToString(), session, PvpRoomRefusal.AlreadyReported);
            var fighters = match.Fighters.SetItem(index, fighter with { Reports = fighter.Reports | report });
            var next = room.Snapshot with { Match = match with { Fighters = fighters } };
            if (whenAll is not null && fighters.All(x => x.Departed || x.Reports.HasFlag(report))) next = whenAll(next);
            return Stage(pending, room, next, kind);
        });

    /// <summary>
    /// A report from a fighter of the room's battle in <paramref name="phase"/>; anything else is ignored and logged,
    /// leaving the room as it is.
    /// </summary>
    private ValueTask<PvpRoomOutcome> Battle(Changes pending, SelectedSessionIdentity session, PvpRoomPhase phase, string report,
        Func<Changes, Room, PvpMatch, int, PvpRoomOutcome> act)
    {
        if (pending.FindSeated(session.CharacterId) is not { } room)
            return ValueTask.FromResult(Ignore(report, session, PvpRoomRefusal.NotFighter));
        if (room.Snapshot.Match is not { } match || room.Snapshot.Phase != phase)
            return ValueTask.FromResult(Ignore(report, session, PvpRoomRefusal.WrongPhase));
        if (match.FighterOf(session) is not { } index || match.Fighters[index].Departed)
            return ValueTask.FromResult(Ignore(report, session, PvpRoomRefusal.NotFighter));
        return ValueTask.FromResult(act(pending, room, match, index));
    }

    private PvpRoomOutcome Ignore(string report, SelectedSessionIdentity session, PvpRoomRefusal reason)
    {
        logger.LogInformation("Duel report {Report} from character {Character} ignored: {Reason}",
            report, session.CharacterId.Value, reason);
        return Refuse(reason);
    }

    /// <summary>Spec RQ-040: a fighter leaving a fight counts as dead, killed by nobody, and fled.</summary>
    private static PvpFighter Flee(PvpFighter fighter) => fighter with
    {
        Alive = false,
        Deaths = fighter.Alive ? fighter.Deaths + 1 : fighter.Deaths,
        Fled = true,
        Departed = true,
    };

    /// <summary>The room waiting again: no battle, and nobody ready.</summary>
    private static PvpRoomSnapshot Waiting(PvpRoomSnapshot snapshot) => snapshot with
    {
        Phase = PvpRoomPhase.Waiting,
        Match = null,
        Seats = [.. snapshot.Seats.Select(seat => seat is PvpSeat.Taken taken ? taken with { Ready = false } : seat)],
    };

    /// <summary>Freezes the battle's end and opens the ranking screen.</summary>
    private PvpRoomSnapshot Conclude(PvpRoomSnapshot snapshot, PvpMatchTerminal end) => snapshot with
    {
        Phase = PvpRoomPhase.AwaitRank,
        Match = snapshot.Match! with { Terminal = end, PhaseDeadline = Now + rules.RankTimeout },
    };

    /// <summary>How the battle ends once nobody is left to fight, by the mode's rule; null while it goes on.</summary>
    private PvpMatchTerminal? TerminalOf(PvpBattleMode mode, ImmutableArray<PvpFighter> fighters)
    {
        var alive = fighters.Where(fighter => fighter.Alive);
        byte[] sides = PvpBattleModeCapabilities.Of(mode).Terminal switch
        {
            PvpBattleTerminal.LastFighterStanding => [.. alive.Select(fighter => (byte)fighter.Seat)],
            PvpBattleTerminal.LastTeamStanding => [.. alive.Select(fighter => (byte)fighter.Team).Distinct()],
        };
        return sides.Length switch
        {
            0 => new PvpMatchTerminal(PvpMatchOutcomeKind.Draw, null, Now),
            1 => new PvpMatchTerminal(PvpMatchOutcomeKind.Decided, sides[0], Now),
            _ => null,
        };
    }

    /// <summary>When the server next has to advance the room itself; a battle on its ranking screen is due through its conclusion.</summary>
    private DateTimeOffset? DueOf(PvpRoomSnapshot? snapshot)
    {
        if (snapshot?.Match is not { } match) return null;
        return snapshot.Phase switch
        {
            PvpRoomPhase.Loading or PvpRoomPhase.PeerLinking or PvpRoomPhase.AwaitResult => match.PhaseDeadline,
            PvpRoomPhase.Fighting => match.Fighters.Where(fighter => !fighter.Departed)
                .Select(fighter => fighter.LastHeartbeat + rules.HeartbeatTimeout)
                .Append(match.PhaseDeadline).Min(),
            PvpRoomPhase.AwaitRank or PvpRoomPhase.Waiting => null,
        };
    }

    /// <summary>
    /// Stages a room's next state and what follows from it: a fight nobody is left to fight ends, an ended battle
    /// waits for settlement, a ranking nobody waits on asks for it, a result screen nobody waits on closes, and a
    /// vacated seat passes ownership on or ends the room.
    /// </summary>
    private PvpRoomOutcome.Changed Stage(Changes pending, Room room, PvpRoomSnapshot next, PvpRoomChangeKind kind,
        (int Seat, PvpSeatReleaseReason Reason)? vacate = null)
    {
        var before = room.Snapshot;
        PvpMatchConclusion? concluded = null;
        PvpMatchId? due = null;
        if (next.Phase == PvpRoomPhase.Fighting && TerminalOf(next.Mode, next.Match!.Fighters) is { } end)
            next = Conclude(next, end);
        if (next.Match is { } match)
        {
            if (match.Terminal is { } terminal && before.Match?.Terminal is null)
            {
                concluded = new PvpMatchConclusion(match.Id, next.Id, next.Number, next.Channel, next.Mode, match.StartedAt,
                    terminal, match.Fighters, match.PhaseDeadline);
                pending.PutConclusion(match.Id, concluded);
            }
            if (next.Phase == PvpRoomPhase.AwaitRank
                && match.Fighters.All(fighter => fighter.Departed || fighter.Reports.HasFlag(PvpFighterReports.RankAcknowledged)))
            {
                due = match.Id;
                // Settled now by the caller, or by the scheduler at once when the caller leaves it.
                if (pending.FindConclusion(match.Id) is { } waiting && waiting.SettleBy > Now)
                    pending.PutConclusion(match.Id, waiting with { SettleBy = Now });
            }
            if (next.Phase == PvpRoomPhase.AwaitResult
                && match.Fighters.All(fighter => fighter.Departed || fighter.Reports.HasFlag(PvpFighterReports.ResultAcknowledged)))
                next = Waiting(next);
        }

        var releases = new List<PvpSeatRelease>();
        var returning = new List<SelectedSessionIdentity>();
        Room? after = room with { Snapshot = next };
        if (vacate is { } release)
        {
            var (seat, reason) = release;
            var leaving = (PvpSeat.Taken)next.Seats[seat];
            var seats = next.Seats.SetItem(seat, PvpSeat.OpenSeat);
            var remaining = Enumerable.Range(0, seats.Length).Where(index => seats[index] is PvpSeat.Taken).ToArray();
            var owner = seat == next.OwnerSeat ? rules.Successor(seat, remaining) : next.OwnerSeat;
            after = owner is { } successor ? room with { Snapshot = next with { Seats = seats, OwnerSeat = successor } } : null;
            releases.Add(new PvpSeatRelease(seat, leaving.Session, reason));
            // An ended session has no town to go back to.
            if (reason != PvpSeatReleaseReason.Departed) returning.Add(leaving.Session);
        }
        var town = new TownMoves(before.Channel.ChannelId, leaving: [], returning: returning);
        var staged = pending.Put(before.Id, room, after, town);
        return new PvpRoomOutcome.Changed(kind, before, staged?.Snapshot, releases, town)
        {
            Concluded = concluded,
            SettlementDue = due,
        };
    }

    private static ValueTask<PvpRoomOutcome> Changed(Changes pending, Room room, PvpRoomSnapshot next, PvpRoomChangeKind kind)
    {
        var town = new TownMoves(room.Snapshot.Channel.ChannelId, leaving: [], returning: []);
        var after = pending.Put(room.Snapshot.Id, room, room with { Snapshot = next }, town)!;
        return ValueTask.FromResult<PvpRoomOutcome>(new PvpRoomOutcome.Changed(kind, room.Snapshot, after.Snapshot, [], town));
    }

    /// <summary>The room the actor holds a seat in as exactly this session, and that seat.</summary>
    private (Room Room, int Seat)? Acting(Changes pending, SelectedSessionIdentity actor)
    {
        if (!sessions.IsInWorld(actor) || pending.FindSeated(actor.CharacterId) is not { } room) return null;
        var seat = room.Snapshot.SeatOf(actor.CharacterId)!.Value;
        return ((PvpSeat.Taken)room.Snapshot.Seats[seat]).Session == actor ? (room, seat) : null;
    }

    private PvpRoomRefusal ActingRefusal(SelectedSessionIdentity actor) =>
        sessions.IsInWorld(actor) ? PvpRoomRefusal.NotSeated : PvpRoomRefusal.SessionNotCurrent;

    private Room? OwnerActing(Changes pending, SelectedSessionIdentity actor) =>
        Acting(pending, actor) is (var room, var seat) && seat == room.Snapshot.OwnerSeat ? room : null;

    private PvpRoomRefusal OwnerRefusal(Changes pending, SelectedSessionIdentity actor) =>
        Acting(pending, actor) is null ? ActingRefusal(actor) : PvpRoomRefusal.NotOwner;

    private bool InWorld(SelectedSessionIdentity session) => sessions.IsInWorld(session);

    private DateTimeOffset Now => clock.GetUtcNow();

    private void WakeDeadlines() => deadlines.Wake();

    private bool InParty(CharacterId characterId) => parties.KeyOf(characterId).Kind == PartyKeyKind.Real;

    private static PvpRoomOutcome Refuse(PvpRoomRefusal reason) => new PvpRoomOutcome.Refused(reason);

    private static ValueTask<PvpRoomOutcome> Refused(PvpRoomRefusal reason) => ValueTask.FromResult(Refuse(reason));

    /// <summary>Makes a committed change's town moves; a commit cannot fail, so a failed move is logged.</summary>
    private async ValueTask ApplyAsync(TownMoves town)
    {
        IReadOnlyList<TownDepartureResult> departures = [];
        IReadOnlyList<TownReturnAdmission> returns = [];
        try
        {
            if (town.Leaving.Count > 0)
                departures = transit.LeaveTown(town.Leaving, TownAreaDepartureReason.PvpRoom);
            if (town.Returning.Count > 0)
            {
                var requests = town.Returning
                    .Select(session => online.PositionOf(session) is { } position
                        ? new TownReturnRequest(session, town.ChannelId, position) : null)
                    .OfType<TownReturnRequest>().ToArray();
                returns = await transit.ReturnToTownAsync(requests, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            logger.LogError(error, "Duel room town moves failed after the room change committed");
        }
        town.Complete(departures, returns);
    }

    private sealed class TownMoves(
        int channelId, IReadOnlyList<SelectedSessionIdentity> leaving, IReadOnlyList<SelectedSessionIdentity> returning)
        : IPvpTownMoves
    {
        public int ChannelId => channelId;
        public IReadOnlyList<SelectedSessionIdentity> Leaving => leaving;
        public IReadOnlyList<SelectedSessionIdentity> Returning => returning;
        public bool Applied { get; private set; }
        public IReadOnlyList<TownDepartureResult> Departures { get; private set; } = [];
        public IReadOnlyList<TownReturnAdmission> Returns { get; private set; } = [];

        public void Complete(IReadOnlyList<TownDepartureResult> departures, IReadOnlyList<TownReturnAdmission> returns)
        {
            Departures = departures;
            Returns = returns;
            Applied = true;
        }
    }

    private sealed class Changes(PvpRoomDomainService owner) : ITransactionParticipant
    {
        private readonly Dictionary<PvpRoomId, (Room? Before, Room? After)> rooms = [];
        private readonly Dictionary<InvitationKey, (PvpInvitation? Before, PvpInvitation? After)> invitations = [];
        private readonly Dictionary<PvpMatchId, (PvpMatchConclusion? Before, PvpMatchConclusion? After)> conclusions = [];
        private readonly List<TownMoves> towns = [];
        private bool closing;

        public bool CommitCanFail => false;

        /// <summary>Whether shutdown has cancelled the running battles, as this transaction sees it.</summary>
        public bool Closing
        {
            get
            {
                if (closing) return true;
                lock (owner.gate) return owner.closing;
            }
        }

        public void Close() => closing = true;

        public Room? Find(PvpRoomId id)
        {
            if (rooms.TryGetValue(id, out var change)) return change.After;
            lock (owner.gate) return owner.rooms.GetValueOrDefault(id);
        }

        public Room? FindSeated(CharacterId characterId)
        {
            foreach (var (_, after) in rooms.Values)
                if (after?.Snapshot.SeatOf(characterId) is not null)
                    return after;
            lock (owner.gate)
                return owner.seated.TryGetValue(characterId, out var id) && !rooms.ContainsKey(id) ? owner.rooms[id] : null;
        }

        public Room? FindByNumber(int listenerPort, ushort number) =>
            Visible(listenerPort).FirstOrDefault(room => room.Snapshot.Number == number);

        /// <summary>The smallest positive number no room on the listener shows, as this transaction sees them.</summary>
        public ushort NextNumber(int listenerPort)
        {
            var used = Visible(listenerPort).Select(room => room.Snapshot.Number).ToHashSet();
            for (var number = 1; number <= ushort.MaxValue; number++)
                if (!used.Contains((ushort)number))
                    return (ushort)number;
            throw new InvalidOperationException("Every duel room number on this listener is in use.");
        }

        private List<Room> Visible(int listenerPort) => [.. Rooms().Where(room => room.Snapshot.ListenerPort == listenerPort)];

        /// <summary>Every room, as this transaction sees them.</summary>
        public List<Room> Rooms()
        {
            List<Room> visible;
            lock (owner.gate)
                visible = [.. owner.rooms.Values.Where(room => !rooms.ContainsKey(room.Snapshot.Id))];
            visible.AddRange(rooms.Values.Select(change => change.After).OfType<Room>());
            return visible;
        }

        public PvpMatchConclusion? FindConclusion(PvpMatchId id)
        {
            if (conclusions.TryGetValue(id, out var change)) return change.After;
            lock (owner.gate) return owner.conclusions.GetValueOrDefault(id);
        }

        /// <summary>Every ended battle waiting for settlement, as this transaction sees them.</summary>
        public IReadOnlyList<PvpMatchConclusion> Conclusions()
        {
            List<PvpMatchConclusion> visible;
            lock (owner.gate)
                visible = [.. owner.conclusions.Values.Where(conclusion => !conclusions.ContainsKey(conclusion.MatchId))];
            visible.AddRange(conclusions.Values.Select(change => change.After).OfType<PvpMatchConclusion>());
            return visible;
        }

        public void PutConclusion(PvpMatchId id, PvpMatchConclusion? next)
        {
            if (conclusions.TryGetValue(id, out var prior)) conclusions[id] = (prior.Before, next);
            else lock (owner.gate) conclusions[id] = (owner.conclusions.GetValueOrDefault(id), next);
        }

        /// <summary>Stages a room's next state; its version is one past the committed one, however often it is staged.</summary>
        public Room? Put(PvpRoomId id, Room? before, Room? after, TownMoves town)
        {
            var committed = rooms.TryGetValue(id, out var prior) ? prior.Before : before;
            var stamped = after is null ? null : after with
            {
                Snapshot = after.Snapshot with { Version = (committed?.Snapshot.Version ?? 0) + 1 },
            };
            rooms[id] = (committed, stamped);
            towns.Add(town);
            return stamped;
        }

        public PvpInvitation? FindInvitation(InvitationKey key)
        {
            if (invitations.TryGetValue(key, out var change)) return change.After;
            lock (owner.gate) return owner.invitations.GetValueOrDefault(key);
        }

        public void PutInvitation(InvitationKey key, PvpInvitation? next)
        {
            if (invitations.TryGetValue(key, out var prior)) invitations[key] = (prior.Before, next);
            else lock (owner.gate) invitations[key] = (owner.invitations.GetValueOrDefault(key), next);
        }

        public ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            lock (owner.gate)
            {
                foreach (var (id, (before, after)) in rooms)
                {
                    if (!ReferenceEquals(owner.rooms.GetValueOrDefault(id), before))
                        throw new InvalidOperationException("A duel room changed before commit.");
                    if (after is null) continue;
                    var snapshot = after.Snapshot;
                    if (owner.rooms.Values.Any(room => room.Snapshot.Id != id && !rooms.ContainsKey(room.Snapshot.Id)
                            && room.Snapshot.ListenerPort == snapshot.ListenerPort && room.Snapshot.Number == snapshot.Number)
                        || rooms.Any(other => other.Key != id && other.Value.After is { } staged
                            && staged.Snapshot.ListenerPort == snapshot.ListenerPort && staged.Snapshot.Number == snapshot.Number))
                        throw new InvalidOperationException("A duel room number was taken before commit.");
                    foreach (var (seat, member) in snapshot.Members)
                    {
                        if (owner.seated.TryGetValue(member.CharacterId, out var held) && held != id
                            && !(rooms.TryGetValue(held, out var releasing) && releasing.After?.Snapshot.SeatOf(member.CharacterId) is null))
                            throw new InvalidOperationException("A duel room member took another seat before commit.");
                        // A seat taken in this transaction must still belong to the exact session that took it.
                        if (before?.Snapshot.Seats[seat] is PvpSeat.Taken kept && kept.Session == member.Session) continue;
                        if (!owner.InWorld(member.Session))
                            throw new InvalidOperationException("A duel room seat's session changed before commit.");
                    }
                }
                foreach (var (key, (before, _)) in invitations)
                    if (!ReferenceEquals(owner.invitations.GetValueOrDefault(key), before))
                        throw new InvalidOperationException("A duel invitation changed before commit.");
                foreach (var (id, (before, _)) in conclusions)
                    if (!ReferenceEquals(owner.conclusions.GetValueOrDefault(id), before))
                        throw new InvalidOperationException("An ended duel battle changed before commit.");
            }
            return ValueTask.CompletedTask;
        }

        public async ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            var wake = false;
            lock (owner.gate)
            {
                foreach (var (id, (before, _)) in rooms)
                    if (before is not null)
                        foreach (var (_, member) in before.Snapshot.Members)
                            if (owner.seated.GetValueOrDefault(member.CharacterId) == id)
                                owner.seated.Remove(member.CharacterId);
                foreach (var (id, (before, after)) in rooms)
                {
                    if (after is null) owner.rooms.Remove(id);
                    else
                    {
                        owner.rooms[id] = after;
                        foreach (var (_, member) in after.Snapshot.Members) owner.seated[member.CharacterId] = id;
                    }
                    wake |= Sooner(owner.DueOf(before?.Snapshot), owner.DueOf(after?.Snapshot));
                }
                foreach (var (id, (before, after)) in conclusions)
                {
                    if (after is null) owner.conclusions.Remove(id);
                    else owner.conclusions[id] = after;
                    wake |= Sooner(before?.SettleBy, after?.SettleBy);
                }
                foreach (var (key, (_, after)) in invitations)
                    if (after is null) owner.invitations.Remove(key);
                    else owner.invitations[key] = after;
                owner.closing |= closing;
                // Expired invitations, and those into rooms that are gone, are tidied whenever anything commits.
                var now = owner.Now;
                foreach (var (key, invitation) in owner.invitations.ToArray())
                    if (now >= invitation.ExpiresAt || !owner.rooms.ContainsKey(invitation.RoomId))
                        owner.invitations.Remove(key);
            }
            foreach (var town in towns) await owner.ApplyAsync(town).ConfigureAwait(false);
            if (wake) owner.WakeDeadlines();
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        /// <summary>
        /// Whether a deadline came closer: the scheduler already waits for any later one, and finds nothing due
        /// when it gets there.
        /// </summary>
        private static bool Sooner(DateTimeOffset? before, DateTimeOffset? after) =>
            after is { } next && (before is not { } previous || next < previous);
    }
}
