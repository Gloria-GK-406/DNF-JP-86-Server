using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.EnchantByBead;

internal sealed class EnchantByBeadDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 6)
            throw new InvalidDataException("ENCHANT_BY_BEAD expects the six statically written A21 bytes.");
        return new EnchantByBeadRequest(metadata, (InventoryContainerType)body[0],
            BinaryPrimitives.ReadInt16LittleEndian(body[1..]),
            (InventoryContainerType)body[3],
            BinaryPrimitives.ReadInt16LittleEndian(body[4..]));
    }
}
