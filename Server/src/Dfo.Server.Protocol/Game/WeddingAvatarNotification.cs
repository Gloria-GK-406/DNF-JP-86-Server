using System.Buffers.Binary;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game;

internal sealed record WeddingAvatarNotification(MarriageChurchSnapshot Church, bool Restore = false) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameWeddingAvatar;
}

internal sealed class WeddingAvatarEncoder : GameNotiMessageEncoder<WeddingAvatarNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingAvatarNotification message)
    {
        var body = new byte[67];
        body[0] = message.Restore ? (byte)1 : (byte)0;
        WeddingWireName.Copy(message.Church.ApplicantName, body.AsSpan(2, 30));
        WeddingWireName.Copy(message.Church.PartnerName, body.AsSpan(32, 30));
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(62), message.Church.Room.AreaId);
        return OnePayload(message, body);
    }
}

internal static class WeddingWireName
{
    internal static void Copy(byte[] name, Span<byte> destination)
    {
        var count = Math.Min(name.Length, destination.Length - 1);
        if (count < name.Length)
        {
            while (count > 0 && (name[count] & 0xC0) == 0x80)
                count--;
        }
        name.AsSpan(0, count).CopyTo(destination);
    }
}
