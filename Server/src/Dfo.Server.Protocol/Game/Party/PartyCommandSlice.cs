using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Party;

public sealed record SetUdpIpPortRequest(
    GameMessageMetadata Metadata,
    SessionEndpoint Endpoint,
    byte[] IdentityTail) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSetUdpIpPort;
}

public sealed record RequestPeerRequest(
    GameMessageMetadata Metadata,
    ushort TargetWireUserId,
    byte RequestType,
    int PeerValue,
    byte[] Tail) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameRequestPeer;
}

public sealed record ResponsePeerRequest(
    GameMessageMetadata Metadata,
    ushort InviterWireUserId,
    byte RequestType,
    int PeerValue,
    byte[] Tail) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameResponsePeer;
}

public sealed record LeavePartyRequest(GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameLeaveParty;
}

public sealed record RemovePartyMemberRequest(
    GameMessageMetadata Metadata,
    byte TargetSlot) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameRemovePartyMember;
}

public sealed record ChangePartyLeaderRequest(
    GameMessageMetadata Metadata,
    byte TargetSlot) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameChangePartyLeader;
}

public sealed record RemovePartyMemberRefusal : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameRemovePartyMemberRefusal;
}

public sealed record CallPartyMemberRealtimeInfoRequest(GameMessageMetadata Metadata)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameCallPartyMemberRealtimeInfo;
}

public sealed record SetPartyInfoRequest(
    GameMessageMetadata Metadata,
    PartySettings Settings,
    PartyTitle Title) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSetPartyInfo;
}

internal static class PartyCommandBody
{
    public static void RequireLength(string name, ReadOnlySpan<byte> body, params int[] allowed)
    {
        if (!allowed.Contains(body.Length))
        {
            throw new InvalidDataException(
                $"{name} requires body length {string.Join(" or ", allowed)}; received '{body.Length}'.");
        }
    }
}

internal sealed class SetUdpIpPortDecoder : IGameMessageDecoder
{
    private const int PrefixLength = 15;

    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < PrefixLength)
        {
            throw new InvalidDataException(
                $"SET_UDP_IP_PORT requires at least {PrefixLength} body bytes; received '{body.Length}'.");
        }

        var endpoint = new SessionEndpoint(
            body[0],
            new SessionIpv4Address(body[1], body[2], body[3], body[4]),
            new SessionIpv4Address(body[5], body[6], body[7], body[8]),
            BinaryPrimitives.ReadUInt16BigEndian(body[9..]),
            BinaryPrimitives.ReadUInt32LittleEndian(body[11..]));
        if (!endpoint.InnerAddress.IsUsableUnicast)
            throw new InvalidDataException("SET_UDP_IP_PORT contains an invalid inner IPv4 address.");
        if (!endpoint.OuterAddress.IsUsableUnicast)
            throw new InvalidDataException("SET_UDP_IP_PORT contains an invalid outer IPv4 address.");
        if (endpoint.Port == 0)
            throw new InvalidDataException("SET_UDP_IP_PORT contains a zero UDP port.");
        if (!SessionEndpoint.IsValidClientMtu(endpoint.Mtu))
        {
            throw new InvalidDataException(
                $"SET_UDP_IP_PORT contains unsupported A21 MTU '{endpoint.Mtu}'; " +
                "expected 512, 1024, 1280, or 1472.");
        }

        return new SetUdpIpPortRequest(metadata, endpoint, body[PrefixLength..].ToArray());
    }
}

internal sealed class RequestPeerDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        PartyCommandBody.RequireLength("REQUEST_PEER", body, 7, 9);
        return new RequestPeerRequest(
            metadata,
            BinaryPrimitives.ReadUInt16LittleEndian(body),
            body[2],
            BinaryPrimitives.ReadInt32LittleEndian(body[3..]),
            body[7..].ToArray());
    }
}

internal sealed class ResponsePeerDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        PartyCommandBody.RequireLength("RESPONSE_PEER", body, 7, 16);
        return new ResponsePeerRequest(
            metadata,
            BinaryPrimitives.ReadUInt16LittleEndian(body),
            body[2],
            BinaryPrimitives.ReadInt32LittleEndian(body[3..]),
            body[7..].ToArray());
    }
}

internal sealed class LeavePartyDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        PartyCommandBody.RequireLength("LEAVE_PARTY", body, 0);
        return new LeavePartyRequest(metadata);
    }
}

