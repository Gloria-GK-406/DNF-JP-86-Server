using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Party;

public sealed record SetUdpIpPortRequest(
    GameMessageMetadata Metadata,
    PartyEndpoint Endpoint,
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

        var endpoint = new PartyEndpoint(
            body[0],
            new PartyIpv4Address(body[1], body[2], body[3], body[4]),
            new PartyIpv4Address(body[5], body[6], body[7], body[8]),
            BinaryPrimitives.ReadUInt16LittleEndian(body[9..]),
            BinaryPrimitives.ReadUInt32LittleEndian(body[11..]));
        if (!endpoint.IsValid)
            throw new InvalidDataException("SET_UDP_IP_PORT contains an invalid endpoint prefix.");

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

        var settings = new PartySettings(canonical);
        var validation = rules.Validate(settings, memberCount: 0);
        if (validation != PartyModeRuleFailure.None)
        {
            throw new InvalidDataException(
                $"SET_PARTY_INFO settings failed shared validation: {validation}.");
        }
        return new SetPartyInfoRequest(metadata, settings, title);
    }
}

public sealed record UpdatePartyEndpointCommand(
    SelectedSessionIdentity Session,
    PartyEndpoint Endpoint) : ICommand<PartyOperationResult>;

public sealed class UpdatePartyEndpointCommandHandler(IPartyDomainService parties)
    : ICommandHandler<UpdatePartyEndpointCommand, PartyOperationResult>
{
    public ValueTask<PartyOperationResult> HandleAsync(
        CommandExecutionContext context,
        UpdatePartyEndpointCommand command,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(parties.UpdateEndpoint(command.Session, command.Endpoint));
}

public sealed record RequestPartyInviteCommand(
    SelectedSessionIdentity Inviter,
    ushort TargetWireUserId) : ICommand<PartyOperationResult>;

public sealed class RequestPartyInviteCommandHandler(
    IPartyDomainService parties,
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
        return parties.RequestInvite(command.Inviter, command.TargetWireUserId, display);
    }
}

public sealed record AcceptPartyInviteCommand(
    SelectedSessionIdentity Invitee,
    ushort InviterWireUserId) : ICommand<PartyOperationResult>;

