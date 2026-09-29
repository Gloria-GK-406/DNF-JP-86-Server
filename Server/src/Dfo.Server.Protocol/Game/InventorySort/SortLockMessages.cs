using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.InventorySort;

public sealed record SortLockRequest(GameMessageMetadata Metadata, GameSlot Slot, InventorySortLockAction Action)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => Action == InventorySortLockAction.Toggle
        ? ClientMessageType.GameSortLock : ClientMessageType.GameSortUnlock;
}
public sealed record SortLockResponse(BusinessSlot Slot, bool Locked) : IGameCmdMessage
{
    public ServerMessageType MessageType => Locked ? ServerMessageType.GameSortLock : ServerMessageType.GameSortUnlock;
}
internal sealed class SortLockDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        Parse(metadata, body, InventorySortLockAction.Toggle);
    internal static SortLockRequest Parse(GameMessageMetadata metadata, ReadOnlySpan<byte> body, InventorySortLockAction action)
    {
        if (body.Length != 3) throw new InvalidDataException($"SORT_LOCK requires 3 body bytes; received {body.Length}.");
        return new(metadata, new((InventoryContainerType)body[0], BinaryPrimitives.ReadInt16LittleEndian(body[1..])), action);
    }
}
internal sealed class SortUnlockDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        SortLockDecoder.Parse(metadata, body, InventorySortLockAction.Unlock);
}
internal sealed class SortLockResponseEncoder(IInventorySlotMappingRuleService slots) : GameCmdMessageEncoder<SortLockResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(SortLockResponse message)
    {
        var slot = slots.ToGameSlot(message.Slot);
        byte[] body = [1, (byte)slot.Space, 0, 0];
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(2), checked((short)slot.Index));
        return OnePayload(message.Locked ? GameProtocol.SortLockType : GameProtocol.SortUnlockType, body);
    }
}
