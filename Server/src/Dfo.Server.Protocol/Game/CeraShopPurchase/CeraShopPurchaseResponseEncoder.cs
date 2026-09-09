namespace Dfo.Server.Protocol.Game.CeraShopPurchase;

internal sealed class CeraShopPurchaseResponseEncoder : GameCmdMessageEncoder<CeraShopPurchaseResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CeraShopPurchaseResponse message) =>
        OnePayload(0x0040, EncodeBody(message));

    internal static byte[] EncodeBody(CeraShopPurchaseResponse message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        if (message.ProductId is { } product)
        {
            if (product <= 0) throw new InvalidDataException("A successful purchase must identify its product.");
            writer.Write((byte)1); writer.Write((byte)0);
            writer.Write(-1); writer.Write(product);
            writer.Write(0); writer.Write(0); writer.Write(0);
            writer.Write((ushort)0);
        }
        else
        {
            if (message.ErrorCode is not (1 or 4 or 11))
                throw new InvalidDataException("This purchase error has no verified A21 response layout.");
            writer.Write((byte)0); writer.Write(message.ErrorCode); writer.Write((byte)0);
            writer.Write(-1); writer.Write(-1);
            writer.Write(0); writer.Write(0); writer.Write(0);
        }
        return stream.ToArray();
    }
}