internal sealed class RemovePartyMemberDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        PartyCommandBody.RequireLength("WALKOUT_PARTY_MEMBER", body, 1);
        return new RemovePartyMemberRequest(metadata, body[0]);
    }
}

internal sealed class ChangePartyLeaderDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        PartyCommandBody.RequireLength("CHANGE_HOST", body, 1);
        return new ChangePartyLeaderRequest(metadata, body[0]);
    }
}

internal sealed class RemovePartyMemberRefusalEncoder
    : GameCmdMessageEncoder<RemovePartyMemberRefusal>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        RemovePartyMemberRefusal message) =>
        OnePayload(GameProtocol.RemovePartyMemberType, EncodeBody());

    internal static byte[] EncodeBody() => [0x00, 0x13];
}

internal sealed class CallPartyMemberRealtimeInfoDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        PartyCommandBody.RequireLength("CALL_PARTY_MEMBER_REALTIME_INFO", body, 0);
        return new CallPartyMemberRealtimeInfoRequest(metadata);
    }
}

internal sealed class SetPartyInfoDecoder(IPartyModeRuleService rules) : IGameMessageDecoder
{
    /// <remarks>
    /// A server policy, not a measured client limit: only 1 B and 12 B titles have been observed
    /// (protocol/game/client-to-server/0x000C.md, section of 2026-10-23). The title is fanned out
    /// to every observer and newcomer in the area, so an unbounded one could fill a third party's
    /// send queue. A longer title is refused rather than cut: a cut title is not what the leader wrote.
    /// </remarks>
    internal const int MaximumTitleBytes = 256;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        byte[] canonical;
        PartyTitle title;
        if (body.Length == PartySettings.WireLength)
        {
            canonical = new byte[PartySettings.WireLength];
            canonical[0] = body[1];
            canonical[1] = body[0];
            body[2..].CopyTo(canonical.AsSpan(2));
            title = PartyTitle.Empty;
        }
        else
        {
            if (body.Length < 16)
                throw new InvalidDataException(
                    $"SET_PARTY_INFO requires 12 or at least 16 body bytes; received '{body.Length}'.");
            if (body[0] > 2 || body[1] != 0)
                throw new InvalidDataException("SET_PARTY_INFO edit shape contains invalid leading gates.");

            var titleLength = BinaryPrimitives.ReadInt32LittleEndian(body[2..]);
            if (titleLength < 0)
                throw new InvalidDataException("SET_PARTY_INFO title length cannot be negative.");
            if (titleLength > MaximumTitleBytes)
            {
                throw new InvalidDataException(
                    $"SET_PARTY_INFO title length '{titleLength}' exceeds '{MaximumTitleBytes}' bytes.");
            }
            var expectedLength = 16L + titleLength;
            if (expectedLength != body.Length)
            {
                throw new InvalidDataException(
                    $"SET_PARTY_INFO edit length requires '{expectedLength}' bytes; received '{body.Length}'.");
            }

            var titleBytes = body.Slice(6, titleLength);
            try
            {
                _ = StrictUtf8.GetCharCount(titleBytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("SET_PARTY_INFO title is not valid UTF-8.", exception);
            }

            canonical = new byte[PartySettings.WireLength];
            canonical[0] = body[1];
            canonical[1] = body[0];
            body.Slice(6 + titleLength, 10).CopyTo(canonical.AsSpan(2));
            title = new PartyTitle(titleBytes);
        }

        var settings = rules.Normalize(new PartySettings(canonical));
        var validation = rules.Validate(settings, memberCount: 0);
        if (validation != PartyModeRuleFailure.None)
        {
            throw new InvalidDataException(
                $"SET_PARTY_INFO settings failed shared validation: {validation}.");
        }
        return new SetPartyInfoRequest(metadata, settings, title);
    }
}

public sealed record RequestPartyInviteCommand(
    PartyCharacterSnapshot Inviter,
    PartyCharacterSnapshot Target) : ICommand<PartyOperationResult>;

public sealed class RequestPartyInviteCommandHandler(
    IPartyService parties,
    IPartyMemberStatusDomainService memberStatus)
    : ICommandHandler<RequestPartyInviteCommand, PartyOperationResult>
{
    public async ValueTask<PartyOperationResult> HandleAsync(
        CommandExecutionContext context,
        RequestPartyInviteCommand command,
        CancellationToken cancellationToken)
    {
        var display = await memberStatus
            .InvitationOfAsync(command.Inviter, cancellationToken)
            .ConfigureAwait(false);
        return parties.RequestInvite(command.Inviter, command.Target, display);
    }
}

