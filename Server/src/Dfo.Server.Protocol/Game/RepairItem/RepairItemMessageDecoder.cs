using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.RepairItem;

internal sealed class RepairItemMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 8)
            throw new InvalidDataException($"REPAIR_ITEM requires 8 body bytes; received {body.Length}.");
        var index = BinaryPrimitives.ReadInt16LittleEndian(body[1..]);
        GameSlot? slot = index == -1 ? null : new GameSlot((InventoryContainerType)body[0], index);
        return new RepairItemRequest(metadata, slot, BinaryPrimitives.ReadInt16LittleEndian(body[3..]),
            body[5], body[6], body[7], (InventoryContainerType)body[0]);
    }
}
