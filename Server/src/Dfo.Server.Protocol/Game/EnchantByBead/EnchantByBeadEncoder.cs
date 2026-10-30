using System.Buffers.Binary;

namespace Dfo.Server.Protocol.Game.EnchantByBead;

internal sealed class EnchantByBeadEncoder : GameCmdMessageEncoder<EnchantByBeadResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(EnchantByBeadResponse message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(EnchantByBeadResponse message)
    {
        if (!message.Success) return [0, message.ErrorCode];
        var body = new byte[4];
        body[0] = 1;
        body[1] = (byte)message.TargetList;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(2), message.TargetSlot);
        return body;
    }
}