public sealed record AcceptPartyInviteCommand(
    PartyCharacterSnapshot Invitee,
    PartyCharacterSnapshot Inviter) : ICommand<PartyOperationResult>;

public sealed class AcceptPartyInviteCommandHandler(IPartyService parties)
    : ICommandHandler<AcceptPartyInviteCommand, PartyOperationResult>
{
    public ValueTask<PartyOperationResult> HandleAsync(
        CommandExecutionContext context,
        AcceptPartyInviteCommand command,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(parties.AcceptInvite(command.Invitee, command.Inviter));
}

public sealed record LeavePartyCommand(CharacterId CharacterId)
    : ICommand<PartyOperationResult>;

public sealed class LeavePartyCommandHandler(
    IPartyService parties,
    IDungeonRunDomainService? runs = null)
    : ICommandHandler<LeavePartyCommand, PartyOperationResult>
{
    public async ValueTask<PartyOperationResult> HandleAsync(
        CommandExecutionContext context,
        LeavePartyCommand command,
        CancellationToken cancellationToken)
    {
        // Capture before the party mutation: run views reject a departing character immediately
        // after the PartyService membership changes, while the exact run index still needs cleanup.
        var run = runs?.Find(command.CharacterId);
        var result = parties.LeaveParty(command.CharacterId);
        if (result.Succeeded && run is not null)
            _ = await runs!.EndAsync(command.CharacterId, run.RunId);
        return result;
    }
}

public sealed record TransferPartyLeadershipCommand(
    CharacterId CurrentLeaderCharacterId,
    CharacterId NextLeaderCharacterId) : ICommand<PartyOperationResult>;

public sealed class TransferPartyLeadershipCommandHandler(IPartyService parties)
    : ICommandHandler<TransferPartyLeadershipCommand, PartyOperationResult>
{
    public ValueTask<PartyOperationResult> HandleAsync(
        CommandExecutionContext context,
        TransferPartyLeadershipCommand command,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(parties.TransferLeadership(
            command.CurrentLeaderCharacterId,
            command.NextLeaderCharacterId));
}

public sealed record RemovePartyMemberCommand(
    CharacterId CurrentLeaderCharacterId,
    CharacterId TargetCharacterId) : ICommand<PartyOperationResult>;

public sealed class RemovePartyMemberCommandHandler(
    IPartyService parties,
    IDungeonRunDomainService? runs = null)
    : ICommandHandler<RemovePartyMemberCommand, PartyOperationResult>
{
    public async ValueTask<PartyOperationResult> HandleAsync(
        CommandExecutionContext context,
        RemovePartyMemberCommand command,
        CancellationToken cancellationToken)
    {
        var run = runs?.Find(command.TargetCharacterId);
        var result = parties.RemoveMember(
            command.CurrentLeaderCharacterId,
            command.TargetCharacterId);
        if (result.Succeeded && run is not null)
            _ = await runs!.EndAsync(command.TargetCharacterId, run.RunId);
        return result;
    }
}

public sealed record QueryPartyRealtimeCommand(PartyCharacterSnapshot Character)
    : ICommand<PartyRealtimeQueryResult>;

public sealed record PartyRealtimeQueryResult(PartySnapshot? Snapshot);

public sealed class QueryPartyRealtimeCommandHandler(IPartyService parties)
    : ICommandHandler<QueryPartyRealtimeCommand, PartyRealtimeQueryResult>
{
    public ValueTask<PartyRealtimeQueryResult> HandleAsync(
        CommandExecutionContext context,
        QueryPartyRealtimeCommand command,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new PartyRealtimeQueryResult(
            parties.FindParty(command.Character)));
}

public sealed record SetPartyInfoCommand(
    PartyCharacterSnapshot Character,
    PartySettings Settings,
    PartyTitle Title) : ICommand<PartyOperationResult>;

public sealed class SetPartyInfoCommandHandler(IPartyService parties)
    : ICommandHandler<SetPartyInfoCommand, PartyOperationResult>
{
    public ValueTask<PartyOperationResult> HandleAsync(
        CommandExecutionContext context,
        SetPartyInfoCommand command,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(parties.CreateOrUpdateSettings(
            command.Character,
            command.Settings,
            command.Title));
}

internal static class PartyTransitionExecution
{
    public static async ValueTask<PartyCommandExecution> RunPartyAsync(
        ISelectedSessionDomainService sessions,
        ITownAreaDomainService? townAreas,
        Func<ValueTask<PartyOperationResult>> action)
    {
        var result = await action().ConfigureAwait(false);
        return new PartyCommandExecution(
            result,
            PartyNotificationRecipientCapture.Capture(
                result.Notifications,
                sessions,
                townAreas));
    }
}

internal sealed record PartyCommandExecution(
    PartyOperationResult Result,
    PartyNotificationRecipientPlan Recipients);

internal static class PartyCharacterProjection
{
    public static bool TryCapture(
        SelectedSessionIdentity session,
        ITownAreaDomainService townAreas,
        out PartyCharacterSnapshot character)
    {
        var current = townAreas.Find(session.CharacterId);
        if (current is null || !session.Owns(current))
        {
            character = null!;
            return false;
        }

        var scope = current.SharesTownPresence
                ? new PartyDirectoryScope(
                    current.Area.ChannelId,
                    current.Area.TownId,
                    current.Area.AreaId, current.Area.Instance)
                : null;
        character = new PartyCharacterSnapshot(
            session.AccountId,
            session.CharacterId,
            session.WireUserId,
            scope);
        return true;
    }
}

public sealed class SetUdpIpPortHandler(
    IOnlineUserQueryDomainService onlineUsers,
    IGameSessionEndpointDomainService endpoints,
    ILogger<SetUdpIpPortHandler> logger) : ClientMessageHandler<SetUdpIpPortRequest>
{
    protected override ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        SetUdpIpPortRequest message,
        CancellationToken cancellationToken)
    {
        if (!onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var session)
            || session is null
            || session.AccountId is null)
        {
            logger.LogInformation("SET_UDP_IP_PORT ignored for unauthenticated connection {ConnectionId}",
                context.Connection.ConnectionId);
            return ValueTask.CompletedTask;
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
        return ValueTask.CompletedTask;
    }
}

internal static class SessionEndpointObservation
{
    private const byte DirectNatType = 0;
    private const uint DirectMtu = 1500;

    public static SessionEndpoint FromConnection(
        SessionEndpoint reported,
        System.Net.IPAddress? remoteAddress)
    {
        ArgumentNullException.ThrowIfNull(reported);
        if (remoteAddress is null)
            return reported;

        byte[] octets;
        if (System.Net.IPAddress.IsLoopback(remoteAddress))
        {
            octets = [127, 0, 0, 1];
        }
        else if (remoteAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            octets = remoteAddress.GetAddressBytes();
        }
        else if (remoteAddress.IsIPv4MappedToIPv6)
        {
            octets = remoteAddress.MapToIPv4().GetAddressBytes();
        }
        else
        {
            return reported;
        }

        var observed = new SessionIpv4Address(octets[0], octets[1], octets[2], octets[3]);
        return observed.IsServerObservedUnicast
            ? reported with
            {
                NatType = DirectNatType,
                InnerAddress = observed,
                OuterAddress = observed,
                Mtu = DirectMtu,
            }
            : reported;
    }
}

public sealed class RequestPeerHandler(
    ISelectedSessionDomainService sessions,
    ITownAreaDomainService townAreas,
    ICommandBus commands,
    PartyNotificationDelivery notifications,
    ILogger<RequestPeerHandler> logger) : ClientMessageHandler<RequestPeerRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        RequestPeerRequest message,
        CancellationToken cancellationToken)
    {
        if (message.RequestType != 0)
        {
            logger.LogInformation(
                "REQUEST_PEER type={RequestType} is outside party scope; state unchanged",
                message.RequestType);
            return;
        }

        var inviter = sessions.FindByConnection(context.Connection.ConnectionId);
        if (inviter is null)
        {
            logger.LogInformation("REQUEST_PEER ignored for non-current connection {ConnectionId}",
                context.Connection.ConnectionId);
            return;
        }

        if (!sessions.TryGetInWorld(message.TargetWireUserId, out var target))
        {
            logger.LogInformation(
                "REQUEST_PEER target={Target} is not a current selected session",
                message.TargetWireUserId);
            return;
        }

        var execution = await PartyTransitionExecution.RunPartyAsync(
            sessions,
            townAreas,
            () => sessions.IsInWorld(inviter)
                && sessions.IsInWorld(target)
                && PartyCharacterProjection.TryCapture(inviter, townAreas, out var inviterCharacter)
                && PartyCharacterProjection.TryCapture(target, townAreas, out var targetCharacter)
                    ? commands.SendAsync(new RequestPartyInviteCommand(
                        inviterCharacter, targetCharacter), cancellationToken)
                    : ValueTask.FromResult(PartyOperationResult.Reject(
                        PartyFailureReason.CallerNotCurrent))).ConfigureAwait(false);
        var result = execution.Result;
        if (result.Succeeded)
            await notifications.DeliverAsync(
                    result.Notifications, execution.Recipients, cancellationToken)
                .ConfigureAwait(false);
        logger.LogInformation(
            "REQUEST_PEER inviter={Inviter} target={Target} clientReportedReviveCoins={ReviveCoins} accepted={Accepted} failure={Failure}",
            inviter.WireUserId, message.TargetWireUserId, message.PeerValue,
            result.Succeeded, result.Failure);
    }
}

