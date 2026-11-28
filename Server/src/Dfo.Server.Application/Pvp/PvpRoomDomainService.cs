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
using Dfo.Server.Data.Channel;
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
internal sealed partial class PvpRoomDomainService(
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
    ILogger<PvpRoomDomainService> logger,
    IPvpTotalMatchCandidateQueryDomainService totalMatchCandidates,
    ICharacterIdentityQueryDomainService identities,
    PvpTraditionalDuelDomainService traditional,
    ICharacterSkillRuleService skillRules,
    ICharacterSkillStateDomainService skillStates,
    PvpColosseumDomainService colosseum,
    PvpMatchmakingRuleService matchmakingRules,
    PvpDailyMissionRuleService missionRules) : IPvpRoomDomainService, IPvpMatchmakingDomainService
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
            return [.. rooms.Values.Select(room => room.Snapshot)
                .Where(room => room.ListenerPort == listenerPort && room.IsPublicLobbyRoom)
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
        var mode = rules.FindBattleMode(request.Mode);
        if (request.Mode != PvpBattleMode.Practice
            && (mode is not { CanSelectInFreeRoom: true } || !mode.AcceptsChannel(channel.LoginEnvironment)))
            return Refuse(PvpRoomRefusal.UnsupportedMode);
        if (!rules.AcceptsMap(request.MapIndex)) return Refuse(PvpRoomRefusal.InvalidMap);
        var password = request.Password is { Length: > 0 } offered ? offered : (ImmutableArray<byte>?)null;
        if (password is { } chosen && !rules.AcceptsPassword(chosen)) return Refuse(PvpRoomRefusal.InvalidPassword);
        if (pending.FindSeated(owner.CharacterId) is not null) return Refuse(PvpRoomRefusal.AlreadySeated);
        if (InParty(owner.CharacterId)) return Refuse(PvpRoomRefusal.InParty);

        var seats = Enumerable.Repeat(PvpSeat.OpenSeat, PvpRoomSnapshot.SeatCount).ToImmutableArray()
            .SetItem(0, new PvpSeat.Taken(owner, mode?.TeamForNewcomer([]) ?? PvpTeam.Individual, Ready: false));
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

    public ValueTask<PvpRoomOutcome> QuickJoinAsync(SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(QuickJoinAsync));
        ArgumentNullException.ThrowIfNull(session);
        if (!sessions.IsInWorld(session)) return Refused(PvpRoomRefusal.SessionNotCurrent);

        var pending = Pending;
        if (pending.FindQuickJoinable(session.ListenerPort) is not { } room)
            return Refused(PvpRoomRefusal.RoomMissing);
        return ValueTask.FromResult(Admit(pending, room, session, PvpRoomChangeKind.Entered, null, checkPassword: true));
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
        if (snapshot.IsMatchmaking)
            return snapshot.Admission is not null && action == PvpSeatAction.Leave && seat == own
                ? ValueTask.FromResult<PvpRoomOutcome>(CloseMatchedRoom(pending, room, PvpRoomChangeKind.MatchAdmissionCancelled))
                : Refused(PvpRoomRefusal.NotWaiting);
        var target = snapshot.Seats[seat];
        var observerLeaving = action == PvpSeatAction.Leave && seat == own && target is PvpSeat.Taken { Observation: not null };
        if (snapshot.Phase != PvpRoomPhase.Waiting && !observerLeaving) return Refused(PvpRoomRefusal.NotWaiting);
        if (snapshot.IsPractice && action != PvpSeatAction.Leave) return Refused(PvpRoomRefusal.InvalidSeat);
        var isOwner = own == snapshot.OwnerSeat;
        switch (action)
        {
            case PvpSeatAction.ChangeTeam:
                if (seat != own) return Refused(PvpRoomRefusal.InvalidSeat);
                if (!rules.BattleMode(snapshot.Mode).AcceptsTeam(team)) return Refused(PvpRoomRefusal.InvalidTeam);
                return Changed(pending, room, snapshot with
                {
                    Seats = snapshot.Seats.SetItem(seat, (PvpSeat.Taken)target with { Team = team, Observation = null }),
                }, PvpRoomChangeKind.TeamChanged);
            case PvpSeatAction.Observe or PvpSeatAction.ObserveAlternate:
                if (seat != own || !rules.BattleMode(snapshot.Mode).AllowsObservation) return Refused(PvpRoomRefusal.InvalidSeat);
                var observation = action == PvpSeatAction.Observe ? PvpObservation.Standard : PvpObservation.Alternate;
                return Changed(pending, room, snapshot with
                {
                    Seats = snapshot.Seats.SetItem(seat, (PvpSeat.Taken)target with { Observation = observation, Ready = false }),
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
                if (!rules.AllowsClosing(snapshot.PlayingMembers.Count(), snapshot.Seats.Count(x => x is PvpSeat.Open)))
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
        if (!snapshot.IsPublicLobbyRoom || rules.FindBattleMode(mode) is not { CanSelectInFreeRoom: true } nextMode
            || !nextMode.AcceptsChannel(snapshot.Channel.LoginEnvironment))
            return Refused(PvpRoomRefusal.UnsupportedMode);
        if (snapshot.MemberCount > nextMode.MaximumMembers)
            return Refused(PvpRoomRefusal.Full);
        var players = snapshot.PlayingMembers.ToArray();
        if (!nextMode.AllowsObservation && players.Length != snapshot.MemberCount)
            return Refused(PvpRoomRefusal.InvalidSeat);
        var teams = nextMode.Reassign(players.Length);
        var seats = snapshot.Seats;
        var next = 0;
        foreach (var (index, member) in players)
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
        if (!room.Snapshot.IsPublicLobbyRoom) return Refused(PvpRoomRefusal.UnsupportedMode);
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
        if (snapshot.IsMatchmaking) return Refused(PvpRoomRefusal.NotWaiting);
        // The reference consumes readiness without starting a match in a practice room.
        if (snapshot.IsPractice) return ValueTask.FromResult<PvpRoomOutcome>(new PvpRoomOutcome.Ignored());
        var self = (PvpSeat.Taken)snapshot.Seats[own];
        if (self.Observation is not null && own != snapshot.OwnerSeat)
            return ValueTask.FromResult<PvpRoomOutcome>(new PvpRoomOutcome.Ignored());
        if (own != snapshot.OwnerSeat || !ready)
            return Changed(pending, room, snapshot with { Seats = snapshot.Seats.SetItem(own, self with { Ready = ready }) },
                PvpRoomChangeKind.ReadyChanged,
                readyConfirmationSeat: snapshot.Mode == PvpBattleMode.TotalMatch && self.Ready == ready ? own : null);

        var members = snapshot.PlayingMembers.ToArray();
        // Spec RQ-042: a battle started after shutdown cancelled the others could not end before the connections go.
        if (pending.Closing) return Refused(PvpRoomRefusal.ShuttingDown);
        var mode = rules.BattleMode(snapshot.Mode);
        if (mode.RefuseStart([.. members.Select(x => new PvpRoomMemberFacts(
                x.Member.Team, x.Member.Ready, x.Seat == snapshot.OwnerSeat))]) is { } refusal)
            return Refused(refusal);

        if (mode is IPvpTotalMatchBattleRules)
            return StartTotalMatchAsync(pending, room, self, members, cancellationToken);

        var startedAt = Now;
        return StartMatch(pending, room, WithOwnerReady(snapshot, self), members, startedAt,
            startedAt + battles.BattleTimeLimit(snapshot.Mode) + rules.FightOverrun, mode.Start(startedAt));
    }

    private async ValueTask<PvpRoomOutcome> StartTotalMatchAsync(
        Changes pending, Room room, PvpSeat.Taken owner, (int Seat, PvpSeat.Taken Member)[] members,
        CancellationToken cancellationToken)
    {
        if (room.Snapshot.Channel.Kind != ChannelEntryKind.FreeDuel)
            return Refuse(PvpRoomRefusal.UnsupportedMode);

        var preparedAt = Now;
        var sides = ImmutableArray.CreateBuilder<PvpTotalMatchSide>(members.Length);
        foreach (var (seat, member) in members)
        {
            var lineup = await totalMatchCandidates.ReadAsync(member.Session, preparedAt, cancellationToken)
                .ConfigureAwait(false);
            if (lineup is null) return Refuse(PvpRoomRefusal.InvalidTeam);
            sides.Add(new(seat, lineup));
        }
        return await StartTotalMatch(pending, room, WithOwnerReady(room.Snapshot, owner), members,
            sides.MoveToImmutable(), preparedAt).ConfigureAwait(false);
    }

    private ValueTask<PvpRoomOutcome> StartTotalMatch(
        Changes pending, Room room, PvpRoomSnapshot snapshot, (int Seat, PvpSeat.Taken Member)[] members,
        ImmutableArray<PvpTotalMatchSide> sides, DateTimeOffset preparedAt)
    {
        var startedAt = Now;
        var (state, deadline) = rules.TotalMatch.Open(sides, preparedAt, startedAt, snapshot.IsMatchmaking,
            rules.HeartbeatTimeout, battles.BattleTimeLimit(PvpBattleMode.TotalMatch) + rules.FightOverrun);
        return StartMatch(pending, room, snapshot, members, startedAt, deadline, state);
    }

    private static PvpRoomSnapshot WithOwnerReady(PvpRoomSnapshot snapshot, PvpSeat.Taken owner) =>
        snapshot with { Seats = snapshot.Seats.SetItem(snapshot.OwnerSeat, owner with { Ready = owner.Observation is null }) };

    private ValueTask<PvpRoomOutcome> StartMatch(
        Changes pending, Room room, PvpRoomSnapshot snapshot, (int Seat, PvpSeat.Taken Member)[] members,
        DateTimeOffset startedAt, DateTimeOffset phaseDeadline, PvpMatchModeState? modeState)
    {
        var match = PvpMatchId.New();
        // Room starts and matched admission share the same battle clock once all fighters may play.
        return Changed(pending, room, snapshot with
        {
            Phase = PvpRoomPhase.Fighting,
            Match = new PvpMatch(
                match,
                [.. members.Select(x => new PvpFighter(x.Seat, x.Member.CharacterId, x.Member.Session, x.Member.Team)
                    { LastHeartbeat = startedAt })],
                rules.StartMap(snapshot.MapIndex, match.Value.GetHashCode()),
                startedAt,
                phaseDeadline,
                modeState),
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
        if (!room.Snapshot.IsPublicLobbyRoom) return new PvpInviteOutcome.Refused(PvpRoomRefusal.UnsupportedMode);
        if (room.Snapshot.Phase != PvpRoomPhase.Waiting) return new PvpInviteOutcome.Refused(PvpRoomRefusal.NotWaiting);
        if (invitee.CharacterId == inviter.CharacterId || invitee.ListenerPort != inviter.ListenerPort
            || !sessions.IsInWorld(invitee))
            return new PvpInviteOutcome.Refused(PvpRoomRefusal.TargetUnavailable);
        if (pending.FindSeated(invitee.CharacterId) is not null) return new PvpInviteOutcome.Refused(PvpRoomRefusal.TargetSeated);
        if (InParty(invitee.CharacterId)) return new PvpInviteOutcome.Refused(PvpRoomRefusal.TargetInParty);
        if (!lobby.ReadyOn(invitee.ListenerPort, inviter.CharacterId)
                .Any(x => x.CharacterId == invitee.CharacterId && (x.Session is null || x.Session == invitee)))
            return new PvpInviteOutcome.Refused(PvpRoomRefusal.TargetNotInLobby);
        if (!HasRoomForGuest(room.Snapshot)) return new PvpInviteOutcome.Refused(PvpRoomRefusal.Full);
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
        if (room.Snapshot.IsPractice) return Refuse(PvpRoomRefusal.RoomMissing);
        if (pending.FindSeated(session.CharacterId) is not null) return Refuse(PvpRoomRefusal.AlreadySeated);
        if (InParty(session.CharacterId)) return Refuse(PvpRoomRefusal.InParty);
        var snapshot = room.Snapshot;
        if (snapshot.Phase != PvpRoomPhase.Waiting) return Refuse(PvpRoomRefusal.NotWaiting);
        if (checkPassword && room.Password is { } expected
            && !(password is { } offered && offered.AsSpan().SequenceEqual(expected.AsSpan())))
            return Refuse(PvpRoomRefusal.WrongPassword);
        if (!HasRoomForGuest(snapshot)) return Refuse(PvpRoomRefusal.Full);
        var mode = rules.BattleMode(snapshot.Mode);
        var seat = snapshot.Seats.IndexOf(PvpSeat.OpenSeat);
        var team = mode.TeamForNewcomer([.. snapshot.PlayingMembers.Select(x => x.Member.Team)]);
        var town = new TownMoves(snapshot.Channel.ChannelId, leaving: [session], returning: []);
        var after = pending.Put(snapshot.Id, room,
            room with { Snapshot = snapshot with { Seats = snapshot.Seats.SetItem(seat, new PvpSeat.Taken(session, team, false)) } },
            town)!;
        return new PvpRoomOutcome.Changed(kind, snapshot, after.Snapshot, [], town);
    }

    private bool HasRoomForGuest(PvpRoomSnapshot room) =>
        room.MemberCount < rules.BattleMode(room.Mode).MaximumMembers && room.Seats.Contains(PvpSeat.OpenSeat);

    /// <summary>Takes a member off a seat; the owner's seat passes on, and the last one out ends the room.</summary>
    private PvpRoomOutcome Release(Changes pending, Room room, int seat, PvpSeatReleaseReason reason) =>
        Stage(pending, room, room.Snapshot,
            reason == PvpSeatReleaseReason.Left ? PvpRoomChangeKind.Left : PvpRoomChangeKind.Kicked, (seat, reason));

    public ValueTask<PvpRoomOutcome> HeartbeatAsync(SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(HeartbeatAsync));
        ArgumentNullException.ThrowIfNull(session);
        return Battle(Pending, session, PvpRoomPhase.Fighting, nameof(HeartbeatAsync), (pending, room, match, index) =>
        {
            var now = Now;
            var fighter = match.Fighters[index];
            var next = room.Snapshot.IsMatchmaking
                ? rules.TotalMatch.ReportOpeningHeartbeat(match, fighter.Seat, now) : match;
            next = next with { Fighters = next.Fighters.SetItem(index, fighter with { LastHeartbeat = now }) };
            return Stage(pending, room, room.Snapshot with { Match = next }, PvpRoomChangeKind.Heartbeat);
        });
    }

    public ValueTask<PvpRoomOutcome> RequestRelayFightAsync(SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(RequestRelayFightAsync));
        ArgumentNullException.ThrowIfNull(session);
        return Battle(Pending, session, PvpRoomPhase.Fighting, nameof(RequestRelayFightAsync), (pending, room, match, index) =>
        {
            var fighter = match.Fighters[index];
            var (state, refusal) = rules.BattleMode(room.Snapshot.Mode).RequestNextTurn(match, fighter);
            if (state is null) return Ignore(nameof(RequestRelayFightAsync), session, refusal);
            return Stage(pending, room, room.Snapshot with { Match = match with { ModeState = state } },
                PvpRoomChangeKind.RelayRequestChanged) with { RelayRequestSeat = fighter.Seat };
        });
    }

    public ValueTask<PvpRoomOutcome> SelectTotalMatchCandidateAsync(
        SelectedSessionIdentity session, byte ordinal, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(SelectTotalMatchCandidateAsync));
        return Battle(Pending, session, PvpRoomPhase.Fighting, nameof(SelectTotalMatchCandidateAsync),
            (pending, room, match, index) =>
            {
                var (next, refusal) = rules.TotalMatch.Choose(match, match.Fighters[index].Seat, ordinal, Now);
                return next is null
                    ? Ignore(nameof(SelectTotalMatchCandidateAsync), session, refusal)
                    : Stage(pending, room, room.Snapshot with { Match = next }, PvpRoomChangeKind.TotalMatchChoiceChanged);
            });
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
            var mode = rules.BattleMode(room.Snapshot.Mode);
            var now = Now;
            if (mode.RefuseDeath(match, victim, now) is { } refusal)
                return Ignore(nameof(ReportDeathAsync), session, refusal);
            int? creditedIndex = null;
            // Only a live enemy of this battle earns the kill; anything else the client names is a death alone.
            var by = -1;
            for (var candidate = 0; candidate < match.Fighters.Length; candidate++)
                if (killer is { } named && match.Fighters[candidate].CharacterId == named)
                    by = candidate;
            if (by >= 0 && by != index && match.Fighters[by] is { Alive: true, Departed: false } scorer
                && (scorer.Team == PvpTeam.Individual || scorer.Team != victim.Team)
                && mode.IsActive(match, scorer))
                creditedIndex = by;
            var next = mode.RecordDeath(match, index, creditedIndex, now);
            return Stage(pending, room, room.Snapshot with { Match = next }, PvpRoomChangeKind.Died)
                with { Death = new PvpDeath(victim.Seat, creditedIndex is { } killerIndex ? match.Fighters[killerIndex].Seat : null) };
        });
    }

    public ValueTask<PvpRoomOutcome> ReportTimeoutAsync(
        SelectedSessionIdentity session, ImmutableArray<byte> clientValues, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(ReportTimeoutAsync));
        ArgumentNullException.ThrowIfNull(session);
        return Battle(Pending, session, PvpRoomPhase.Fighting, nameof(ReportTimeoutAsync), (pending, room, match, _) =>
        {
            var mode = rules.BattleMode(room.Snapshot.Mode);
            var (end, refusal) = mode.ReportTimeout(match, Now, battles, rules.TimeoutReportLead);
            if (end is null) return Ignore(nameof(ReportTimeoutAsync), session, refusal);
            logger.LogInformation("Duel battle {Match} timed out on character {Character}'s report {Values}; drawn",
                match.Id.Value, session.CharacterId.Value, clientValues.IsDefault ? "" : Convert.ToHexString(clientValues.AsSpan()));
            return Stage(pending, room, Conclude(room.Snapshot, end), PvpRoomChangeKind.TimedOut);
        });
    }

    public ValueTask<PvpRoomOutcome> AcknowledgeRankAsync(SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(AcknowledgeRankAsync));
        ArgumentNullException.ThrowIfNull(session);
        return Report(Pending, session, PvpRoomPhase.AwaitRank, PvpFighterReports.RankAcknowledged,
            PvpRoomChangeKind.RankAcknowledged);
    }

    public ValueTask<PvpRoomOutcome> AcknowledgeResultAsync(SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(AcknowledgeResultAsync));
        ArgumentNullException.ThrowIfNull(session);
        var pending = Pending;
        if (Acting(pending, session) is (var room, var seat)
            && room.Snapshot is { Phase: PvpRoomPhase.AwaitResult, Match: { } match }
            && room.Snapshot.Seats[seat] is PvpSeat.Taken { Observation: not null })
        {
            if (match.ObserverResultAcknowledgements.Contains(seat))
                return ValueTask.FromResult<PvpRoomOutcome>(new PvpRoomOutcome.Ignored());
            return Changed(pending, room, room.Snapshot with
            {
                Match = match with { ObserverResultAcknowledgements = match.ObserverResultAcknowledgements.Add(seat) },
            }, PvpRoomChangeKind.ResultAcknowledged);
        }
        return Report(pending, session, PvpRoomPhase.AwaitResult, PvpFighterReports.ResultAcknowledged,
            PvpRoomChangeKind.ResultAcknowledged);
    }

    public ValueTask<PvpRoomOutcome> DepartAsync(
        SelectedSessionIdentity session, PvpDepartureReason reason, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(DepartAsync));
        ArgumentNullException.ThrowIfNull(session);
        var pending = Pending;
        pending.RemoveMatchTicket(session);
        if (pending.FindSeated(session.CharacterId) is not { } room
            || room.Snapshot.SeatOf(session.CharacterId) is not { } seat
            || ((PvpSeat.Taken)room.Snapshot.Seats[seat]).Session != session)
            return Refused(PvpRoomRefusal.NotSeated);
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason), reason, null);
        var snapshot = room.Snapshot;
        if (snapshot.Admission is not null)
            return ValueTask.FromResult<PvpRoomOutcome>(
                CloseMatchedRoom(pending, room, PvpRoomChangeKind.MatchAdmissionCancelled, session));
        var next = snapshot;
        if (snapshot.Match is { } match && match.FighterOf(session) is { } index && !match.Fighters[index].Departed)
        {
            var fighter = match.Fighters[index];
            next = snapshot.Phase switch
            {
                // Spec SA-1: the fight is on from the start, so leaving at any point before its end is fleeing it.
                PvpRoomPhase.Fighting => snapshot with { Match = match with { Fighters = match.Fighters.SetItem(index, Flee(fighter)) } },
                // The result is frozen: leaving now only stops the screens from waiting for this fighter.
                PvpRoomPhase.AwaitRank or PvpRoomPhase.AwaitResult =>
                    snapshot with { Match = match with { Fighters = match.Fighters.SetItem(index, fighter with { Departed = true }) } },
                PvpRoomPhase.Waiting => throw new InvalidOperationException("A waiting duel room has no battle."),
            };
        }
        return ValueTask.FromResult<PvpRoomOutcome>(
            Stage(pending, room, next, PvpRoomChangeKind.Departed, (seat, PvpSeatReleaseReason.Departed)));
    }

    public bool RecordDailyHealth(SelectedSessionIdentity session, IReadOnlyList<ushort> missionIds)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(RecordDailyHealth));
        var pending = Pending;
        if (Acting(pending, session) is not (var room, var seat)
            || room.Snapshot is not { Channel.SupportsMatchmaking: true, IsMatchmaking: true, Match: { } match }
            || room.Snapshot.Phase is not (PvpRoomPhase.Fighting or PvpRoomPhase.AwaitRank)
            || match.FighterOf(session) is not { } index || match.Fighters[index].Departed || match.Fighters[index].Fled)
            return false;
        if (room.Snapshot.Phase == PvpRoomPhase.Fighting
            && !rules.BattleMode(room.Snapshot.Mode).IsActive(match, match.Fighters[index]))
            return false;

        var previous = match.DailyHealthReports.GetValueOrDefault(seat) ?? ImmutableDictionary<ushort, int>.Empty;
        var next = previous;
        var opponents = match.Fighters.Count(fighter => fighter.Team != match.Fighters[index].Team);
        foreach (var (missionId, limit) in missionRules.HealthReportLimits(room.Snapshot.Channel,
                     room.Snapshot.Mode, opponents, missionIds))
        {
            var count = next.GetValueOrDefault(missionId);
            if (count < limit) next = next.SetItem(missionId, count + 1);
        }
        if (ReferenceEquals(next, previous)) return false;
        var reports = match.DailyHealthReports.SetItem(seat, next);
        var snapshot = room.Snapshot with { Match = match with { DailyHealthReports = reports } };
        pending.Put(snapshot.Id, room, room with { Snapshot = snapshot }, new TownMoves(snapshot.Channel.ChannelId, [], []));
        if (pending.FindConclusion(match.Id) is { } conclusion)
            pending.PutConclusion(match.Id, conclusion with { DailyHealthReports = reports });
        return true;
    }

    public bool RecordDailyWinPose(SelectedSessionIdentity session, ushort missionId, byte ordinal)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(RecordDailyWinPose));
        var pending = Pending;
        if (Acting(pending, session) is not (var room, var seat)
            || room.Snapshot is not { Channel.SupportsMatchmaking: true, IsMatchmaking: true, Match: { } match }
            || room.Snapshot.Phase is not (PvpRoomPhase.Fighting or PvpRoomPhase.AwaitRank)
            || match.FighterOf(session) is not { } index || match.Fighters[index].Departed || match.Fighters[index].Fled
            || match.DailyWinPoses.ContainsKey(seat) || !missionRules.CanReportWinPose(room.Snapshot.Channel, missionId, ordinal))
            return false;
        var poses = match.DailyWinPoses.Add(seat, ordinal);
        var snapshot = room.Snapshot with { Match = match with { DailyWinPoses = poses } };
        pending.Put(snapshot.Id, room, room with { Snapshot = snapshot }, new TownMoves(snapshot.Channel.ChannelId, [], []));
        if (pending.FindConclusion(match.Id) is { } conclusion)
            pending.PutConclusion(match.Id, conclusion with { DailyWinPoses = poses });
        return true;
    }

    public async ValueTask<bool> RecordDailySkillAsync(SelectedSessionIdentity session, ushort skillId,
        CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(RecordDailySkillAsync));
        var pending = Pending;
        if (Acting(pending, session) is not (var room, var seat)
            || room.Snapshot is not { Channel.SupportsMatchmaking: true, IsMatchmaking: true,
                Phase: PvpRoomPhase.Fighting, Match: { } match }
            || match.FighterOf(session) is not { } index || match.Fighters[index].Departed || match.Fighters[index].Fled
            || !rules.BattleMode(room.Snapshot.Mode).IsActive(match, match.Fighters[index]))
            return false;

        byte job;
        IReadOnlyList<CharacterSkillState>? skills;
        if (match.ModeState is PvpTotalMatchBattle total)
        {
            var side = total.Sides.Single(entry => entry.Seat == seat);
            if (total.Phase != PvpTotalMatchPhase.Fighting || total.RoundOpensAt > Now || side.Choice is not { } choice)
                return false;
            var candidate = side.Lineup.Candidates[choice];
            job = candidate.Character.Identity.Job;
            skills = candidate.DuelSkills.Skills;
        }
        else
        {
            var facts = await skillStates.ReadAsync(session.CharacterId, cancellationToken, SkillGroup.Duel).ConfigureAwait(false);
            if (facts is null) return false;
            var builds = await skillStates.BuildsAsync(session.CharacterId, cancellationToken, SkillGroup.Duel).ConfigureAwait(false);
            job = facts.Career.Job;
            skills = facts.Pages.FirstOrDefault(page => page.PageIndex == builds.Active.Value)?.Skills;
        }
        if (skills is null || !skills.Any(skill => skill.SkillId == skillId && skill.Level > 0)
            || skillRules.Get(job, skillId) is not { ConsumesColorlessCube: true })
            return false;

        var count = match.DailyColorlessSkills.GetValueOrDefault(seat);
        if (count >= byte.MaxValue) return false;
        var updated = match with { DailyColorlessSkills = match.DailyColorlessSkills.SetItem(seat, count + 1) };
        var snapshot = room.Snapshot with { Match = updated };
        pending.Put(snapshot.Id, room, room with { Snapshot = snapshot }, new TownMoves(snapshot.Channel.ChannelId, [], []));
        return true;
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
        if (pending.Find(room) is not { } current) return ValueTask.FromResult<PvpRoomOutcome.Changed?>(null);
        var snapshot = current.Snapshot;
        if (snapshot.Admission is { } admission && admission.Deadline <= now)
            return ValueTask.FromResult<PvpRoomOutcome.Changed?>(CloseMatchedRoom(pending, current, PvpRoomChangeKind.MatchAdmissionCancelled));
        if (snapshot.Match is not { } match) return ValueTask.FromResult<PvpRoomOutcome.Changed?>(null);
        PvpRoomOutcome.Changed? Advanced(PvpRoomSnapshot next) => Stage(pending, current, next, PvpRoomChangeKind.DeadlinePassed);
        var changed = snapshot.Phase switch
        {
            PvpRoomPhase.AwaitResult => match.PhaseDeadline <= now ? Advanced(Waiting(snapshot)) : null,
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
        var mode = rules.BattleMode(snapshot.Mode);
        var current = silent ? match with { Fighters = fighters } : match;
        var advanced = mode.AdvanceTurn(current, now, battles, rules.FightOverrun);
        var overrun = advanced is null && mode.Deadline(current) <= now;
        if (!silent && !overrun && advanced is null) return null;
        var next = snapshot with { Match = advanced ?? current };
        return overrun && mode.Watchdog(current, Now) is { } end
            ? Conclude(next, end)
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
            .Where(room => room.Snapshot.Admission is not null || room.Snapshot.Match is { Terminal: null })
            .Select(room => room.Snapshot.Admission is not null
                ? CloseMatchedRoom(pending, room, PvpRoomChangeKind.MatchAdmissionCancelled)
                : Stage(pending, room, Waiting(room.Snapshot), PvpRoomChangeKind.CancelledForShutdown))
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
            Match = match with { PhaseDeadline = Now + rules.ResultTimeout(room.Snapshot.Origin), Results = [.. results] },
        }, PvpRoomChangeKind.Settled);
    }

    /// <summary>
    /// A phase report: recorded once per fighter; what follows once every fighter left in the battle made it is
    /// <see cref="Stage"/>'s.
    /// </summary>
    private ValueTask<PvpRoomOutcome> Report(Changes pending, SelectedSessionIdentity session, PvpRoomPhase phase,
        PvpFighterReports report, PvpRoomChangeKind kind) =>
        Battle(pending, session, phase, kind.ToString(), (_, room, match, index) =>
        {
            var fighter = match.Fighters[index];
            if (fighter.Reports.HasFlag(report)) return Ignore(kind.ToString(), session, PvpRoomRefusal.AlreadyReported);
            var fighters = match.Fighters.SetItem(index, fighter with { Reports = fighter.Reports | report });
            return Stage(pending, room, room.Snapshot with { Match = match with { Fighters = fighters } }, kind);
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
        Match = rules.TotalMatch.RecordAppearances(snapshot.Match!, end.At) with
        {
            Terminal = end,
            PhaseDeadline = Now + rules.RankTimeout,
        },
    };

    /// <summary>When the server next has to advance the room itself; a battle on its ranking screen is due through its conclusion.</summary>
    private DateTimeOffset? DueOf(PvpRoomSnapshot? snapshot)
    {
        if (snapshot?.Admission is { } admission) return admission.Deadline;
        if (snapshot?.Match is not { } match) return null;
        return snapshot.Phase switch
        {
            PvpRoomPhase.AwaitResult => match.PhaseDeadline,
            PvpRoomPhase.Fighting => match.Fighters.Where(fighter => !fighter.Departed)
                .Select(fighter => fighter.LastHeartbeat + rules.HeartbeatTimeout)
                .Append(rules.BattleMode(snapshot.Mode).Deadline(match)).Min(),
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
        if (next.Phase == PvpRoomPhase.Fighting && next.Match is { } fight)
        {
            var mode = rules.BattleMode(next.Mode);
            var now = Now;
            if (mode.TerminalOf(fight, now) is { } end)
                next = Conclude(next, end);
            else
                next = next with { Match = mode.AfterNonterminalChange(fight, now) };
        }
        if (next.Match is { } match)
        {
            if (match.Terminal is { } terminal && before.Match?.Terminal is null)
            {
                concluded = new PvpMatchConclusion(match.Id, next.Id, next.Number, next.Channel, next.Mode, match.StartedAt,
                    terminal, match.Fighters, match.PhaseDeadline)
                {
                    TotalMatchAppearances = rules.TotalMatch.AppearancesOf(match.ModeState),
                    DailyHealthReports = match.DailyHealthReports,
                    DailyColorlessSkills = match.DailyColorlessSkills,
                    DailyWinPoses = match.DailyWinPoses,
                    Origin = next.Origin,
                    ColosseumRuns = next.ColosseumRuns,
                };
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

        if (next.IsMatchmaking && next.Phase == PvpRoomPhase.Waiting)
            return CloseMatchedRoom(pending, room, kind, vacate is { Reason: PvpSeatReleaseReason.Departed } departure
                ? ((PvpSeat.Taken)before.Seats[departure.Seat]).Session : null);

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

    private static ValueTask<PvpRoomOutcome> Changed(Changes pending, Room room, PvpRoomSnapshot next, PvpRoomChangeKind kind,
        int? readyConfirmationSeat = null)
    {
        var town = new TownMoves(room.Snapshot.Channel.ChannelId, leaving: [], returning: []);
        var after = pending.Put(room.Snapshot.Id, room, room with { Snapshot = next }, town)!;
        return ValueTask.FromResult<PvpRoomOutcome>(new PvpRoomOutcome.Changed(kind, room.Snapshot, after.Snapshot, [], town)
        {
            ReadyConfirmationSeat = readyConfirmationSeat,
        });
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
        private (ImmutableArray<PvpMatchmakingTicket> Before, ImmutableArray<PvpMatchmakingTicket> After)? queue;
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

        public void Close()
        {
            closing = true;
            MatchmakingQueue = [];
        }

        public ImmutableArray<PvpMatchmakingTicket> MatchmakingQueue
        {
            get
            {
                if (queue is { } staged) return staged.After;
                lock (owner.gate) return owner.matchmakingQueue;
            }
            set
            {
                if (queue is { } staged) queue = (staged.Before, value);
                else lock (owner.gate) queue = (owner.matchmakingQueue, value);
            }
        }

        public void RemoveMatchTicket(SelectedSessionIdentity session, PvpBattleMode? mode = null)
        {
            var current = MatchmakingQueue;
            if (current.FirstOrDefault(ticket => ticket.Session == session && (mode is null || ticket.Mode == mode)) is { } ticket)
                MatchmakingQueue = current.Remove(ticket);
        }

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
            Visible(listenerPort).FirstOrDefault(room => room.Snapshot.Number == number && !room.Snapshot.IsMatchmaking);

        public Room? FindQuickJoinable(int listenerPort) => Visible(listenerPort)
            .Where(room => room.Snapshot.IsPublicLobbyRoom && room.Password is null && room.Snapshot.Phase == PvpRoomPhase.Waiting
                && owner.HasRoomForGuest(room.Snapshot))
            .MinBy(room => room.Snapshot.Number);

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
                if (queue is { } staged && staged.Before != owner.matchmakingQueue)
                    throw new InvalidOperationException("The duel matchmaking queue changed before commit.");
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
                if (queue is { } staged) owner.matchmakingQueue = staged.After;
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
