using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.MarriageProposal;

public sealed record WeddingChurchEnterRequest(GameMessageMetadata Metadata, uint Peer, uint ChurchArea, byte Flag)
    : IClientNetworkMessage { public ClientMessageType MessageType => ClientMessageType.GameWeddingChurchEntry; }
internal sealed record WeddingChurchEnterAck(int Status) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameWeddingChurchEntry; }
internal sealed record WeddingChurchPreparationNotification(MarriageChurchSnapshot Snapshot, ushort Remaining = 0) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameWeddingCeremonyPreparation; }

internal sealed class WeddingChurchEnterDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 23) throw new InvalidDataException("WEDDING_ENTER_CEREMONY requires 23 body bytes.");
        return new WeddingChurchEnterRequest(metadata, BinaryPrimitives.ReadUInt32LittleEndian(body[14..]),
            BinaryPrimitives.ReadUInt32LittleEndian(body[18..]), body[22]);
    }
}

internal static class MarriageChurchPayload
{
    public static byte[] Preparation(MarriageChurchSnapshot snapshot, ushort remaining = 0)
    {
        var bytes = new byte[79];
        // RVA00895C3F compares this with the partner id previously loaded by NOTI03D0.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, checked((uint)snapshot.Peer.Value));
        // RVA00F9CFC3 compares this record field with the player's town area.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), snapshot.Room.AreaId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), checked((uint)snapshot.Room.ReadyUntil.ToUnixTimeSeconds()));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), checked((uint)snapshot.Room.ChannelId));
        CopyName(snapshot.ApplicantName, bytes.AsSpan(17, 30));
        CopyName(snapshot.PartnerName, bytes.AsSpan(47, 30));
        // Native dstr11CE2 formats raw12 as the wedding channel; tier16 is ordinary zero.
        // In particular, bytes77..78 are not part of the second 30-byte name.
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(77), remaining);
        return bytes;
    }

    internal static void CopyName(byte[] name, Span<byte> destination)
    {
        var count = Math.Min(name.Length, destination.Length - 1);
        if (count < name.Length) while (count > 0 && (name[count] & 0xC0) == 0x80) count--;
        name.AsSpan(0, count).CopyTo(destination);
    }
}

internal sealed class WeddingChurchPreparationEncoder : GameNotiMessageEncoder<WeddingChurchPreparationNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingChurchPreparationNotification message) =>
        OnePayload(GameProtocol.WeddingCeremonyPreparationType, MarriageChurchPayload.Preparation(message.Snapshot, message.Remaining));
}
internal sealed class WeddingChurchEnterEncoder : GameCmdMessageEncoder<WeddingChurchEnterAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingChurchEnterAck message)
    {
        var body = new byte[5]; body[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1), message.Status);
        return OnePayload(GameProtocol.WeddingChurchEntryType, body);
    }
}

public sealed record MarriageChurchPreparationResult(MarriageChurchSnapshot? Snapshot);



public sealed class WeddingChurchEnterHandler(ICommandBus commands) : ClientMessageHandler<WeddingChurchEnterRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameWeddingChurchEnterCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, WeddingChurchEnterRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameWeddingChurchEnterCommand(context, message), ct).ConfigureAwait(false);
}