public sealed class ResponsePeerHandler(
    ISelectedSessionDomainService sessions,
    ITownAreaDomainService townAreas,
    ICommandBus commands,
    PartyNotificationDelivery notifications,
    ILogger<ResponsePeerHandler> logger) : ClientMessageHandler<ResponsePeerRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        ResponsePeerRequest message,
        CancellationToken cancellationToken)
    {
        if (message.RequestType != 0)
        {
            logger.LogInformation(
                "RESPONSE_PEER type={RequestType} is outside party scope; state unchanged",
                message.RequestType);
            return;
        }

        var invitee = sessions.FindByConnection(context.Connection.ConnectionId);
        if (invitee is null)
        {
            logger.LogInformation("RESPONSE_PEER ignored for non-current connection {ConnectionId}",
                context.Connection.ConnectionId);
            return;
        }

        if (!sessions.TryGetInWorld(message.InviterWireUserId, out var inviter))
        {
            logger.LogInformation(
                "RESPONSE_PEER inviter={Inviter} is not a current selected session",
                message.InviterWireUserId);
            return;
        }

        var execution = await PartyTransitionExecution.RunPartyAsync(
            sessions,
            townAreas,
            () => sessions.IsInWorld(invitee)
                && sessions.IsInWorld(inviter)
                && PartyCharacterProjection.TryCapture(invitee, townAreas, out var inviteeCharacter)
                && PartyCharacterProjection.TryCapture(inviter, townAreas, out var inviterCharacter)
                    ? commands.SendAsync(new AcceptPartyInviteCommand(
                        inviteeCharacter, inviterCharacter), cancellationToken)
                    : ValueTask.FromResult(PartyOperationResult.Reject(
                        PartyFailureReason.CallerNotCurrent))).ConfigureAwait(false);
        var result = execution.Result;
        if (result.Succeeded)
            await notifications.DeliverAsync(
                    result.Notifications, execution.Recipients, cancellationToken)
                .ConfigureAwait(false);
        logger.LogInformation(
            "RESPONSE_PEER invitee={Invitee} inviter={Inviter} accepted={Accepted} failure={Failure}",
            invitee.WireUserId, message.InviterWireUserId, result.Succeeded, result.Failure);
    }
}

