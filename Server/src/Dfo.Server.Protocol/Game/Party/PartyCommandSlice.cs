using Dfo.Server.Application.Dispatching;
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

internal sealed class SetUdpIpPortHandler(ICommandBus commands)
    : ClientMessageHandler<SetUdpIpPortRequest>, IProtocolCommandMessageHandler<GameSetUdpIpPortCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        SetUdpIpPortRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameSetUdpIpPortCommand(context, message), cancellationToken);
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

internal sealed class RequestPeerHandler(ICommandBus commands)
    : ClientMessageHandler<RequestPeerRequest>, IProtocolCommandMessageHandler<GameRequestPeerCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        RequestPeerRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameRequestPeerCommand(context, message), cancellationToken);
}

internal sealed class ResponsePeerHandler(ICommandBus commands)
    : ClientMessageHandler<ResponsePeerRequest>, IProtocolCommandMessageHandler<GameResponsePeerCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        ResponsePeerRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameResponsePeerCommand(context, message), cancellationToken);
}

public sealed class LeavePartyHandler(ICommandBus commands)
    : ClientMessageHandler<LeavePartyRequest>, IProtocolCommandMessageHandler<GameLeavePartyCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, LeavePartyRequest message,
        CancellationToken cancellationToken) => _ = await commands.SendAsync(new GameLeavePartyCommand(context, message), cancellationToken);
}

public sealed class ChangePartyLeaderHandler(ICommandBus commands)
    : ClientMessageHandler<ChangePartyLeaderRequest>, IProtocolCommandMessageHandler<GameChangePartyLeaderCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ChangePartyLeaderRequest message,
        CancellationToken cancellationToken) => _ = await commands.SendAsync(new GameChangePartyLeaderCommand(context, message), cancellationToken);
}

public sealed class RemovePartyMemberHandler(ICommandBus commands)
    : ClientMessageHandler<RemovePartyMemberRequest>, IProtocolCommandMessageHandler<GameRemovePartyMemberCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, RemovePartyMemberRequest message,
        CancellationToken cancellationToken) => _ = await commands.SendAsync(new GameRemovePartyMemberCommand(context, message), cancellationToken);
}

internal sealed class CallPartyMemberRealtimeInfoHandler(ICommandBus commands)
    : ClientMessageHandler<CallPartyMemberRealtimeInfoRequest>, IProtocolCommandMessageHandler<GameCallPartyMemberRealtimeInfoCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        CallPartyMemberRealtimeInfoRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameCallPartyMemberRealtimeInfoCommand(context, message), cancellationToken);
}

internal sealed class SetPartyInfoHandler(ICommandBus commands)
    : ClientMessageHandler<SetPartyInfoRequest>, IProtocolCommandMessageHandler<GameSetPartyInfoCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        SetPartyInfoRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameSetPartyInfoCommand(context, message), cancellationToken);
}
