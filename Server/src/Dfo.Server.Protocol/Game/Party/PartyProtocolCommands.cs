using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
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
    ILogger<GameSetUdpIpPortCommandHandler> logger)
    : ICommandHandler<GameSetUdpIpPortCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext commandContext,
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
            return ValueTask.FromResult(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var accepted = endpoints.TryObserve(
            session.SessionIdentity,
            SessionEndpointObservation.FromConnection(
                message.Endpoint, context.Connection.RemoteAddress));
        logger.LogInformation(
            "SET_UDP_IP_PORT session={SessionId} accepted={Accepted} "
                + "reported={ReportedInner}/{ReportedOuter}:{Port} observed={ObservedAddress}",
            session.SessionIdentity,
            accepted,
            message.Endpoint.InnerAddress,
            message.Endpoint.OuterAddress,
            message.Endpoint.Port,
            context.Connection.RemoteAddress);
        return ValueTask.FromResult(accepted);
    }
}

internal sealed class GameRequestPeerCommandHandler(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas,
    IPartyService parties, IPartyMemberStatusDomainService memberStatus,
    ICommandProjections projections, IPartyProjectionStateDomainService projectionState, IOutboundOrdering ordering,
    ILogger<GameRequestPeerCommandHandler> logger)
    : PartyProtocolCommandProjection(projections, projectionState.StreamOf, parties, ordering),
      ICommandHandler<GameRequestPeerCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext commandContext,
        GameRequestPeerCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch;
        var message = command.Input;
        var request = commandContext.Request ?? throw new InvalidOperationException("A party protocol request is required.");
        request.DemandCurrent();
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
            return false;
        }

        var execution = await PartyTransitionExecution.RunPartyAsync(
            sessions,
            townAreas,
            () => sessions.IsInWorld(inviter)
                && sessions.IsInWorld(target)
                && PartyCharacterProjection.TryCapture(inviter, townAreas, out var inviterCharacter)
                && PartyCharacterProjection.TryCapture(target, townAreas, out var targetCharacter)
                    ? InviteAsync(inviterCharacter, targetCharacter)
                    : ValueTask.FromResult(PartyOperationResult.Reject(
                        PartyFailureReason.CallerNotCurrent))).ConfigureAwait(false);
        var result = execution.Result;
        if (result.Succeeded)
            await DeliverPartyNotificationsAsync(
                    result.Notifications, execution.Recipients, cancellationToken)
                .ConfigureAwait(false);
        logger.LogInformation(
            "REQUEST_PEER inviter={Inviter} target={Target} clientReportedReviveCoins={ReviveCoins} accepted={Accepted} failure={Failure}",
            inviter.WireUserId, message.TargetWireUserId, message.PeerValue,
            result.Succeeded, result.Failure);
        return result.Succeeded;

        async ValueTask<PartyOperationResult> InviteAsync(PartyCharacterSnapshot inviter, PartyCharacterSnapshot target)
        {
            var display = await memberStatus.InvitationOfAsync(inviter, cancellationToken).ConfigureAwait(false);
            request.DemandCurrent();
            return PartyService!.RequestInvite(inviter, target, display);
        }

    }
}

internal sealed class GameResponsePeerCommandHandler(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas,
    IPartyService parties,
    ICommandProjections projections, IPartyProjectionStateDomainService projectionState, IOutboundOrdering ordering,
    ILogger<GameResponsePeerCommandHandler> logger)
    : PartyProtocolCommandProjection(projections, projectionState.StreamOf, parties, ordering),
      ICommandHandler<GameResponsePeerCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext commandContext,
        GameResponsePeerCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch;
        var message = command.Input;
        var request = commandContext.Request ?? throw new InvalidOperationException("A party protocol request is required.");
        request.DemandCurrent();
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
            return false;
        }

        var execution = await PartyTransitionExecution.RunPartyAsync(
            sessions,
            townAreas,
            () => sessions.IsInWorld(invitee)
                && sessions.IsInWorld(inviter)
                && PartyCharacterProjection.TryCapture(invitee, townAreas, out var inviteeCharacter)
                && PartyCharacterProjection.TryCapture(inviter, townAreas, out var inviterCharacter)
                    ? ValueTask.FromResult(PartyService!.AcceptInvite(inviteeCharacter, inviterCharacter))
                    : ValueTask.FromResult(PartyOperationResult.Reject(
                        PartyFailureReason.CallerNotCurrent))).ConfigureAwait(false);
        var result = execution.Result;
        if (result.Succeeded)
            await DeliverPartyNotificationsAsync(
                    result.Notifications, execution.Recipients, cancellationToken)
                .ConfigureAwait(false);
        logger.LogInformation(
            "RESPONSE_PEER invitee={Invitee} inviter={Inviter} accepted={Accepted} failure={Failure}",
            invitee.WireUserId, message.InviterWireUserId, result.Succeeded, result.Failure);
        return result.Succeeded;
    }
}

internal sealed class GameSetPartyInfoCommandHandler(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas,
    IPartyService parties,
    ICommandProjections projections, IPartyProjectionStateDomainService projectionState, IOutboundOrdering ordering,
    ILogger<GameSetPartyInfoCommandHandler> logger)
    : PartyProtocolCommandProjection(projections, projectionState.StreamOf, parties, ordering),
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

        var execution = await PartyTransitionExecution.RunPartyAsync(
            sessions,
            townAreas,
            () => sessions.IsInWorld(session)
                && PartyCharacterProjection.TryCapture(session, townAreas, out var character)
                ? ValueTask.FromResult(PartyService!.CreateOrUpdateSettings(character, message.Settings, message.Title))
                : ValueTask.FromResult(PartyOperationResult.Reject(
                    PartyFailureReason.CallerNotCurrent))).ConfigureAwait(false);
        var result = execution.Result;
        if (result.Succeeded)
        {
            await DeliverPartyNotificationsAsync(
                    result.Notifications, execution.Recipients, cancellationToken)
                .ConfigureAwait(false);
        }
        logger.LogInformation(
            "SET_PARTY_INFO character={CharacterId} accepted={Accepted} failure={Failure}",
            session.CharacterId,
            result.Succeeded,
            result.Failure);
        return result.Succeeded;
    }
}

internal sealed class GameCallPartyMemberRealtimeInfoCommandHandler(ISelectedSessionDomainService sessions,
    IPartyService parties,
    ICommandProjections projections, IPartyProjectionStateDomainService projectionState, IOutboundOrdering ordering,
    ILogger<GameCallPartyMemberRealtimeInfoCommandHandler> logger)
    : PartyProtocolCommandProjection(projections, projectionState.StreamOf, parties, ordering),
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
            ? PartyService!.FindParty(new PartyCharacterSnapshot(session.AccountId, session.CharacterId, session.WireUserId, null))
            : null;
        if (snapshot is { IsVirtual: false })
        {
            await DeliverPartyRealtimeAsync(session, snapshot, cancellationToken)
                .ConfigureAwait(false);
        }
        logger.LogInformation(
            "CALL_PARTY_MEMBER_REALTIME_INFO character={CharacterId} inParty={InParty}",
            session.CharacterId, snapshot is { IsVirtual: false });
        return snapshot is { IsVirtual: false };
    }
}