public sealed class LeavePartyHandler(
    ISelectedSessionDomainService sessions,
    ICommandBus commands,
    PartyNotificationDelivery notifications,
    ILogger<LeavePartyHandler> logger,
    ITownAreaDomainService? townAreas = null,
    IDungeonRunDomainService? runs = null,
    IDungeonRunParticipantDepartureSink? departureSink = null,
    IllusionTowerPartyCoordinator? towerParties = null, BossTowerPartyCoordinator? bossParties = null)
    : ClientMessageHandler<LeavePartyRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        LeavePartyRequest message,
        CancellationToken cancellationToken)
    {
        if (IllusionTowerProjectionDelivery.Stale(message.Metadata))
            return;
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogInformation("LEAVE_PARTY ignored for non-current connection {ConnectionId}",
                context.Connection.ConnectionId);
            return;
        }

        PartyCommandExecution execution;
        DungeonRunState? departingRun;
        if (bossParties is not null)
        {
            var bossAttempt = await bossParties.TryLeaveAsync(session, allowVirtual: false, departureResponse: null, cancellationToken).ConfigureAwait(false);
            if (bossAttempt.Handled) return;
        }
        if (towerParties is not null)
        {
            var towerAttempt = await towerParties.TryLeaveAsync(
                session, allowVirtual: false, departureResponse: null, cancellationToken)
                .ConfigureAwait(false);
            if (towerAttempt.Handled)
            {
                logger.LogInformation(
                    "LEAVE_PARTY character={CharacterId} accepted={Accepted} failure={Failure}",
                    session.CharacterId, towerAttempt.Result.Succeeded, towerAttempt.Result.Failure);
                return;
            }
        }

        departingRun = runs?.Find(session.CharacterId);
        execution = await PartyTransitionExecution.RunPartyAsync(
            sessions,
            townAreas,
            () =>
            {
                if (!sessions.IsInWorld(session))
                    return ValueTask.FromResult(PartyOperationResult.Reject(
                        PartyFailureReason.CallerNotCurrent));
                return commands.SendAsync(
                    new LeavePartyCommand(session.CharacterId), cancellationToken);
            }).ConfigureAwait(false);
        var result = execution.Result;
        if (result.Succeeded)
        {
            if (departingRun is not null && runs?.Find(session.CharacterId) is null)
                departureSink?.ParticipantDeparted(departingRun.RunId, session.CharacterId);
            await notifications.DeliverAsync(
                    result.Notifications, execution.Recipients, cancellationToken)
                .ConfigureAwait(false);
        }
        logger.LogInformation(
            "LEAVE_PARTY character={CharacterId} accepted={Accepted} failure={Failure}",
            session.CharacterId, result.Succeeded, result.Failure);
    }
}

