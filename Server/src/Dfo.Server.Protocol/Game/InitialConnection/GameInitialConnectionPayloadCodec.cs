using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Configuration;

namespace Dfo.Server.Protocol.Game.InitialConnection;

public static class GameInitialConnectionPayloadCodec
{
    private const int ServerIndex = 1;
    private const int InitialUdpPort1 = 12311;
    private const int InitialUdpPort2 = 12312;
    private const int CommandPacketCount = 1086;
    private const int NotificationPacketCount = 1036;

    public static byte[] EncodeBody(
        GameInitialConnectionNotice notice,
        GameLoginOptions options)
    {
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(options);
        if (notice.ChannelId is < 1 or > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"Channel ID '{notice.ChannelId}' cannot be represented by the initial connection notice.");
        }

        if (!IPAddress.TryParse(options.ServerIp, out var serverAddress)
            || serverAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidDataException(
                $"Game server address '{options.ServerIp}' is not a usable IPv4 address.");
        }

        using var body = new MemoryStream();
        body.WriteByte(0x01);
        WriteDString(body, $"ch.{notice.ChannelId}");
        WriteInt32(body, 0);
        WriteInt32(body, 0);
        body.WriteByte(ServerIndex);
        body.WriteByte((byte)notice.ChannelId);
        body.WriteByte(0x00);
        WriteInt32(body, checked((int)notice.PreparedAt.ToUnixTimeSeconds()));
        WriteInt32(body, 1);
        WriteDString(body, options.ServerIp);
        WriteInt32(body, InitialUdpPort1);
        WriteInt32(body, InitialUdpPort2);
        body.WriteByte(0x00);
        body.WriteByte(0x00);
        WriteInt32(body, CommandPacketCount);
        WriteInt32(body, NotificationPacketCount);
        return body.ToArray();
    }

    private static void WriteDString(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        WriteInt32(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}
