using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.SellItem;

internal sealed class SellItemMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 13)
            throw new InvalidDataException($"SELL_ITEM requires 13 body bytes; received {body.Length}.");
        return new SellItemRequest(metadata, (InventoryContainerType)body[0],
            BinaryPrimitives.ReadInt16LittleEndian(body[1..]), BinaryPrimitives.ReadInt16LittleEndian(body[3..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[5..]), BinaryPrimitives.ReadInt32LittleEndian(body[9..]));
    }
}
