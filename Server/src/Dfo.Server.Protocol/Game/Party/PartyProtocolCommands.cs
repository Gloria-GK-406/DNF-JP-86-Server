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
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CharacterDeath;
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

internal sealed partial class GameRequestPeerCommandHandler(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas,
    IPartyService parties, IPartyMemberStatusDomainService memberStatus,
    ICommandProjections projections, IPartyProjectionStateDomainService projectionState, IOutboundOrdering ordering,
    IDungeonRunQueryDomainService runs, IGameCommandOutbox outbox, ILogger<GameRequestPeerCommandHandler> logger)
    :
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
            await RefuseAsync(PartyFailureReason.TargetNotFound).ConfigureAwait(false);
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

        async ValueTask<PartyOperationResult> InviteAsync(PartyCharacterSnapshot inviter, PartyCharacterSnapshot target)
        {
            var display = await memberStatus.InvitationOfAsync(inviter, cancellationToken).ConfigureAwait(false);
            request.DemandCurrent();
            return PartyRunAdmission.Refusal(runs, inviter, target)
                ?? parties.RequestInvite(inviter, target, display);
        }

        ValueTask RefuseAsync(PartyFailureReason failure) => outbox.RegisterExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [new RequestPeerRefusal(PartyRefusalCode.ForInvite(failure), message.RequestType)], []),
            cancellationToken);

    }
}

internal sealed partial class GameResponsePeerCommandHandler(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas,
    IPartyService parties,
    ICommandProjections projections, IPartyProjectionStateDomainService projectionState, IOutboundOrdering ordering,
    IDungeonRunQueryDomainService runs, IGameCommandOutbox outbox, ILogger<GameResponsePeerCommandHandler> logger)
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

        var result = await (!sessions.IsInWorld(invitee) || !sessions.IsInWorld(inviter)
                ? ValueTask.FromResult(PartyOperationResult.Reject(PartyFailureReason.CallerNotCurrent))
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

        ValueTask RefuseAsync(PartyFailureReason failure) => outbox.RegisterExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [new ResponsePeerRefusal(PartyRefusalCode.ForAcceptance(failure), message.RequestType)], []),
            cancellationToken);
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
    IGameCommandOutbox outbox, ILogger<GameSetPartyInfoCommandHandler> logger)
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

        var result = await ( sessions.IsInWorld(session)
                && PartyCharacterProjection.TryCapture(session, townAreas, out var character)
                ? ValueTask.FromResult(parties.CreateOrUpdateSettings(character, message.Settings, message.Title))
                : ValueTask.FromResult(PartyOperationResult.Reject(
                    PartyFailureReason.CallerNotCurrent))).ConfigureAwait(false);
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
