using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.DeleteItem;

internal sealed class DeleteItemMessageDecoder : IGameMessageDecoder
{
    private const int OrdinaryBodyLength = 19;

    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != OrdinaryBodyLength)
            throw new InvalidDataException(
                $"DELETE_ITEM ordinary request requires {OrdinaryBodyLength} body bytes; received {body.Length}.");

        return new DeleteItemRequest(
            metadata,
            (InventoryContainerType)body[0],
            body[1],
            BinaryPrimitives.ReadUInt16LittleEndian(body[2..]),
            BinaryPrimitives.ReadUInt16LittleEndian(body[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[6..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[10..]),
            BinaryPrimitives.ReadUInt32LittleEndian(body[14..]),
            body[18]);
    }
}
