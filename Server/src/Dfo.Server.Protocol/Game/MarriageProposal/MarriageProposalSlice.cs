using System.Buffers.Binary;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.MarriageProposal;

public sealed record WeddingProposalRequest(GameMessageMetadata Metadata, uint Target, byte Action, byte First, byte Second)
    : IClientNetworkMessage { public ClientMessageType MessageType => ClientMessageType.GameWeddingProposal; }
public sealed record WeddingResponseRequest(GameMessageMetadata Metadata, uint Target, byte Action, byte First, byte Second, byte Outcome)
    : IClientNetworkMessage { public ClientMessageType MessageType => ClientMessageType.GameWeddingResponse; }
public sealed record WeddingProposalAck(int Status, uint Target, byte Action, byte First = 0, byte Second = 0)
    : IGameCmdMessage { public ServerMessageType MessageType => ServerMessageType.GameWeddingProposal; }
public sealed record WeddingResponseAck(int Status, uint Target, byte Action, byte Outcome)
    : IGameCmdMessage { public ServerMessageType MessageType => ServerMessageType.GameWeddingResponse; }
public sealed record WeddingProposalNotification(uint Actor, byte Action = 1, byte Tier = 0)
    : IGameNotiMessage { public ServerMessageType MessageType => ServerMessageType.GameWeddingProposalNotification; }
public sealed record WeddingResponseNotification(uint Actor, byte Outcome, byte Action = 1, int Status = 0)
    : IGameNotiMessage { public ServerMessageType MessageType => ServerMessageType.GameWeddingResponseNotification; }

internal sealed class WeddingProposalDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 21) throw new InvalidDataException("WEDDING_REQUEST requires 21 body bytes.");
        // A21 WriteHeader emits the outer frame header separately. WriteRaw then copies a
        // complete 21B native struct, including 14 opaque bytes before these seven fields.
        // The opaque region is not an embedded wire header and has no authority over identity.
        var fields = body[14..];
        return new WeddingProposalRequest(metadata, BinaryPrimitives.ReadUInt32LittleEndian(fields), fields[4], fields[5], fields[6]);
    }
}
internal sealed class WeddingResponseDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 22) throw new InvalidDataException("WEDDING_RESPONSE requires 22 body bytes.");
        var fields = body[14..];
        return new WeddingResponseRequest(metadata, BinaryPrimitives.ReadUInt32LittleEndian(fields), fields[4], fields[5], fields[6], fields[7]);
    }
}
internal static class WeddingProposalPayload
{
    public static byte[] Fields(int status, uint target, byte action, byte? outcome = null, byte tier = 0)
    {
        var body = new byte[outcome is null ? 11 : 12];
        BinaryPrimitives.WriteInt32LittleEndian(body, status);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), target);
        body[8] = action;
        body[9] = tier;
        if (outcome is { } value) body[11] = value;
        return body;
    }
    public static byte[] Cmd(int status, uint target, byte action, byte? outcome = null) =>
        [1, .. Fields(status, target, action, outcome)];
}
internal sealed class WeddingProposalAckEncoder : GameCmdMessageEncoder<WeddingProposalAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingProposalAck message) =>
        OnePayload(GameProtocol.WeddingProposalType, WeddingProposalPayload.Cmd(message.Status, message.Target, message.Action));
}
internal sealed class WeddingResponseAckEncoder : GameCmdMessageEncoder<WeddingResponseAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingResponseAck message) =>
        OnePayload(GameProtocol.WeddingResponseType, WeddingProposalPayload.Cmd(message.Status, message.Target, message.Action, message.Outcome));
}
internal sealed class WeddingProposalNotificationEncoder : GameNotiMessageEncoder<WeddingProposalNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingProposalNotification message) =>
        OnePayload(GameProtocol.WeddingProposalNotificationType, WeddingProposalPayload.Fields(0, message.Actor, message.Action, tier: message.Tier));
}
internal sealed class WeddingResponseNotificationEncoder : GameNotiMessageEncoder<WeddingResponseNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingResponseNotification message) =>
        OnePayload(GameProtocol.WeddingResponseNotificationType, WeddingProposalPayload.Fields(message.Status, message.Actor, message.Action, message.Outcome));
}



internal sealed class WeddingProposalHandler(ICommandBus commands) : ClientMessageHandler<WeddingProposalRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameWeddingProposalCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, WeddingProposalRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameWeddingProposalCommand(context, message), ct).ConfigureAwait(false);
}

internal sealed class WeddingResponseHandler(ICommandBus commands) : ClientMessageHandler<WeddingResponseRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameWeddingResponseCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, WeddingResponseRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameWeddingResponseCommand(context, message), ct).ConfigureAwait(false);
}
