using System.Buffers.Binary;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.UpgradeChronicle;

/// <summary>CMD 0x010F UPGRADE_CHRONICLE: one ticket acting on one equipment piece.</summary>
/// <remarks>
/// A21 builds the same 19 bytes from four windows (0x00C21B70 dispatches by <c>[emancipate ticket]</c>);
/// the requiem window's builder is 0x01011400. Slots are main-list indexes. The byte after the target ID
/// is decoded from the target object (vfunc +0x148) and has no known server meaning, so it is only kept.
/// Ticket kinds 2/3/19 always send material slot 0xFFFF and ID 0x0000FFFF; the server takes the
/// materials from the piece's <c>[emancipate] [input]</c>.
/// </remarks>
public sealed record UpgradeChronicleRequest(GameMessageMetadata Metadata,
    short TicketSlot, int TicketItemId, short TargetSlot, int TargetItemId, byte Opaque,
    short MaterialSlot, int MaterialItemId) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
    public ClientMessageType MessageType => ClientMessageType.GameUpgradeChronicle;
}

/// <summary>One success row: the client lowers that pile by <see cref="Count"/> or clears the slot when it would reach zero.</summary>
public sealed record UpgradeChronicleDebit(byte ListType, short Slot, int Count);

public sealed record UpgradeChronicleResponse(bool Success, byte ErrorCode = 0,
    IReadOnlyList<UpgradeChronicleDebit>? Debits = null) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameUpgradeChronicle;
}

internal sealed class UpgradeChronicleDecoder : IGameMessageDecoder
{
    public const int BodyLength = 19;

    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != BodyLength)
            throw new InvalidDataException($"UPGRADE_CHRONICLE body must be {BodyLength} bytes.");
        return new UpgradeChronicleRequest(metadata,
            BinaryPrimitives.ReadInt16LittleEndian(body),
            BinaryPrimitives.ReadInt32LittleEndian(body[2..]),
            BinaryPrimitives.ReadInt16LittleEndian(body[6..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[8..]),
            body[12],
            BinaryPrimitives.ReadInt16LittleEndian(body[13..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[15..]));
    }
}

/// <summary>
/// A21 handler 0x0111A950: success is <c>u8 1, u8 N, N×{u8 list, u16 slot, u32 count}</c> (ticket kind 5 has no
/// rows); failure is <c>u8 0, u8 code</c>. The handler never installs an item, so the changed piece follows as
/// an inventory notification.
/// </summary>
internal sealed class UpgradeChronicleEncoder : GameCmdMessageEncoder<UpgradeChronicleResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(UpgradeChronicleResponse message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(UpgradeChronicleResponse message)
    {
        if (!message.Success) return [0, message.ErrorCode];
        var debits = message.Debits ?? [];
        var body = new byte[2 + debits.Count * 7];
        body[0] = 1;
        body[1] = checked((byte)debits.Count);
        for (var i = 0; i < debits.Count; i++)
        {
            var row = body.AsSpan(2 + i * 7);
            row[0] = debits[i].ListType;
            BinaryPrimitives.WriteInt16LittleEndian(row[1..], debits[i].Slot);
            BinaryPrimitives.WriteInt32LittleEndian(row[3..], debits[i].Count);
        }
        return body;
    }
}
