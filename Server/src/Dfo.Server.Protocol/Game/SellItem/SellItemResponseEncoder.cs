namespace Dfo.Server.Protocol.Game.SellItem;

internal sealed class SellItemResponseEncoder : GameCmdMessageEncoder<SellItemResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(SellItemResponse message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(SellItemResponse message)
    {
        if (message.Slot is not { } slot)
            return [0, message.ErrorCode];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(message.Gold);
        writer.Write((byte)slot.Space);
        writer.Write(checked((short)slot.Index));
        writer.Write(checked((short)message.Count));
        return stream.ToArray();
    }
}
