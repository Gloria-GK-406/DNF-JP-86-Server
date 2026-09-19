using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.DisjointItem;

internal sealed class DisjointItemMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 9)
            throw new InvalidDataException(
                $"DISJOINT_ITEM requires 9 body bytes; received {body.Length}.");
        return new DisjointItemRequest(
            metadata,
            new GameSlot((InventoryContainerType)body[2],
                BinaryPrimitives.ReadInt16LittleEndian(body)),
            BinaryPrimitives.ReadInt16LittleEndian(body[3..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[5..]));
    }
}
