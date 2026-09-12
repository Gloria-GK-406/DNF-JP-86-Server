using System.Collections.Immutable;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Parties;

internal sealed class PartyDomainService(
    ISelectedSessionDomainService sessions,
    ISessionPresenceDomainService presence,
    IPartyMemberStatusDomainService memberStatus,
    TimeProvider clock) : IPartyDomainService
{
    internal static readonly TimeSpan InvitationLifetime = TimeSpan.FromSeconds(30);

    private readonly object sync = new();
    private readonly Dictionary<ushort, PartyState> parties = [];
    private readonly Dictionary<ushort, PartyState> membership = [];
    private readonly Dictionary<ushort, EndpointEntry> endpoints = [];
    private readonly Dictionary<ushort, PendingInvite> invitations = [];
    private ushort nextPartyId = 1;
    private long nextGeneration;

    public PartyOperationResult UpdateEndpoint(
        SelectedSessionIdentity session,
        PartyEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.IsValid)
            return PartyOperationResult.Reject(PartyFailureReason.InvalidEndpoint);

        lock (sync)
        {
            if (!IsCurrent(session))
                return PartyOperationResult.Reject(PartyFailureReason.SessionNotCurrent);

            endpoints[session.WireUserId] = new EndpointEntry(session, endpoint);
            if (TryGetExactMember(session, out var party, out _))
                party.Revision++;
            return PartyOperationResult.Success();
        }
    }

    public PartyOperationResult RequestInvite(
        SelectedSessionIdentity inviter,
        ushort targetWireUserId,
        PartyInviteDisplayState display)
    {
        ArgumentNullException.ThrowIfNull(inviter);
        ArgumentNullException.ThrowIfNull(display);

        lock (sync)
        {
            if (!IsCurrent(inviter))
                return PartyOperationResult.Reject(PartyFailureReason.SessionNotCurrent);
            if (targetWireUserId == inviter.WireUserId)
                return PartyOperationResult.Reject(PartyFailureReason.SelfInvite);

            var target = sessions.FindByWireUserId(targetWireUserId);
            if (target is null || !IsCurrent(target))
                return PartyOperationResult.Reject(PartyFailureReason.TargetNotFound);

            var domainFailure = ValidateTownPair(inviter, target);
            if (domainFailure != PartyFailureReason.None)
                return PartyOperationResult.Reject(domainFailure);

            if (membership.ContainsKey(targetWireUserId))
                return PartyOperationResult.Reject(PartyFailureReason.TargetAlreadyInParty);

            long? inviterPartyGeneration = null;
            if (membership.TryGetValue(inviter.WireUserId, out var inviterParty))
            {
                var inviterMember = inviterParty.Find(inviter.WireUserId);
                if (inviterMember?.Session != inviter)
                    return PartyOperationResult.Reject(PartyFailureReason.SessionNotCurrent);
                if (inviterParty.LeaderWireUserId != inviter.WireUserId)
                    return PartyOperationResult.Reject(PartyFailureReason.InviterNotLeader);
                if (inviterParty.IsFull)
                    return PartyOperationResult.Reject(PartyFailureReason.PartyFull);
                inviterPartyGeneration = inviterParty.Generation;
            }

            invitations[targetWireUserId] = new PendingInvite(
                inviter,
                target,
                inviterPartyGeneration,
                clock.GetUtcNow() + InvitationLifetime);

            return PartyOperationResult.Success(new PartyNotificationPlan(
                [],
                Invitation: new PartyInviteNotification(target, inviter, display)));
        }
    }

    public PartyOperationResult AcceptInvite(
        SelectedSessionIdentity invitee,
        ushort inviterWireUserId)
    {
        ArgumentNullException.ThrowIfNull(invitee);

        lock (sync)
        {
            if (!IsCurrent(invitee))
                return PartyOperationResult.Reject(PartyFailureReason.SessionNotCurrent);
            if (!invitations.TryGetValue(invitee.WireUserId, out var invitation)
                || invitation.Invitee != invitee
                || invitation.Inviter.WireUserId != inviterWireUserId)
            {
                return PartyOperationResult.Reject(PartyFailureReason.InviteNotFoundOrStale);
            }

            if (clock.GetUtcNow() >= invitation.ExpiresAt)
                return PartyOperationResult.Reject(PartyFailureReason.InviteExpired);
            if (!IsCurrent(invitation.Inviter))
                return PartyOperationResult.Reject(PartyFailureReason.InviteNotFoundOrStale);

            var domainFailure = ValidateTownPair(invitation.Inviter, invitee);
            if (domainFailure != PartyFailureReason.None)
                return PartyOperationResult.Reject(domainFailure);
            if (membership.ContainsKey(invitee.WireUserId))
                return PartyOperationResult.Reject(PartyFailureReason.TargetAlreadyInParty);

            PartyState party;
            if (invitation.InviterPartyGeneration is { } expectedGeneration)
            {
                if (!membership.TryGetValue(inviterWireUserId, out party!)
                    || party.Generation != expectedGeneration)
                {
                    return PartyOperationResult.Reject(PartyFailureReason.PartyGenerationChanged);
                }

                var inviterMember = party.Find(inviterWireUserId);
                if (inviterMember?.Session != invitation.Inviter)
                    return PartyOperationResult.Reject(PartyFailureReason.InviteNotFoundOrStale);
                if (party.LeaderWireUserId != inviterWireUserId)
                    return PartyOperationResult.Reject(PartyFailureReason.InviterNotLeader);
                if (party.IsFull)
                    return PartyOperationResult.Reject(PartyFailureReason.PartyFull);
            }
            else
            {
                if (membership.ContainsKey(inviterWireUserId))
                    return PartyOperationResult.Reject(PartyFailureReason.PartyGenerationChanged);

                party = new PartyState(
                    AllocatePartyId(),
                    checked(++nextGeneration),
                    invitation.Inviter);
                parties.Add(party.PartyId, party);
                membership.Add(inviterWireUserId, party);
            }

            // Invalid responses must leave the pending invitation untouched. Consumption is
            // atomic with the first accepted mutation, which also makes a successful replay fail.
            invitations.Remove(invitee.WireUserId);
            if (!party.TryAdd(invitee))
                return PartyOperationResult.Reject(PartyFailureReason.PartyFull);
            membership.Add(invitee.WireUserId, party);

            var snapshot = Snapshot(party);
            return PartyOperationResult.Success(new PartyNotificationPlan(
                [],
                snapshot,
                Acceptance: new PartyAcceptanceNotification(invitation.Inviter, invitee)));
        }
    }

    public PartyOperationResult LeavePartyExact(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (sync)
        {
            if (!IsCurrent(session))
                return PartyOperationResult.Reject(PartyFailureReason.SessionNotCurrent);
            return LeaveExactLocked(session);
        }
    }

    public PartyOperationResult CleanupSessionExact(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (sync)
        {
            var changed = false;
            if (endpoints.TryGetValue(session.WireUserId, out var endpoint)
                && endpoint.Session == session)
            {
                endpoints.Remove(session.WireUserId);
                changed = true;
            }

            foreach (var targetWireUserId in invitations
                         .Where(pair => pair.Value.Inviter == session || pair.Value.Invitee == session)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                invitations.Remove(targetWireUserId);
                changed = true;
            }

            var leave = LeaveExactLocked(session);
            if (leave.Succeeded)
                return leave;
            if (changed || IsCurrent(session))
                return PartyOperationResult.Success();
            return PartyOperationResult.Reject(PartyFailureReason.SessionNotCurrent);
        }
    }

    public PartySnapshot? FindParty(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (sync)
            return TryGetExactMember(session, out var party, out _) ? Snapshot(party) : null;
    }

    public PartyEndpoint? FindEndpoint(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (sync)
            return endpoints.TryGetValue(session.WireUserId, out var endpoint)
                && endpoint.Session == session
                    ? endpoint.Endpoint
                    : null;
    }

    private PartyOperationResult LeaveExactLocked(SelectedSessionIdentity session)
    {
        if (!TryGetExactMember(session, out var party, out var member))
            return PartyOperationResult.Reject(PartyFailureReason.NotInParty);

        if (member.Slot == 0)
        {
            party.Revision++;
            var audience = party.Members
                .Where(value => value is not null)
                .Select(value => value!.Session)
                .ToImmutableArray();
            foreach (var partyMember in party.Members)
            {
                if (partyMember is not null)
                    membership.Remove(partyMember.Session.WireUserId);
            }
            parties.Remove(party.PartyId);
            return PartyOperationResult.Success(new PartyNotificationPlan(
                audience,
                ClearPartyId: party.PartyId,
                ClearPartyGeneration: party.Generation,
                ClearPartyRevision: party.Revision));
        }

        party.Members[member.Slot] = null;
        party.Revision++;
        membership.Remove(session.WireUserId);
        return PartyOperationResult.Success(new PartyNotificationPlan(
            [session],
            Snapshot(party),
            ClearPartyId: party.PartyId,
            ClearPartyGeneration: party.Generation,
            ClearPartyRevision: party.Revision));
    }

    private PartyFailureReason ValidateTownPair(
        SelectedSessionIdentity inviter,
        SelectedSessionIdentity target)
    {
        if (inviter.ListenerPort <= 0
            || target.ListenerPort <= 0
            || inviter.ListenerPort != target.ListenerPort)
        {
            return PartyFailureReason.NotInSameGameChannel;
        }

        var inviterPresence = presence.Find(inviter.CharacterId);
        var targetPresence = presence.Find(target.CharacterId);
        if (inviterPresence?.Session != inviter
            || targetPresence?.Session != target
            || !inviterPresence.StandingInTown
            || !targetPresence.StandingInTown)
        {
            return PartyFailureReason.NotStandingInTown;
        }
        if (inviterPresence.ListenerPort != inviter.ListenerPort
            || targetPresence.ListenerPort != target.ListenerPort)
        {
            return PartyFailureReason.NotInSameGameChannel;
        }
        if (inviterPresence.TownId != targetPresence.TownId
            || inviterPresence.AreaId != targetPresence.AreaId)
        {
            return PartyFailureReason.NotInSameTownArea;
        }
        return PartyFailureReason.None;
    }

    private bool IsCurrent(SelectedSessionIdentity session) =>
        sessions.Find(session.AccountId) == session
        && sessions.FindByWireUserId(session.WireUserId) == session;

    private bool TryGetExactMember(
        SelectedSessionIdentity session,
        out PartyState party,
        out PartyMemberState member)
    {
        if (membership.TryGetValue(session.WireUserId, out party!))
        {
            var found = party.Find(session.WireUserId);
            if (found?.Session == session)
            {
                member = found;
                return true;
            }
        }
        member = null!;
        return false;
    }

    private PartySnapshot Snapshot(PartyState party)
    {
        var members = ImmutableArray.CreateBuilder<PartyMemberSnapshot>(PartyState.Capacity);
        foreach (var member in party.Members)
        {
            if (member is null)
                continue;
            var endpoint = endpoints.TryGetValue(member.Session.WireUserId, out var registered)
                && registered.Session == member.Session
                    ? registered.Endpoint
                    : null;
            members.Add(new PartyMemberSnapshot(
                member.Session,
                member.Slot,
                endpoint,
                memberStatus.CurrentHpPercentOf(member.Session)));
        }
        return new PartySnapshot(
            party.PartyId,
            party.Generation,
            party.Revision,
            party.LeaderWireUserId,
            members.ToImmutable());
    }

    private ushort AllocatePartyId()
    {
        for (var attempts = 0; attempts < ushort.MaxValue - 1; attempts++)
        {
            var candidate = nextPartyId++;
            if (nextPartyId is 0 or ushort.MaxValue)
                nextPartyId = 1;
            if (candidate is not 0 and not ushort.MaxValue && !parties.ContainsKey(candidate))
                return candidate;
        }
        throw new InvalidOperationException("No party identifier is available.");
    }

    private sealed class PartyState
    {
        internal const int Capacity = 4;

        internal PartyState(ushort partyId, long generation, SelectedSessionIdentity leader)
        {
            PartyId = partyId;
            Generation = generation;
            Members[0] = new PartyMemberState(leader, 0);
        }

        internal ushort PartyId { get; }
        internal long Generation { get; }
        internal long Revision { get; set; } = 1;
        internal ushort LeaderWireUserId => Members[0]!.Session.WireUserId;
        internal PartyMemberState?[] Members { get; } = new PartyMemberState?[Capacity];
        internal bool IsFull => Members.All(member => member is not null);

        internal PartyMemberState? Find(ushort wireUserId) =>
            Members.FirstOrDefault(member => member?.Session.WireUserId == wireUserId);

        internal bool TryAdd(SelectedSessionIdentity session)
        {
            for (byte slot = 1; slot < Capacity; slot++)
            {
                if (Members[slot] is not null)
                    continue;
                Members[slot] = new PartyMemberState(session, slot);
                Revision++;
                return true;
            }
            return false;
        }
    }

    private sealed record PartyMemberState(SelectedSessionIdentity Session, byte Slot);
    private sealed record EndpointEntry(SelectedSessionIdentity Session, PartyEndpoint Endpoint);
    private sealed record PendingInvite(
        SelectedSessionIdentity Inviter,
        SelectedSessionIdentity Invitee,
        long? InviterPartyGeneration,
        DateTimeOffset ExpiresAt);
}
