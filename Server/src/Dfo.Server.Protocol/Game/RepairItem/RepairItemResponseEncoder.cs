using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.RepairItem;

internal sealed class RepairItemResponseEncoder : GameCmdMessageEncoder<RepairItemResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(RepairItemResponse message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(RepairItemResponse message)
    {
        if (message.ErrorCode != 0)
            return [0, message.ErrorCode];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(message.Gold);
        writer.Write((byte)(message.Slot?.Space ?? message.RepairAllSpace));
        writer.Write(checked((short)(message.Slot?.Index ?? -1)));
        writer.Write((short)0);
        return stream.ToArray();
    }
}
