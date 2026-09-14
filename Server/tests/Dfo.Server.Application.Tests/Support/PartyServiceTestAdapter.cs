using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>
/// Test-side application adapter that preserves the old exact-session scenarios while proving
/// that current-connection validation and presence projection now happen outside PartyService.
/// </summary>
internal sealed class PartyServiceTestAdapter
{
    private readonly SelectedSessionDomainService sessions;
    private readonly SessionPresenceDomainService presence;
    private readonly TestSessionEndpoints endpoints;
    private readonly PartyService inner;

    internal PartyServiceTestAdapter(
        SelectedSessionDomainService sessions,
        SessionPresenceDomainService presence,
        IPartyMemberStatusDomainService memberStatus,
        IPartyModeRuleService rules,
        TimeProvider clock)
    {
        this.sessions = sessions;
        this.presence = presence;
        endpoints = new TestSessionEndpoints(sessions);
        inner = new PartyService(memberStatus, rules, clock, endpoints);
    }

    internal void UpdateDirectoryPresence(CharacterId characterId, PartyDirectoryScope? scope) =>
        inner.UpdateDirectoryPresence(characterId, scope);

    internal IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query) => inner.Query(query);

    internal PartyOperationResult CreateOrUpdateSettingsExact(
        SelectedSessionIdentity session,
        PartySettings settings,
        PartyTitle title) =>
        TryProject(session, out var character)
            ? inner.CreateOrUpdateSettings(character!, settings, title)
            : PartyOperationResult.Reject(PartyFailureReason.CallerNotCurrent);

    internal PartyOperationResult ObserveSessionEndpoint(
        SelectedSessionIdentity session,
        SessionEndpoint endpoint) =>
        !IsCurrent(session)
            ? PartyOperationResult.Reject(PartyFailureReason.CallerNotCurrent)
            : !endpoint.IsValid
                ? PartyOperationResult.Reject(PartyFailureReason.InvalidEndpoint)
                : endpoints.Observe(session, endpoint);

    internal PartyOperationResult RequestInvite(
        SelectedSessionIdentity inviter,
        ushort targetWireUserId,
        PartyInviteDisplayState display)
    {
        var target = sessions.FindByWireUserId(targetWireUserId);
        return target is not null
            && TryProject(inviter, out var inviterCharacter)
            && TryProject(target, out var targetCharacter)
                ? inner.RequestInvite(inviterCharacter!, targetCharacter!, display)
                : PartyOperationResult.Reject(PartyFailureReason.CallerNotCurrent);
    }

    internal PartyOperationResult AcceptInvite(
        SelectedSessionIdentity invitee,
        ushort inviterWireUserId)
    {
        var inviter = sessions.FindByWireUserId(inviterWireUserId);
        return inviter is not null
            && TryProject(invitee, out var inviteeCharacter)
            && TryProject(inviter, out var inviterCharacter)
                ? inner.AcceptInvite(inviteeCharacter!, inviterCharacter!)
                : PartyOperationResult.Reject(PartyFailureReason.InviteNotFoundOrStale);
    }

    internal PartyOperationResult LeavePartyExact(SelectedSessionIdentity session) =>
        IsCurrent(session)
            ? inner.LeaveParty(session.CharacterId)
            : PartyOperationResult.Reject(PartyFailureReason.CallerNotCurrent);

    internal PartyOperationResult CleanupSessionExact(SelectedSessionIdentity session) =>
        IsCurrent(session)
            ? inner.CleanupCharacter(session.CharacterId)
            : PartyOperationResult.Reject(PartyFailureReason.CallerNotCurrent);

    internal PartySnapshot? FindParty(SelectedSessionIdentity session) =>
        inner.FindParty(session.CharacterId);

    internal SessionEndpoint? FindSessionEndpoint(SelectedSessionIdentity session) =>
        endpoints.FindThroughCurrentSession(session.CharacterId);

    internal PartyService Service => inner;

    private bool TryProject(
        SelectedSessionIdentity session,
        out PartyCharacterSnapshot? character)
    {
        if (!IsCurrent(session))
        {
            character = null;
            return false;
        }

        var currentPresence = presence.Find(session.CharacterId);
        var scope = currentPresence?.Session == session
            && currentPresence.StandingInTown
            && currentPresence.SharesTownPresence
            && currentPresence.ChannelId > 0
                ? new PartyDirectoryScope(
                    currentPresence.ChannelId,
                    currentPresence.TownId,
                    currentPresence.AreaId)
                : null;
        character = new PartyCharacterSnapshot(
            session.AccountId,
            session.CharacterId,
            session.WireUserId,
            scope);
        return true;
    }

    private bool IsCurrent(SelectedSessionIdentity session) =>
        sessions.FindByCharacter(session.CharacterId) == session;

    private sealed class TestSessionEndpoints(SelectedSessionDomainService sessions)
        : IPartyMemberSessionEndpointSource
    {
        private readonly Dictionary<(NetworkConnectionId ConnectionId, long Generation), SessionEndpoint>
            values = [];

        public PartyOperationResult Observe(
            SelectedSessionIdentity session,
            SessionEndpoint endpoint)
        {
            values[(session.ConnectionId, session.ConnectionGeneration)] = endpoint;
            return PartyOperationResult.Success();
        }

        public SessionEndpoint? FindThroughCurrentSession(CharacterId characterId)
        {
            var session = sessions.FindByCharacter(characterId);
            return session is null
                ? null
                : values.GetValueOrDefault((session.ConnectionId, session.ConnectionGeneration));
        }
    }
}
