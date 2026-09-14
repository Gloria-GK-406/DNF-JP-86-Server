using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.InitialConnection;

public static class GameInitialConnectionPayloadCodec
{
    private const int ServerIndex = 1;
    /// <summary>The sizes of the client's two dispatch tables, which it checks against its own.</summary>
    /// <remarks>
    /// These are the A21 client's table sizes, read off a known-good session rather than counted
    /// from an enum: the notice's last three 32-bit fields there are 1271, 1218 and zero. The
    /// preceding client answered 1086 and 1036; sending those to A21 makes it drop the connection
    /// a few seconds after the notice.
    /// </remarks>
    private const int CommandPacketCount = 1271;

    private const int NotificationPacketCount = 1218;

    /// <summary>How many further addresses follow the one above. Always none.</summary>
    private const int ExtraAddressCount = 0;

    /// <summary>
    /// Two ASCII zero characters, not two zero bytes.
    /// </summary>
    /// <remarks>
    /// The known-good session carries <c>0x30 0x30</c> here. Writing <c>0x00 0x00</c> -- which is
    /// what this codec did while it targeted the older client -- leaves the frame the right length
    /// but the wrong content, and the client stops after one further command.
    /// </remarks>
    private const byte MarkerCharacter = (byte)'0';

    public static byte[] EncodeBody(GameInitialConnectionNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        if (notice.ChannelId is < 1 or > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"Channel ID '{notice.ChannelId}' cannot be represented by the initial connection notice.");
        }

        if (!IPAddress.TryParse(notice.EndpointHost, out var serverAddress)
            || serverAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidDataException(
                $"Game server address '{notice.EndpointHost}' is not a usable IPv4 address.");
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
        WriteDString(body, notice.EndpointHost);
        WriteInt32(body, A21UdpDiscoveryProtocol.PortA);
        WriteInt32(body, A21UdpDiscoveryProtocol.PortB);
        WriteInt32(body, ExtraAddressCount);
        body.WriteByte(MarkerCharacter);
        body.WriteByte(MarkerCharacter);
        WriteInt32(body, CommandPacketCount);
        WriteInt32(body, NotificationPacketCount);
        WriteInt32(body, 0);
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
