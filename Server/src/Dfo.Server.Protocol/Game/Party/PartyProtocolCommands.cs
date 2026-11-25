using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.AntonRaid;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.PvpTotalMatchTeam;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Protocol.Game.PvpRoom;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Party;

internal sealed record GameSetUdpIpPortCommand(NetworkDispatchContext Dispatch, SetUdpIpPortRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed record GameRequestPeerCommand(NetworkDispatchContext Dispatch, RequestPeerRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed record GameResponsePeerCommand(NetworkDispatchContext Dispatch, ResponsePeerRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed record GameSetPartyInfoCommand(NetworkDispatchContext Dispatch, SetPartyInfoRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed record GameCallPartyMemberRealtimeInfoCommand(NetworkDispatchContext Dispatch, CallPartyMemberRealtimeInfoRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GameSetUdpIpPortCommandHandler(IOnlineUserQueryDomainService onlineUsers, IGameSessionEndpointDomainService endpoints,
    ISelectedSessionDomainService sessions, IPartyService parties, ICommandProjections projections,
    ILogger<GameSetUdpIpPortCommandHandler> logger)
    : ICommandHandler<GameSetUdpIpPortCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext commandContext,
        GameSetUdpIpPortCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch;
        var message = command.Input;
        var request = commandContext.Request ?? throw new InvalidOperationException("A party protocol request is required.");
        request.DemandCurrent();
        if (!onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var session)
            || session is null
            || session.AccountId is null)
        {
            logger.LogInformation("SET_UDP_IP_PORT ignored for unauthenticated connection {ConnectionId}",
                context.Connection.ConnectionId);
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var previous = endpoints.Find(session.SessionIdentity);
        var observed = SessionEndpointObservation.FromConnection(
            message.Endpoint, context.Connection.RemoteAddress);
        var accepted = endpoints.TryObserve(session.SessionIdentity, observed);
        if (accepted && previous != observed)
            await RepublishPartyEndpointsAsync().ConfigureAwait(false);
        logger.Log(
            accepted ? LogLevel.Information : LogLevel.Warning,
            "SET_UDP_IP_PORT session={SessionId} accepted={Accepted} "
                + "reported={ReportedInner}/{ReportedOuter}:{Port} nat={NatType} mtu={Mtu} "
                + "reportValid={ReportValid} observed={ObservedAddress}",
            session.SessionIdentity,
            accepted,
            message.Endpoint.InnerAddress,
            message.Endpoint.OuterAddress,
            message.Endpoint.Port,
            message.Endpoint.NatType,
            message.Endpoint.Mtu,
            message.Endpoint.IsValidClientReport,
            context.Connection.RemoteAddress);
        return accepted;

        // A member whose endpoint arrives or changes after joining would otherwise stay at the
        // placeholder its party mates were sent. A21's 0x000B handler only refreshes each peer's
        // record and the P2P peers read it when they are set up (RVA 0x011896E0), so a repeat
        // is safe and reaches them before a dungeon is entered. 70 does not resend here.
        async ValueTask RepublishPartyEndpointsAsync()
        {
            var selected = sessions.FindByConnection(context.Connection.ConnectionId);
            if (selected is null || !sessions.IsInWorld(selected))
                return;
            var party = parties.FindParty(new PartyCharacterSnapshot(
                selected.AccountId, selected.CharacterId, selected.WireUserId, null));
            if (party.IsVirtual)
                return;
            var audience = party.Members
                .Select(member => sessions.FindByCharacter(member.CharacterId))
                .OfType<SelectedSessionIdentity>()
                .ToArray();
            await PartyExactNotificationDelivery.PublishWithRetryAsync(
                projections, audience, new PartyEndpointsChanged(party), cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>REQUEST_PEER: a party invitation (type 0), or an invitation into the sender's duel room (type 2).</summary>
/// <remarks>
/// One command for the one message, dispatched on the request type (Spec RQ-022). A duel room and a party
/// exclude each other (RQ-015): a party invitation from or to a seated character is refused 0x13.
/// </remarks>
internal sealed partial class GameRequestPeerCommandHandler(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas,
    IPartyService parties, IPartyMemberStatusDomainService memberStatus,
    ICommandProjections projections, IPartyProjectionStateDomainService projectionState, IOutboundOrdering ordering,
    IDungeonRunQueryDomainService runs, IAntonRaidDomainService raids, IGameCommandOutbox outbox,
    IPvpRoomDomainService rooms, ISelectedCharacterCurrentViewDomainService currentViews, ICommandWorkUnits units,
    ILogger<GameRequestPeerCommandHandler> logger, AntonRaidSquadProjection raidSquads)
    : ICommandHandler<GameRequestPeerCommand, bool>
{

    public async ValueTask<bool> HandleAsync(CommandExecutionContext commandContext,
        GameRequestPeerCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch;
        var message = command.Input;
        var request = commandContext.Request ?? throw new InvalidOperationException("A party protocol request is required.");
        request.DemandCurrent();
        if (message.RequestType == PvpRoomInviteRequestMessage.RequestType)
            return await InviteToRoomAsync(commandContext, command, cancellationToken).ConfigureAwait(false);
        if (message.RequestType != 0)
        {
            logger.LogInformation(
                "REQUEST_PEER type={RequestType} is outside party scope; state unchanged",
                message.RequestType);
            return false;
        }

        var inviter = sessions.FindByConnection(context.Connection.ConnectionId);
        if (inviter is null)
        {
            logger.LogInformation("REQUEST_PEER ignored for non-current connection {ConnectionId}",
                context.Connection.ConnectionId);
            return false;
        }

        if (!sessions.TryGetInWorld(message.TargetWireUserId, out var target))
        {
            logger.LogInformation(
                "REQUEST_PEER target={Target} is not a current selected session",
                message.TargetWireUserId);
            await RefuseAsync(PartyFailureReason.TargetNotFound).ConfigureAwait(false);
            return false;
        }

        if (rooms.IsSeated(inviter.CharacterId) || rooms.IsSeated(target.CharacterId))
        {
            logger.LogInformation("REQUEST_PEER inviter={Inviter} target={Target} refused: a side holds a duel room seat",
                inviter.WireUserId, message.TargetWireUserId);
            await RefuseWithAsync(PartyRefusalCode.Refused).ConfigureAwait(false);
            return false;
        }

        var result = await (!sessions.IsInWorld(inviter) || !sessions.IsInWorld(target)
                ? ValueTask.FromResult(PartyOperationResult.Reject(PartyFailureReason.CallerNotCurrent))
                : PartyCharacterProjection.TryCapture(inviter, townAreas, out var inviterCharacter)
                    && PartyCharacterProjection.TryCapture(target, townAreas, out var targetCharacter)
                    ? InviteAsync(inviterCharacter, targetCharacter)
                    // A side outside the town areas is in a dungeon or between places.
                    : ValueTask.FromResult(PartyOperationResult.Reject(
                        PartyFailureReason.NotStandingInTown))).ConfigureAwait(false);
        var recipients = PartyNotificationRecipientCapture.Capture(result.Notifications, sessions, townAreas);
        if (result.Succeeded)
            await PartyNotificationStages.DeliverPartyNotificationsAsync(projections, projectionState, parties, ordering,
                    result.Notifications, recipients, cancellationToken)
                .ConfigureAwait(false);
        else
            await RefuseAsync(result.Failure).ConfigureAwait(false);
        logger.LogInformation(
            "REQUEST_PEER inviter={Inviter} target={Target} clientReportedReviveCoins={ReviveCoins} accepted={Accepted} failure={Failure}",
            inviter.WireUserId, message.TargetWireUserId, message.PeerValue,
            result.Succeeded, result.Failure);
        return result.Succeeded;

        async ValueTask<PartyOperationResult> InviteAsync(PartyCharacterSnapshot inviterCharacter, PartyCharacterSnapshot targetCharacter)
        {
            if (raids.IsRecruitingMember(inviter) || raids.IsRecruitingMember(target))
            {
                if (raids.IsPreparing(inviter))
                {
                    var prepared = raids.PrepareSquad(inviter, AntonRaidSquadSignal.Invite, target);
                    return prepared with { Notifications = raidSquads.Project(prepared.Notifications) };
                }
                if (raids.IsRecruitingMember(inviter) && raids.IsRecruitingMember(target))
                {
                    var raidDisplay = await memberStatus.InvitationOfAsync(inviterCharacter, cancellationToken).ConfigureAwait(false);
                    request.DemandCurrent();
                    return raids.RequestSquadInvite(inviter, target, raidDisplay);
                }
                return PartyOperationResult.Reject(PartyFailureReason.RaidRecruiting);
            }

            var display = await memberStatus.InvitationOfAsync(inviterCharacter, cancellationToken).ConfigureAwait(false);
            request.DemandCurrent();
            return PartyRunAdmission.Refusal(runs, inviterCharacter, targetCharacter)
                ?? parties.RequestInvite(inviterCharacter, targetCharacter, display);
        }

        ValueTask RefuseAsync(PartyFailureReason failure) => RefuseWithAsync(PartyRefusalCode.ForInvite(failure));

        ValueTask RefuseWithAsync(byte reason) => outbox.RegisterExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [new RequestPeerRefusal(reason, message.RequestType)], []),
            cancellationToken);

    }

    /// <summary>
    /// Type 2: the room owner records the invitation in one write unit; once it returns, the invitee gets the
    /// inviter's USERINFO0 and then NOTI 0x0007 type 2. A refusal answers <c>00 reason 02</c>.
    /// </summary>
    private async ValueTask<bool> InviteToRoomAsync(CommandExecutionContext execution, GameRequestPeerCommand command,
        CancellationToken cancellationToken)
    {
        var connection = command.Dispatch.Connection;
        var message = command.Input;
        if (sessions.FindByConnection(connection.ConnectionId) is not { } inviter)
        {
            logger.LogInformation("REQUEST_PEER type 2 ignored for non-current connection {ConnectionId}", connection.ConnectionId);
            return false;
        }
        PvpInviteOutcome outcome;
        // Upstream takes only the seven-byte form for a room invitation.
        if (message.Tail.Length != 0)
            outcome = new PvpInviteOutcome.Refused(PvpRoomRefusal.InviteMissing);
        else if (!sessions.TryGetInWorld(message.TargetWireUserId, out var target))
            outcome = new PvpInviteOutcome.Refused(PvpRoomRefusal.TargetUnavailable);
        else
            outcome = await units.WriteAsync(execution,
                new CommandWorkUnitDeclaration("PvpRoomInvite", [new SelectedSessionRecipient(inviter, connection, message.Metadata)]),
                (_, token) => rooms.InviteAsync(inviter, target, token, message.PeerValue), cancellationToken).ConfigureAwait(false);
        switch (outcome)
        {
            case PvpInviteOutcome.Refused refused:
                logger.LogInformation("REQUEST_PEER type 2 inviter={Inviter} target={Target} refused: {Refusal}",
                    inviter.WireUserId, message.TargetWireUserId, refused.Reason);
                await outbox.RegisterExplicitAsync(connection.ConnectionId, new GameExplicitMessageDelivery(
                    [new RequestPeerRefusal(PvpRoomRefusalCode.ForInvite(refused.Reason), message.RequestType)], []),
                    cancellationToken).ConfigureAwait(false);
                return false;
            case PvpInviteOutcome.Sent sent:
                if (await currentViews.TryLoadAsync(inviter, cancellationToken).ConfigureAwait(false) is not { } view)
                {
                    logger.LogWarning("REQUEST_PEER type 2 inviter={Inviter}: current view gone after the invitation was recorded; not delivered",
                        inviter.WireUserId);
                    return false;
                }
                await projections.ProjectAsync(new NotificationAudience.TheseSessions([sent.Invitation.Invitee]),
                    new PvpRoomBroadcast([
                        new PvpParticipantPublicationMessage(PvpRoomProjector.Identity(view)),
                        new PvpRoomInviteRequestMessage(inviter.WireUserId, message.PeerValue),
                    ]), cancellationToken).ConfigureAwait(false);
                logger.LogInformation("REQUEST_PEER type 2 inviter={Inviter} target={Target} room={Room}: invited",
                    inviter.WireUserId, message.TargetWireUserId, sent.Room.Number);
                return true;
            default:
                throw new InvalidOperationException($"Unknown duel invitation outcome {outcome.GetType().Name}.");
        }
    }
}

/// <summary>RESPONSE_PEER: accepting a party invitation (type 0), or an invitation into a duel room (type 2).</summary>
/// <remarks>
/// One command for the one message, dispatched on the request type (Spec RQ-022). A seated character cannot
/// join a party, nor anyone a seated inviter's (RQ-015): refused 0x13.
/// </remarks>
internal sealed partial class GameResponsePeerCommandHandler(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas,
    IPartyService parties,
    ICommandProjections projections, IPartyProjectionStateDomainService projectionState, IOutboundOrdering ordering,
    IDungeonRunQueryDomainService runs, IAntonRaidDomainService raids, IGameCommandOutbox outbox,
    IPvpRoomDomainService rooms, IPvpLobbyDomainService lobby, ISelectedCharacterCurrentViewDomainService currentViews,
    IOnlineCharacterRuntimeDomainService online, IOnlineUserQueryDomainService onlineUsers,
    IGameSessionEndpointDomainService endpoints, IWeaknessDomainService weakness, ICommandWorkUnits units,
    ILogger<GameResponsePeerCommandHandler> logger, AntonRaidSquadProjection raidSquads,
    IPvpTotalMatchCandidateQueryDomainService totalMatchCandidates,
    TimeProvider clock)
    :
      ICommandHandler<GameResponsePeerCommand, bool>
{

    public async ValueTask<bool> HandleAsync(CommandExecutionContext commandContext,
        GameResponsePeerCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch;
        var message = command.Input;
        var request = commandContext.Request ?? throw new InvalidOperationException("A party protocol request is required.");
        request.DemandCurrent();
        if (message.RequestType == PvpRoomInviteRequestMessage.RequestType)
            return await AcceptRoomInviteAsync(commandContext, command, cancellationToken).ConfigureAwait(false);
        if (message.RequestType != 0)
        {
            logger.LogInformation(
                "RESPONSE_PEER type={RequestType} is outside party scope; state unchanged",
                message.RequestType);
            return false;
        }

        var invitee = sessions.FindByConnection(context.Connection.ConnectionId);
        if (invitee is null)
        {
            logger.LogInformation("RESPONSE_PEER ignored for non-current connection {ConnectionId}",
                context.Connection.ConnectionId);
            return false;
        }

        if (!sessions.TryGetInWorld(message.InviterWireUserId, out var inviter))
        {
            logger.LogInformation(
                "RESPONSE_PEER inviter={Inviter} is not a current selected session",
                message.InviterWireUserId);
            await RefuseAsync(PartyFailureReason.TargetNotFound).ConfigureAwait(false);
            return false;
        }

        if (rooms.IsSeated(invitee.CharacterId) || rooms.IsSeated(inviter.CharacterId))
        {
            logger.LogInformation("RESPONSE_PEER invitee={Invitee} inviter={Inviter} refused: a side holds a duel room seat",
                invitee.WireUserId, message.InviterWireUserId);
            await RefuseWithAsync(PartyRefusalCode.Refused).ConfigureAwait(false);
            return false;
        }

        var result = await (!sessions.IsInWorld(invitee) || !sessions.IsInWorld(inviter)
                ? ValueTask.FromResult(PartyOperationResult.Reject(PartyFailureReason.CallerNotCurrent))
                : raids.IsRecruitingMember(invitee) || raids.IsRecruitingMember(inviter)
                    ? PrepareRaidSquadAsync()
                : PartyCharacterProjection.TryCapture(invitee, townAreas, out var inviteeCharacter)
                    && PartyCharacterProjection.TryCapture(inviter, townAreas, out var inviterCharacter)
                    ? ValueTask.FromResult(PartyRunAdmission.Refusal(runs, inviteeCharacter, inviterCharacter)
                        ?? parties.AcceptInvite(inviteeCharacter, inviterCharacter))
                    : ValueTask.FromResult(PartyOperationResult.Reject(
                        PartyFailureReason.NotStandingInTown))).ConfigureAwait(false);
        var recipients = PartyNotificationRecipientCapture.Capture(result.Notifications, sessions, townAreas);
        if (result.Succeeded)
            await PartyNotificationStages.DeliverPartyNotificationsAsync(projections, projectionState, parties, ordering,
                    result.Notifications, recipients, cancellationToken)
                .ConfigureAwait(false);
        else
            await RefuseAsync(result.Failure).ConfigureAwait(false);
        logger.LogInformation(
            "RESPONSE_PEER invitee={Invitee} inviter={Inviter} accepted={Accepted} failure={Failure}",
            invitee.WireUserId, message.InviterWireUserId, result.Succeeded, result.Failure);
        return result.Succeeded;

        async ValueTask<PartyOperationResult> PrepareRaidSquadAsync()
        {
            if (raids.IsPreparing(invitee))
            {
                if (message.PeerValue != 0)
                    return PartyOperationResult.Reject(PartyFailureReason.RaidRecruiting);
                var prepared = raids.PrepareSquad(invitee, AntonRaidSquadSignal.Accept, inviter);
                return prepared with { Notifications = raidSquads.Project(prepared.Notifications) };
            }
            if (raids.IsRecruitingMember(invitee) && raids.IsRecruitingMember(inviter))
            {
                var accepted = raids.RespondSquadInvite(invitee, inviter, message.PeerValue == 0);
                if (accepted.Succeeded)
                {
                    await raidSquads.DeliverChangesAsync(accepted.Changes, cancellationToken).ConfigureAwait(false);
                    return PartyOperationResult.Success();
                }
            }
            return PartyOperationResult.Reject(PartyFailureReason.RaidRecruiting);
        }

        ValueTask RefuseAsync(PartyFailureReason failure) => RefuseWithAsync(PartyRefusalCode.ForAcceptance(failure));

        ValueTask RefuseWithAsync(byte reason) => outbox.RegisterExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [new ResponsePeerRefusal(reason, message.RequestType)], []),
            cancellationToken);
    }

    /// <summary>
    /// Type 2. The seven-byte form is an acceptance; the client declines a room invitation by closing its dialog
    /// and sends nothing (upstream: A21 <c>2535A30</c>), so a sixteen-byte decline form changes nothing and is
    /// not answered. An acceptance enters the room in one write unit and projects as an entry with no command
    /// reply; a refusal answers <c>00 reason 02</c>.
    /// </summary>
    private async ValueTask<bool> AcceptRoomInviteAsync(CommandExecutionContext execution, GameResponsePeerCommand command,
        CancellationToken cancellationToken)
    {
        var connection = command.Dispatch.Connection;
        var message = command.Input;
        if (sessions.FindByConnection(connection.ConnectionId) is not { } invitee)
        {
            logger.LogInformation("RESPONSE_PEER type 2 ignored for non-current connection {ConnectionId}", connection.ConnectionId);
            return false;
        }
        if (message.Tail.Length != 0)
        {
            logger.LogInformation("RESPONSE_PEER type 2 invitee={Invitee} inviter={Inviter}: declined; nothing changes",
                invitee.WireUserId, message.InviterWireUserId);
            return false;
        }
        PvpRoomOutcome outcome = !sessions.TryGetInWorld(message.InviterWireUserId, out var inviter)
            ? new PvpRoomOutcome.Refused(PvpRoomRefusal.SessionNotCurrent)
            : await units.WriteAsync(execution,
                new CommandWorkUnitDeclaration("PvpRoomInviteAccept", [new SelectedSessionRecipient(invitee, connection, message.Metadata)]),
                (_, token) => rooms.AcceptInviteAsync(invitee, inviter.CharacterId, token, message.PeerValue), cancellationToken).ConfigureAwait(false);
        switch (outcome)
        {
            case PvpRoomOutcome.Refused refused:
                logger.LogInformation("RESPONSE_PEER type 2 invitee={Invitee} inviter={Inviter} refused: {Refusal}",
                    invitee.WireUserId, message.InviterWireUserId, refused.Reason);
                await outbox.RegisterExplicitAsync(connection.ConnectionId, new GameExplicitMessageDelivery(
                    [new ResponsePeerRefusal(PvpRoomRefusalCode.ForAcceptance(refused.Reason), message.RequestType)], []),
                    cancellationToken).ConfigureAwait(false);
                return false;
            case PvpRoomOutcome.Changed changed:
                logger.LogInformation("RESPONSE_PEER type 2 invitee={Invitee} inviter={Inviter} room={Room}: entered",
                    invitee.WireUserId, message.InviterWireUserId, changed.After?.Number);
                await new PvpRoomProjector(sessions, lobby, currentViews, online, onlineUsers, endpoints, townAreas, parties,
                    projectionState, weakness, projections, outbox, logger, totalMatchCandidates: new PvpTotalMatchCandidateProjection(rooms, totalMatchCandidates, outbox, clock))
                    .ProjectAsync(changed, cancellationToken).ConfigureAwait(false);
                return true;
            default:
                throw new InvalidOperationException($"Unknown duel room outcome {outcome.GetType().Name}.");
        }
    }
}

/// <summary>A party bound to a dungeon run takes no new members.</summary>
/// <remarks>
/// A run's members are its party's current members who entered it, so growing that party would hand a
/// character outside the entry roster a place beside the run. The refusal sits in the protocol commands
/// because the party service must not depend on runs. Either side being in a run refuses: an invite or an
/// application into a running party, and a run member (alone in a virtual party too) joining elsewhere.
/// Both commands already require both sides to stand in town and entering a run leaves it, so this is
/// unreachable today; it reports what the party service reports for a side outside town.
/// </remarks>
internal static class PartyRunAdmission
{
    public static PartyOperationResult? Refusal(IDungeonRunQueryDomainService runs,
        PartyCharacterSnapshot first, PartyCharacterSnapshot second) =>
        runs.Find(first.CharacterId) is not null || runs.Find(second.CharacterId) is not null
            ? PartyOperationResult.Reject(PartyFailureReason.NotStandingInTown)
            : null;
}

internal sealed partial class GameSetPartyInfoCommandHandler(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas,
    IPartyService parties,
    ICommandProjections projections, IPartyProjectionStateDomainService projectionState, IOutboundOrdering ordering,
    IAntonRaidDomainService raids, IGameCommandOutbox outbox, ILogger<GameSetPartyInfoCommandHandler> logger,
    AntonRaidSquadProjection raidSquads)
    :
      ICommandHandler<GameSetPartyInfoCommand, bool>
{

    public async ValueTask<bool> HandleAsync(CommandExecutionContext commandContext,
        GameSetPartyInfoCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch;
        var message = command.Input;
        var request = commandContext.Request ?? throw new InvalidOperationException("A party protocol request is required.");
        request.DemandCurrent();
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogInformation(
                "SET_PARTY_INFO ignored for non-current connection {ConnectionId}",
                context.Connection.ConnectionId);
            return false;
        }

        var result = raids.IsRecruitingMember(session)
            ? PrepareRaidSquad()
            : sessions.IsInWorld(session) && PartyCharacterProjection.TryCapture(session, townAreas, out var character)
                ? parties.CreateOrUpdateSettings(character, message.Settings, message.Title)
                : PartyOperationResult.Reject(PartyFailureReason.CallerNotCurrent);
        var recipients = PartyNotificationRecipientCapture.Capture(result.Notifications, sessions, townAreas);
        if (result.Succeeded)
        {
            await PartyNotificationStages.DeliverPartyNotificationsAsync(projections, projectionState, parties, ordering,
                    result.Notifications, recipients, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await outbox.RegisterExplicitAsync(context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(
                    [new SetPartyInfoRefusal(PartyRefusalCode.ForSettings(result.Failure))], []),
                cancellationToken).ConfigureAwait(false);
        }
        logger.LogInformation(
            "SET_PARTY_INFO character={CharacterId} accepted={Accepted} failure={Failure}",
            session.CharacterId,
            result.Succeeded,
            result.Failure);
        return result.Succeeded;

        PartyOperationResult PrepareRaidSquad()
        {
            if (!raids.IsPreparing(session) && !parties.FindParty(new(session.AccountId, session.CharacterId, session.WireUserId, null)).IsLeased)
                return PartyOperationResult.Reject(PartyFailureReason.RaidRecruiting);
            var prepared = raids.PrepareSquad(session, AntonRaidSquadSignal.Settings);
            return prepared with { Notifications = raidSquads.Project(prepared.Notifications) };
        }
    }
}

internal sealed partial class GameCallPartyMemberRealtimeInfoCommandHandler(ISelectedSessionDomainService sessions,
    IPartyService parties,
    ICommandProjections projections, IPartyProjectionStateDomainService projectionState,
    ILogger<GameCallPartyMemberRealtimeInfoCommandHandler> logger)
    :
      ICommandHandler<GameCallPartyMemberRealtimeInfoCommand, bool>
{

    public async ValueTask<bool> HandleAsync(CommandExecutionContext commandContext,
        GameCallPartyMemberRealtimeInfoCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch;
        var message = command.Input;
        var request = commandContext.Request ?? throw new InvalidOperationException("A party protocol request is required.");
        request.DemandCurrent();
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogInformation(
                "CALL_PARTY_MEMBER_REALTIME_INFO ignored for non-current connection {ConnectionId}",
                context.Connection.ConnectionId);
            return false;
        }

        var snapshot = sessions.IsInWorld(session)
            ? parties.FindParty(new PartyCharacterSnapshot(session.AccountId, session.CharacterId, session.WireUserId, null))
            : null;
        if (snapshot is { IsVirtual: false })
        {
            await PartyNotificationStages.DeliverPartyRealtimeAsync(projections, projectionState, session, snapshot, cancellationToken)
                .ConfigureAwait(false);
        }
        logger.LogInformation(
            "CALL_PARTY_MEMBER_REALTIME_INFO character={CharacterId} inParty={InParty}",
            session.CharacterId, snapshot is { IsVirtual: false });
        return snapshot is { IsVirtual: false };
    }
}