public sealed class ChangePartyLeaderHandler(
    ISelectedSessionDomainService sessions,
    IPartyService parties,
    ICommandBus commands,
    PartyNotificationDelivery notifications,
    ILogger<ChangePartyLeaderHandler> logger,
    ITownAreaDomainService? townAreas = null,
    IllusionTowerPartyCoordinator? towerParties = null, BossTowerPartyCoordinator? bossParties = null)
    : ClientMessageHandler<ChangePartyLeaderRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        ChangePartyLeaderRequest message,
        CancellationToken cancellationToken)
    {
        if (IllusionTowerProjectionDelivery.Stale(message.Metadata))
            return;
        var leader = sessions.FindByConnection(context.Connection.ConnectionId);
        if (leader is null)
            return;
        PartyCommandExecution execution;
        SelectedSessionIdentity target;
        if (bossParties is not null)
        {
            var bossAttempt = await bossParties.TryTransferAsync(leader, message.TargetSlot, cancellationToken).ConfigureAwait(false);
            if (bossAttempt.Handled) return;
        }
        if (towerParties is not null)
        {
            var towerAttempt = await towerParties.TryTransferAsync(
                leader, message.TargetSlot, cancellationToken).ConfigureAwait(false);
            if (towerAttempt.Handled)
            {
                logger.LogInformation(
                    "CHANGE_HOST leader={Leader} target={Target} slot={Slot} accepted={Accepted} failure={Failure}",
                    leader.CharacterId, towerAttempt.Target?.CharacterId, message.TargetSlot,
                    towerAttempt.Result.Succeeded, towerAttempt.Result.Failure);
                return;
            }
        }

        var roster = parties.FindParty(Character(leader));
        var targetMember = roster.IsVirtual
            ? null
            : roster.Members.SingleOrDefault(member => member.Slot == message.TargetSlot);
        var capturedTarget = targetMember is not null
            && sessions.TryGetInWorld(targetMember.CharacterId, out var inWorld)
            ? inWorld
            : null;
        if (capturedTarget is null)
        {
            logger.LogInformation(
                "CHANGE_HOST leader={Leader} slot={Slot} ignored because the target is not current",
                leader.CharacterId, message.TargetSlot);
            return;
        }
        target = capturedTarget;

        execution = await PartyTransitionExecution.RunPartyAsync(
            sessions,
            townAreas,
            () =>
            {
                if (!sessions.IsInWorld(leader)
                    || !sessions.IsInWorld(target))
                {
                    return ValueTask.FromResult(PartyOperationResult.Reject(
                        PartyFailureReason.CallerNotCurrent));
                }
                return commands.SendAsync(new TransferPartyLeadershipCommand(
                    leader.CharacterId, target.CharacterId), cancellationToken);
            }).ConfigureAwait(false);
        if (execution.Result.Succeeded)
        {
            await notifications.DeliverAsync(
                    execution.Result.Notifications, execution.Recipients, cancellationToken)
                .ConfigureAwait(false);
        }
        logger.LogInformation(
            "CHANGE_HOST leader={Leader} target={Target} slot={Slot} accepted={Accepted} failure={Failure}",
            leader.CharacterId, target.CharacterId, message.TargetSlot,
            execution.Result.Succeeded, execution.Result.Failure);
    }

    private static PartyCharacterSnapshot Character(SelectedSessionIdentity session) =>
        new(session.AccountId, session.CharacterId, session.WireUserId, null);
}

