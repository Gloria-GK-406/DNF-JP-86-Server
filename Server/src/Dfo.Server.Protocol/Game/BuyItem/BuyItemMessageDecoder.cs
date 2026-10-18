using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.BuyItem;

internal sealed class BuyItemMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 16)
            throw new InvalidDataException($"BUY_ITEM requires 16 body bytes; received {body.Length}.");

        var npc = BinaryPrimitives.ReadInt32LittleEndian(body[12..]);
        if (npc == 20011) return new HousingBuyItemRequest(metadata, BinaryPrimitives.ReadInt32LittleEndian(body), BinaryPrimitives.ReadInt32LittleEndian(body[4..]), BinaryPrimitives.ReadInt32LittleEndian(body[8..]), npc);
        return new BuyItemRequest(metadata,
            BinaryPrimitives.ReadInt32LittleEndian(body),
            BinaryPrimitives.ReadInt32LittleEndian(body[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[12..]));
    }
}
