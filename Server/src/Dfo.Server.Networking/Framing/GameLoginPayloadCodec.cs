using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Networking.Configuration;

namespace Dfo.Server.Networking.Framing;

public static class GameLoginPayloadCodec
{
    private const int MaximumStringLength = 256;

    public static bool TryDecode(
        ReadOnlySpan<byte> body,
        out string loginId,
        out string passwordHash)
    {
        var offset = 0;
        if (!TryReadDString(body, ref offset, out loginId)
            || !TryReadDString(body, ref offset, out passwordHash)
            || string.IsNullOrEmpty(loginId))
        {
            loginId = string.Empty;
            passwordHash = string.Empty;
            return false;
        }

        return true;
    }

    public static byte[] EncodeSuccess(GameLoginOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var serverIp = Encoding.ASCII.GetBytes(options.ServerIp);
        var body = new byte[checked(45 + serverIp.Length)];
        body[0] = 0x01;
        body[1] = 18;
        body[2] = 0x00;
        body[3] = options.Environment;
        body[4] = 0x00;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(5, 4), options.LoginChannelPort);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(9, 4), serverIp.Length);
        serverIp.CopyTo(body, 13);
        var offset = 13 + serverIp.Length;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset, 4), options.UnknownPort);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset + 4, 4), options.UnknownPort);
        return GameServerFrameEncoder.Encode(GameProtocol.Command, GameProtocol.LoginType, body);
    }

    private static bool TryReadDString(
        ReadOnlySpan<byte> body,
        ref int offset,
        out string value)
    {
        value = string.Empty;
        if (offset > body.Length - 4)
        {
            return false;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(offset, 4));
        offset += 4;
        if (length is < 0 or > MaximumStringLength || offset > body.Length - length)
        {
            return false;
        }

        value = Encoding.ASCII.GetString(body.Slice(offset, length));
        offset += length;
        return true;
    }
}
