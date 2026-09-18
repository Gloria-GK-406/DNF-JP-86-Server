namespace Dfo.Server.Protocol.Game.RepairItem;

internal sealed class RepairItemResponseEncoder : GameCmdMessageEncoder<RepairItemResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(RepairItemResponse message) =>
        OnePayload(GameProtocol.RepairItemType, EncodeBody(message));

    internal static byte[] EncodeBody(RepairItemResponse message)
    {
        if (message.Receipt is not { } receipt)
            return [0, message.ErrorCode];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(receipt.Gold);
        writer.Write((byte)receipt.Container);
        writer.Write(checked((short)receipt.SlotIndex));
        writer.Write((short)0);
        return stream.ToArray();
    }
}
