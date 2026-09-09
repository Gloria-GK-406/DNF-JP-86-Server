namespace Dfo.Server.Protocol.Game.SellItem;

internal sealed class SellItemResponseEncoder : GameCmdMessageEncoder<SellItemResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(SellItemResponse message) =>
        OnePayload(GameProtocol.SellItemType, EncodeBody(message));

    internal static byte[] EncodeBody(SellItemResponse message)
    {
        if (message.Receipt is not { } receipt)
            return [0, message.ErrorCode];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(receipt.Gold);
        writer.Write((byte)receipt.Container);
        writer.Write(checked((short)receipt.SlotIndex));
        writer.Write(checked((short)receipt.Count));
        return stream.ToArray();
    }
}