public sealed class RemovePartyMemberHandler(
    ISelectedSessionDomainService sessions,
    IPartyService parties,
    ICommandBus commands,
    PartyNotificationDelivery notifications,
    IGameMessageSender sender,
    ILogger<RemovePartyMemberHandler> logger,
    ITownAreaDomainService? townAreas = null,
    IDungeonRunDomainService? runs = null,
    IDungeonRunParticipantDepartureSink? departureSink = null,
    ISelectedCharacterCurrentViewDomainService? currentViews = null,
    IOnlineCharacterRuntimeDomainService? onlineCharacters = null,
    TownAreaProjectionDelivery? townAreaProjection = null,
    IllusionTowerPartyCoordinator? towerParties = null, BossTowerPartyCoordinator? bossParties = null)
    : ClientMessageHandler<RemovePartyMemberRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        RemovePartyMemberRequest message,
        CancellationToken cancellationToken)
    {
        if (IllusionTowerProjectionDelivery.Stale(message.Metadata))
            return;
        var leader = sessions.FindByConnection(context.Connection.ConnectionId);
        if (leader is null)
            return;
        PartyCommandExecution execution;
        SelectedSessionIdentity target;
        DungeonRunState? departingRun;
        SelectCharacterResult? returnRuntime;
        CharacterPositionState? returnPosition;
        if (bossParties is not null)
        {
            var bossAttempt = await bossParties.TryRemoveAsync(leader, message.TargetSlot, cancellationToken).ConfigureAwait(false);
            if (bossAttempt.Handled) return;
        }
        if (towerParties is not null)
        {
            var towerAttempt = await towerParties.TryRemoveAsync(
                leader, message.TargetSlot, cancellationToken).ConfigureAwait(false);
            if (towerAttempt.Handled)
            {
                if (!towerAttempt.Result.Succeeded)
                {
                    await RefuseAsync(context.Connection.ConnectionId, cancellationToken)
                        .ConfigureAwait(false);
                }
                logger.LogInformation(
                    "WALKOUT_PARTY_MEMBER leader={Leader} target={Target} slot={Slot} accepted={Accepted} failure={Failure}",
                    leader.CharacterId, towerAttempt.Target?.CharacterId, message.TargetSlot,
                    towerAttempt.Result.Succeeded, towerAttempt.Result.Failure);
                return;
            }
        }

        var roster = parties.FindParty(Character(leader));
        var targetMember = roster.IsVirtual
            ? null
            : roster.Members.SingleOrDefault(member => member.Slot == message.TargetSlot);
        var capturedTarget = targetMember is not null
            && sessions.TryGetInWorld(targetMember.CharacterId, out var inWorld)
            ? inWorld
            : null;
        if (capturedTarget is null)
        {
            await RefuseAsync(context.Connection.ConnectionId, cancellationToken)
                .ConfigureAwait(false);
            return;
        }
        target = capturedTarget;

        departingRun = runs?.Find(target.CharacterId);
        returnRuntime = departingRun is not null && currentViews is not null
            ? await currentViews.TryLoadAsync(target, cancellationToken).ConfigureAwait(false)
            : null;
        returnPosition = departingRun is not null ? onlineCharacters?.PositionOf(target) : null;
        execution = await PartyTransitionExecution.RunPartyAsync(
            sessions,
            townAreas,
            () =>
            {
                if (!sessions.IsInWorld(leader)
                    || !sessions.IsInWorld(target))
                {
                    return ValueTask.FromResult(PartyOperationResult.Reject(
                        PartyFailureReason.CallerNotCurrent));
                }
                return commands.SendAsync(new RemovePartyMemberCommand(
                    leader.CharacterId, target.CharacterId), cancellationToken);
            }).ConfigureAwait(false);
        if (!execution.Result.Succeeded)
        {
            await RefuseAsync(context.Connection.ConnectionId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (departingRun is not null && runs?.Find(target.CharacterId) is null)
                departureSink?.ParticipantDeparted(departingRun.RunId, target.CharacterId);
            await notifications.DeliverAsync(
                    execution.Result.Notifications, execution.Recipients, cancellationToken)
                .ConfigureAwait(false);
            if (departingRun is not null
                && returnRuntime is not null
                && returnPosition is not null
                && townAreaProjection is not null
                && sessions.IsInWorld(target))
            {
                await townAreaProjection.EnterAsync(
                    [new TownAreaEntry(target, returnRuntime, returnPosition)],
                    cancellationToken).ConfigureAwait(false);
            }
        }
        logger.LogInformation(
            "WALKOUT_PARTY_MEMBER leader={Leader} target={Target} slot={Slot} accepted={Accepted} failure={Failure}",
            leader.CharacterId, target.CharacterId, message.TargetSlot,
            execution.Result.Succeeded, execution.Result.Failure);
    }

    private ValueTask RefuseAsync(
        NetworkConnectionId connectionId,
        CancellationToken cancellationToken) =>
        sender.SendCmdAsync(connectionId, new RemovePartyMemberRefusal(), cancellationToken);

    private static PartyCharacterSnapshot Character(SelectedSessionIdentity session) =>
        new(session.AccountId, session.CharacterId, session.WireUserId, null);
}