public sealed class AcceptPartyInviteCommandHandler(IPartyDomainService parties)
    : ICommandHandler<AcceptPartyInviteCommand, PartyOperationResult>
{
    public ValueTask<PartyOperationResult> HandleAsync(
        CommandExecutionContext context,
        AcceptPartyInviteCommand command,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(parties.AcceptInvite(command.Invitee, command.InviterWireUserId));
}

public sealed record LeavePartyCommand(SelectedSessionIdentity Session)
    : ICommand<PartyOperationResult>;

public sealed class LeavePartyCommandHandler(IPartyDomainService parties)
    : ICommandHandler<LeavePartyCommand, PartyOperationResult>
{
    public ValueTask<PartyOperationResult> HandleAsync(
        CommandExecutionContext context,
        LeavePartyCommand command,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(parties.LeavePartyExact(command.Session));
}

public sealed record QueryPartyRealtimeCommand(SelectedSessionIdentity Session)
    : ICommand<PartyRealtimeQueryResult>;

public sealed record PartyRealtimeQueryResult(PartySnapshot? Snapshot);

public sealed class QueryPartyRealtimeCommandHandler(IPartyDomainService parties)
    : ICommandHandler<QueryPartyRealtimeCommand, PartyRealtimeQueryResult>
{
    public ValueTask<PartyRealtimeQueryResult> HandleAsync(
        CommandExecutionContext context,
        QueryPartyRealtimeCommand command,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new PartyRealtimeQueryResult(
            parties.FindParty(command.Session)));
}

public sealed record SetPartyInfoCommand(
    SelectedSessionIdentity Session,
    PartySettings Settings,
    PartyTitle Title) : ICommand<PartyOperationResult>;

public sealed class SetPartyInfoCommandHandler(IPartyDomainService parties)
    : ICommandHandler<SetPartyInfoCommand, PartyOperationResult>
{
    public ValueTask<PartyOperationResult> HandleAsync(
        CommandExecutionContext context,
        SetPartyInfoCommand command,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(parties.CreateOrUpdateSettingsExact(
            command.Session,
            command.Settings,
            command.Title));
}

internal static class PartyTransitionExecution
{
    public static async ValueTask<TResult> RunAsync<TResult>(
        SelectedSessionTransitionGate transitionGate,
        SelectedSessionIdentity first,
        SelectedSessionIdentity? second,
        Func<ValueTask<TResult>> action,
        CancellationToken cancellationToken)
    {
        var accountIds = second is null || second.AccountId == first.AccountId
            ? [first.AccountId]
            : new[] { first.AccountId, second.AccountId }
                .OrderBy(value => value.Value)
                .ToArray();
        var leases = new List<IAsyncDisposable>(accountIds.Length);
        try
        {
            foreach (var accountId in accountIds)
            {
                leases.Add(await transitionGate
                    .AcquireAsync(accountId, UserAccessMode.Exclusive, cancellationToken)
                    .ConfigureAwait(false));
            }

            return await action().ConfigureAwait(false);
        }
        finally
        {
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
        }
    }
}

public sealed class SetUdpIpPortHandler(
    ISelectedSessionDomainService sessions,
    SelectedSessionTransitionGate transitionGate,
    ICommandBus commands,
    PartyNotificationDelivery notifications,
    ILogger<SetUdpIpPortHandler> logger) : ClientMessageHandler<SetUdpIpPortRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        SetUdpIpPortRequest message,
        CancellationToken cancellationToken)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogInformation("SET_UDP_IP_PORT ignored for non-current connection {ConnectionId}",
                context.Connection.ConnectionId);
            return;
        }

        var result = await PartyTransitionExecution.RunAsync(
            transitionGate,
            session,
            null,
            () => sessions.FindByConnection(context.Connection.ConnectionId) == session
                ? commands.SendAsync(
                    new UpdatePartyEndpointCommand(session, message.Endpoint), cancellationToken)
                : ValueTask.FromResult(PartyOperationResult.Reject(
                    PartyFailureReason.SessionNotCurrent)),
            cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
            await notifications.DeliverAsync(result.Notifications, cancellationToken)
                .ConfigureAwait(false);
        logger.LogInformation(
            "SET_UDP_IP_PORT character={CharacterId} accepted={Accepted} failure={Failure}",
            session.CharacterId, result.Succeeded, result.Failure);
    }
}

public sealed class RequestPeerHandler(
    ISelectedSessionDomainService sessions,
    SelectedSessionTransitionGate transitionGate,
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

        var target = sessions.FindByWireUserId(message.TargetWireUserId);
        if (target is null)
        {
            logger.LogInformation(
                "REQUEST_PEER target={Target} is not a current selected session",
                message.TargetWireUserId);
            return;
        }

        var result = await PartyTransitionExecution.RunAsync(
            transitionGate,
            inviter,
            target,
            () => sessions.FindByConnection(context.Connection.ConnectionId) == inviter
                && sessions.FindByWireUserId(message.TargetWireUserId) == target
                    ? commands.SendAsync(new RequestPartyInviteCommand(
                        inviter, message.TargetWireUserId), cancellationToken)
                    : ValueTask.FromResult(PartyOperationResult.Reject(
                        PartyFailureReason.SessionNotCurrent)),
            cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
            await notifications.DeliverAsync(result.Notifications, cancellationToken)
                .ConfigureAwait(false);
        logger.LogInformation(
            "REQUEST_PEER inviter={Inviter} target={Target} clientReportedReviveCoins={ReviveCoins} accepted={Accepted} failure={Failure}",
            inviter.WireUserId, message.TargetWireUserId, message.PeerValue,
            result.Succeeded, result.Failure);
    }
}

