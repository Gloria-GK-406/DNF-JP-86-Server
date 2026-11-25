using System.Collections.Immutable;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Pvp;

internal sealed partial class PvpRoomDomainService
{
    private ImmutableArray<MatchTicket> matchmakingQueue = [];

    private sealed record MatchTicket(SelectedSessionIdentity Session, PvpBattleMode Mode, CharacterCareer Career,
        ImmutableArray<CharacterCareer> ExcludedCareers, Guid? ColosseumRun);

    public async ValueTask<PvpMatchmakingQueueResult> EnqueueAsync(
        SelectedSessionIdentity session, PvpBattleMode mode, ImmutableArray<CharacterCareer> excludedCareers,
        CancellationToken cancellationToken, bool colosseumQueue = false)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(EnqueueAsync));
        var pending = Pending;
        var fighterCount = PvpMatchAdmission.FighterCount(mode);
        // Keep each player's two rating updates in fight order even when that player reconnects before settlement.
        var awaitingRating = pending.Conclusions().Where(matchmakingRules.AppliesTo)
            .SelectMany(battle => battle.Fighters).Select(fighter => fighter.CharacterId).ToHashSet();
        var awaitingGroupRating = pending.Conclusions().Where(matchmakingRules.AppliesToTotalMatch)
            .SelectMany(battle => battle.Fighters).Select(fighter => fighter.Session.AccountId).ToHashSet();
        if (fighterCount == 0) return new(PvpMatchmakingDecision.UnsupportedMode);
        if (pending.Closing) return new(PvpMatchmakingDecision.ServerClosing);
        if (awaitingRating.Contains(session.CharacterId)) return new(PvpMatchmakingDecision.PriorResultPending);
        if (!CanQueue(pending, session)) return new(PvpMatchmakingDecision.UnavailableInLobby);
        if (await MatchmakingChannelAsync(session, cancellationToken).ConfigureAwait(false) is not { } channel)
            return new(PvpMatchmakingDecision.ChannelUnavailable);
        if (!matchmakingRules.AllowsOrdinaryQueue(channel, mode))
        {
            if (!matchmakingRules.AllowsTotalMatchQueue(channel, mode))
                return new(PvpMatchmakingDecision.ModeUnavailableInChannel);
            if (awaitingGroupRating.Contains(session.AccountId))
                return new(PvpMatchmakingDecision.PriorResultPending);
        }

        Guid? colosseumRunId = null;
        if (colosseumQueue)
        {
            if (await colosseum.ReadForQueueAsync(session, channel, cancellationToken).ConfigureAwait(false)
                    is not { Complete: false } run
                || run.Recipient != session.CharacterId
                || (run.Owner.Kind == PvpColosseumKind.TotalMatch
                    ? mode != PvpBattleMode.TotalMatch : mode != PvpBattleMode.Individual))
                return new(PvpMatchmakingDecision.ColosseumStateMismatch);
            colosseumRunId = run.Id;
        }

        var profiles = await identities.ReadAsync([session.CharacterId], cancellationToken).ConfigureAwait(false);
        if (!profiles.TryGetValue(session.CharacterId, out var profile) || profile.Job is < 0 or > byte.MaxValue)
            return new(PvpMatchmakingDecision.CharacterProfileUnavailable);

        var ticket = new MatchTicket(session, mode, new(profile.Job, profile.GrowType & 0xF), excludedCareers, colosseumRunId);
        var available = pending.MatchmakingQueue.RemoveAll(waiting =>
            awaitingRating.Contains(waiting.Session.CharacterId) || !CanQueue(pending, waiting.Session)
            || (waiting.Mode == PvpBattleMode.TotalMatch && awaitingGroupRating.Contains(waiting.Session.AccountId)));
        var needed = fighterCount - 1;
        var eligible = available.Where(waiting => waiting.Mode == mode
                && waiting.Session.ListenerPort == session.ListenerPort && waiting.Session.AccountId != session.AccountId
                && waiting.Session.CharacterId != session.CharacterId && waiting.Session.WireUserId != session.WireUserId
                && !ticket.ExcludedCareers.Contains(waiting.Career) && !waiting.ExcludedCareers.Contains(ticket.Career))
            .DistinctBy(waiting => waiting.Session.AccountId);
        var preparedAt = Now;
        ImmutableArray<PvpTotalMatchSide> sides = [];
        MatchTicket[] opponents;
        if (mode == PvpBattleMode.TotalMatch)
        {
            var (own, missingEquipment) = await traditional.ReadMatchmakingLineupAsync(session, preparedAt, cancellationToken)
                .ConfigureAwait(false);
            if (own is null)
                return new(missingEquipment
                    ? PvpMatchmakingDecision.MissingTotalMatchEquipment
                    : PvpMatchmakingDecision.InvalidTotalMatchLineup);
            opponents = [];
            foreach (var waiting in eligible)
            {
                var (opponent, _) = await traditional.ReadMatchmakingLineupAsync(waiting.Session, preparedAt, cancellationToken)
                    .ConfigureAwait(false);
                if (opponent is not null)
                {
                    sides = [new(0, opponent), new(1, own)];
                    opponents = [waiting];
                    break;
                }
                available = available.Remove(waiting);
            }
        }
        else
        {
            opponents = eligible.Take(needed).ToArray();
        }
        if (opponents.Length < needed)
        {
            var existing = available.FirstOrDefault(waiting => waiting.Session == session);
            pending.MatchmakingQueue = existing is null ? available.Add(ticket) : available.Replace(existing, ticket);
            return new(PvpMatchmakingDecision.Waiting);
        }

        pending.MatchmakingQueue = available.RemoveAll(waiting => waiting.Session == session || opponents.Contains(waiting));
        var pairedTickets = opponents.Append(ticket).ToArray();
        var members = pairedTickets.Select(waiting => waiting.Session).ToArray();
        var colosseumRuns = pairedTickets.Select((waiting, seat) => (waiting.ColosseumRun, seat))
            .Where(entry => entry.ColosseumRun.HasValue)
            .ToImmutableDictionary(entry => entry.seat, entry => entry.ColosseumRun!.Value);
        var seats = Enumerable.Range(0, PvpRoomSnapshot.SeatCount).Select(seat => seat < members.Length
            ? new PvpSeat.Taken(members[seat], seat % 2 == 0 ? PvpTeam.First : PvpTeam.Second, false)
            : PvpSeat.ClosedSeat).ToImmutableArray();
        var participants = (byte)((1 << members.Length) - 1);
        var room = new PvpRoomSnapshot(PvpRoomId.New(), pending.NextNumber(channel.ListenerPort), channel,
            1, [], false, 0, mode, 0, seats, PvpRoomPhase.Waiting, 0,
            PvpRoomOrigin.Matchmaking, null)
        {
            ColosseumRuns = colosseumRuns,
            Admission = new(Now + rules.MatchAdmissionTimeout, participants)
            {
                PreparedAt = preparedAt,
                TotalMatchSides = sides,
            },
        };
        var town = new TownMoves(channel.ChannelId, leaving: members, returning: []);
        var paired = pending.Put(room.Id, null, new Room(room, null), town)!;
        return new(PvpMatchmakingDecision.Paired,
            new PvpRoomOutcome.Changed(PvpRoomChangeKind.MatchPaired, null, paired.Snapshot, [], town));
    }

    public async ValueTask<bool> CancelAsync(SelectedSessionIdentity session, PvpBattleMode mode, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(CancelAsync));
        var pending = Pending;
        if (!InWorld(session) || pending.FindSeated(session.CharacterId)?.Snapshot.IsMatchmaking == true
            || await MatchmakingChannelAsync(session, cancellationToken).ConfigureAwait(false) is not { } channel
            || !(matchmakingRules.AllowsOrdinaryQueue(channel, mode)
                || matchmakingRules.AllowsTotalMatchQueue(channel, mode)))
            return false;
        pending.RemoveMatchTicket(session, mode);
        return true;
    }

    public async ValueTask<PvpMatchmakingCounts?> WaitingCountsAsync(SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        if (!InWorld(session) || await MatchmakingChannelAsync(session, cancellationToken).ConfigureAwait(false) is not { } channel)
            return null;
        ImmutableArray<MatchTicket> queue;
        lock (gate) queue = matchmakingQueue;
        var waiting = queue.Where(ticket => ticket.Session.ListenerPort == session.ListenerPort && InWorld(ticket.Session)
            && !InParty(ticket.Session.CharacterId) && !IsSeated(ticket.Session.CharacterId)).ToArray();
        return new(waiting.Count(ticket => ticket.Mode == PvpBattleMode.Individual),
            waiting.Count(ticket => ticket.Mode == PvpBattleMode.Relay),
            channel.AllowsTotalMatchmaking ? waiting.Count(ticket => ticket.Mode == PvpBattleMode.TotalMatch) : null);
    }

    public ValueTask<PvpRoomOutcome> ConfirmOpponentAsync(
        SelectedSessionIdentity session, uint roomNumber, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(ConfirmOpponentAsync));
        var pending = Pending;
        if (Acting(pending, session) is not (var room, var seat) || room.Snapshot.Number != roomNumber
            || room.Snapshot.Admission is not { } admission || admission.Deadline <= Now)
            return Refused(PvpRoomRefusal.WrongPhase);
        var confirmed = (byte)(admission.Confirmed | (1 << seat));
        if (confirmed == admission.Confirmed) return Refused(PvpRoomRefusal.AlreadyReported);
        return Changed(pending, room, room.Snapshot with { Admission = admission with { Confirmed = confirmed } },
            PvpRoomChangeKind.MatchAdmissionChanged);
    }

    public ValueTask<PvpRoomOutcome> ReportTransportAsync(
        SelectedSessionIdentity session, byte? readyPeers, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(ReportTransportAsync));
        var pending = Pending;
        if (Acting(pending, session) is not (var room, var seat)
            || room.Snapshot.Admission is not { Preloading: true } admission || admission.Deadline <= Now)
            return Refused(PvpRoomRefusal.WrongPhase);
        var bit = (byte)(1 << seat);
        var next = readyPeers is { } peers
            ? admission.ReportLinks(seat, peers)
            : admission with { Loaded = (byte)(admission.Loaded | bit) };
        if (next == admission) return Refused(PvpRoomRefusal.AlreadyReported);
        if (next.Linked != admission.Participants)
            return Changed(pending, room, room.Snapshot with { Admission = next }, PvpRoomChangeKind.MatchAdmissionChanged);

        var snapshot = room.Snapshot with
        {
            Admission = null,
            Seats = [.. room.Snapshot.Seats.Select(initial => initial is PvpSeat.Taken member ? member with { Ready = true } : initial)],
        };
        var members = snapshot.PlayingMembers.ToArray();
        var startedAt = Now;
        if (snapshot.Mode == PvpBattleMode.TotalMatch)
            return StartTotalMatch(pending, room, snapshot, members, admission.TotalMatchSides, admission.PreparedAt);

        return StartMatch(pending, room, snapshot, members, startedAt,
            startedAt + battles.BattleTimeLimit(snapshot.Mode) + rules.FightOverrun,
            rules.BattleMode(snapshot.Mode).Start(startedAt));
    }

    public bool CanEnterColosseum(SelectedSessionIdentity session)
    {
        var pending = Pending;
        return !pending.Closing && CanQueue(pending, session)
            && !pending.MatchmakingQueue.Any(ticket => ticket.Session.AccountId == session.AccountId)
            && !pending.Conclusions().Any(battle => battle.Fighters.Any(fighter
                => fighter.Session.AccountId == session.AccountId));
    }

    private bool CanQueue(Changes pending, SelectedSessionIdentity session) =>
        InWorld(session) && online.StandingOf(session) is not null
        && !InParty(session.CharacterId) && pending.FindSeated(session.CharacterId) is null;

    private async ValueTask<ChannelEntryIdentity?> MatchmakingChannelAsync(
        SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        var channel = await channels.ResolveAsync(session.ListenerPort, cancellationToken).ConfigureAwait(false);
        return channel.SupportsMatchmaking ? channel : null;
    }

    private PvpRoomOutcome.Changed CloseMatchedRoom(Changes pending, Room room, PvpRoomChangeKind kind,
        SelectedSessionIdentity? departed = null)
    {
        var snapshot = room.Snapshot;
        var releases = snapshot.Members.Select(entry => new PvpSeatRelease(entry.Seat, entry.Member.Session,
            entry.Member.Session == departed ? PvpSeatReleaseReason.Departed : PvpSeatReleaseReason.Left)).ToArray();
        var town = new TownMoves(snapshot.Channel.ChannelId, leaving: [],
            returning: releases.Where(release => release.Reason == PvpSeatReleaseReason.Left)
                .Select(release => release.Session).ToArray());
        pending.Put(snapshot.Id, room, null, town);
        return new(kind, snapshot, null, releases, town);
    }
}