public sealed class CallPartyMemberRealtimeInfoHandler(
    ISelectedSessionDomainService sessions,
    ICommandBus commands,
    PartyNotificationDelivery notifications,
    ILogger<CallPartyMemberRealtimeInfoHandler> logger)
    : ClientMessageHandler<CallPartyMemberRealtimeInfoRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        CallPartyMemberRealtimeInfoRequest message,
        CancellationToken cancellationToken)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogInformation(
                "CALL_PARTY_MEMBER_REALTIME_INFO ignored for non-current connection {ConnectionId}",
                context.Connection.ConnectionId);
            return;
        }

        var result = await (sessions.IsInWorld(session)
            ? commands.SendAsync(new QueryPartyRealtimeCommand(new PartyCharacterSnapshot(
                session.AccountId, session.CharacterId, session.WireUserId, null)), cancellationToken)
            : ValueTask.FromResult(new PartyRealtimeQueryResult(null))).ConfigureAwait(false);
        if (result.Snapshot is { IsVirtual: false } snapshot)
        {
            await notifications.DeliverRealtimeAsync(session, snapshot, cancellationToken)
                .ConfigureAwait(false);
        }
        logger.LogInformation(
            "CALL_PARTY_MEMBER_REALTIME_INFO character={CharacterId} inParty={InParty}",
            session.CharacterId, result.Snapshot is { IsVirtual: false });
    }
}

public sealed class SetPartyInfoHandler(
    ISelectedSessionDomainService sessions,
    ITownAreaDomainService townAreas,
    ICommandBus commands,
    PartyNotificationDelivery notifications,
    ILogger<SetPartyInfoHandler> logger) : ClientMessageHandler<SetPartyInfoRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        SetPartyInfoRequest message,
        CancellationToken cancellationToken)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogInformation(
                "SET_PARTY_INFO ignored for non-current connection {ConnectionId}",
                context.Connection.ConnectionId);
            return;
        }

        var execution = await PartyTransitionExecution.RunPartyAsync(
            sessions,
            townAreas,
            () => sessions.IsInWorld(session)
                && PartyCharacterProjection.TryCapture(session, townAreas, out var character)
                ? commands.SendAsync(new SetPartyInfoCommand(
                    character,
                    message.Settings,
                    message.Title), cancellationToken)
                : ValueTask.FromResult(PartyOperationResult.Reject(
                    PartyFailureReason.CallerNotCurrent))).ConfigureAwait(false);
        var result = execution.Result;
        if (result.Succeeded)
        {
            await notifications.DeliverAsync(
                    result.Notifications, execution.Recipients, cancellationToken)
                .ConfigureAwait(false);
        }
        logger.LogInformation(
            "SET_PARTY_INFO character={CharacterId} accepted={Accepted} failure={Failure}",
            session.CharacterId,
            result.Succeeded,
            result.Failure);
    }
}
