using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Configuration;
using Dfo.Server.Networking.Framing;

namespace Dfo.Server.Protocol.Game.Login;

/// <summary>
/// Encodes the Game LOGIN response. The success body moved here from the payload codec so that the
/// codec is left holding only the inbound direction; it was the one inbound codec that depended on
/// transport configuration, and that dependency lived entirely in this encoding.
/// </summary>
internal sealed class GameLoginResponseEncoder(GameLoginOptions options)
    : ServerMessageEncoder<GameLoginResponse>
{
    protected override IReadOnlyList<byte[]> EncodeMessage(GameLoginResponse message) =>
        message.Succeeded
            ? One(EncodeSuccess(options))
            : throw new InvalidOperationException(
                "The observed Game LOGIN protocol has no failure body yet.");

    internal static byte[] EncodeSuccess(GameLoginOptions options)
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
}
