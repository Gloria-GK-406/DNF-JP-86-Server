using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.RepairItem;

internal sealed class RepairItemMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.SequenceEqual(new byte[] { 0, 255, 255, 255, 255, 1, 0 }))
            return new RepairItemRequest(metadata, null, -1, 1, 0, 0);
        if (body.Length != 8 || body[5] != 0)
            throw new InvalidDataException($"REPAIR_ITEM requires an ordinary 8-byte body or the verified automatic 7-byte body; received {body.Length}.");
        var index = BinaryPrimitives.ReadInt16LittleEndian(body[1..]);
        GameSlot? slot = index == -1 ? null : new GameSlot((InventoryContainerType)body[0], index);
        return new RepairItemRequest(metadata, slot, BinaryPrimitives.ReadInt16LittleEndian(body[3..]),
            body[5], body[6], body[7], (InventoryContainerType)body[0]);
    }
}