public sealed class ResponsePeerHandler(
    ISelectedSessionDomainService sessions,
    SelectedSessionTransitionGate transitionGate,
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

        var inviter = sessions.FindByWireUserId(message.InviterWireUserId);
        if (inviter is null)
        {
            logger.LogInformation(
                "RESPONSE_PEER inviter={Inviter} is not a current selected session",
                message.InviterWireUserId);
            return;
        }

        var result = await PartyTransitionExecution.RunAsync(
            transitionGate,
            invitee,
            inviter,
            () => sessions.FindByConnection(context.Connection.ConnectionId) == invitee
                && sessions.FindByWireUserId(message.InviterWireUserId) == inviter
                    ? commands.SendAsync(new AcceptPartyInviteCommand(
                        invitee, message.InviterWireUserId), cancellationToken)
                    : ValueTask.FromResult(PartyOperationResult.Reject(
                        PartyFailureReason.SessionNotCurrent)),
            cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
            await notifications.DeliverAsync(result.Notifications, cancellationToken)
                .ConfigureAwait(false);
        logger.LogInformation(
            "RESPONSE_PEER invitee={Invitee} inviter={Inviter} accepted={Accepted} failure={Failure}",
            invitee.WireUserId, message.InviterWireUserId, result.Succeeded, result.Failure);
    }
}

public sealed class LeavePartyHandler(
    ISelectedSessionDomainService sessions,
    SelectedSessionTransitionGate transitionGate,
    ICommandBus commands,
    PartyNotificationDelivery notifications,
    ILogger<LeavePartyHandler> logger) : ClientMessageHandler<LeavePartyRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        LeavePartyRequest message,
        CancellationToken cancellationToken)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogInformation("LEAVE_PARTY ignored for non-current connection {ConnectionId}",
                context.Connection.ConnectionId);
            return;
        }

        var result = await PartyTransitionExecution.RunAsync(
            transitionGate,
            session,
            null,
            () => sessions.FindByConnection(context.Connection.ConnectionId) == session
                ? commands.SendAsync(new LeavePartyCommand(session), cancellationToken)
                : ValueTask.FromResult(PartyOperationResult.Reject(
                    PartyFailureReason.SessionNotCurrent)),
            cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
            await notifications.DeliverAsync(result.Notifications, cancellationToken)
                .ConfigureAwait(false);
        logger.LogInformation(
            "LEAVE_PARTY character={CharacterId} accepted={Accepted} failure={Failure}",
            session.CharacterId, result.Succeeded, result.Failure);
    }
}

public sealed class CallPartyMemberRealtimeInfoHandler(
    ISelectedSessionDomainService sessions,
    SelectedSessionTransitionGate transitionGate,
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

        var result = await PartyTransitionExecution.RunAsync(
            transitionGate,
            session,
            null,
            () => sessions.FindByConnection(context.Connection.ConnectionId) == session
                ? commands.SendAsync(new QueryPartyRealtimeCommand(session), cancellationToken)
                : ValueTask.FromResult(new PartyRealtimeQueryResult(null)),
            cancellationToken).ConfigureAwait(false);
        if (result.Snapshot is { } snapshot)
        {
            await notifications.DeliverRealtimeAsync(session, snapshot, cancellationToken)
                .ConfigureAwait(false);
        }
        logger.LogInformation(
            "CALL_PARTY_MEMBER_REALTIME_INFO character={CharacterId} inParty={InParty}",
            session.CharacterId, result.Snapshot is not null);
    }
}

public sealed class SetPartyInfoHandler(
    ISelectedSessionDomainService sessions,
    SelectedSessionTransitionGate transitionGate,
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

        var result = await PartyTransitionExecution.RunAsync(
            transitionGate,
            session,
            null,
            () => sessions.FindByConnection(context.Connection.ConnectionId) == session
                ? commands.SendAsync(new SetPartyInfoCommand(
                    session,
                    message.Settings,
                    message.Title), cancellationToken)
                : ValueTask.FromResult(PartyOperationResult.Reject(
                    PartyFailureReason.SessionNotCurrent)),
            cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
        {
            await notifications.DeliverAsync(result.Notifications, cancellationToken)
                .ConfigureAwait(false);
        }
        logger.LogInformation(
            "SET_PARTY_INFO character={CharacterId} accepted={Accepted} failure={Failure}",
            session.CharacterId,
            result.Succeeded,
            result.Failure);
    }
}
