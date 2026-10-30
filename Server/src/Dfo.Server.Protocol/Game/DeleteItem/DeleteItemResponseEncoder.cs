using System.Buffers.Binary;

namespace Dfo.Server.Protocol.Game.DeleteItem;

internal sealed class DeleteItemResponseEncoder : GameCmdMessageEncoder<DeleteItemResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(DeleteItemResponse message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(DeleteItemResponse message)
    {
        if (!message.Success)
            return [0, message.ErrorCode, (byte)message.Slot.Space];

        var body = new byte[11];
        body[0] = 1;
        body[1] = (byte)message.Slot.Space;
        body[2] = 1;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(3, 2), checked((short)message.Slot.Index));
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(5, 4), message.AppliedCount);
        // RVA D303E0 reads this trailing i16; the observed success builder writes zero.
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(9, 2), 0);
        return body;
    }
}
