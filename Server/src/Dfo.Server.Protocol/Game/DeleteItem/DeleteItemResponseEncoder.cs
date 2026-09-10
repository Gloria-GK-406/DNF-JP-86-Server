using System.Buffers.Binary;

namespace Dfo.Server.Protocol.Game.DeleteItem;

internal sealed class DeleteItemResponseEncoder : GameCmdMessageEncoder<DeleteItemResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(DeleteItemResponse message) =>
        OnePayload(GameProtocol.DeleteItemType, EncodeBody(message));

    internal static byte[] EncodeBody(DeleteItemResponse message)
    {
        if (message.Receipt is not { } receipt)
            return [0, message.ErrorCode, (byte)message.Container];

        var body = new byte[11];
        body[0] = 1;
        body[1] = (byte)receipt.Container;
        body[2] = 1;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(3, 2), checked((short)receipt.SlotIndex));
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(5, 4), receipt.AppliedCount);
        // RVA D303E0 reads this trailing i16; the observed success builder writes zero.
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(9, 2), 0);
        return body;
    }
}
