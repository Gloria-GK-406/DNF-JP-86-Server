using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ReinforceItem;

internal sealed class ReinforceItemMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < 16)
            throw new InvalidDataException("UPGRADE_ITEM requires at least 16 body bytes.");
        var nameLength = BinaryPrimitives.ReadInt32LittleEndian(body[12..]);
        if (nameLength is < 0 or > 128 || nameLength > body.Length - 16)
            throw new InvalidDataException("UPGRADE_ITEM has an invalid item-name length.");
        return new ReinforceItemRequest(
            metadata,
            body[0],
            BinaryPrimitives.ReadInt16LittleEndian(body[2..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[4..]),
            BinaryPrimitives.ReadInt16LittleEndian(body[8..]),
            BinaryPrimitives.ReadInt16LittleEndian(body[10..]),
            body.Slice(16, nameLength).ToArray(),
            (InventoryContainerType)body[1]);
    }
}
