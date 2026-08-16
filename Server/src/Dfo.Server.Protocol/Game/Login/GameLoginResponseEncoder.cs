using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Networking.Configuration;

namespace Dfo.Server.Protocol.Game.Login;

/// <summary>
/// Encodes the Game LOGIN response. The success body moved here from the payload codec so that the
/// codec is left holding only the inbound direction; it was the one inbound codec that depended on
/// transport configuration, and that dependency lived entirely in this encoding.
/// </summary>
internal sealed class GameLoginResponseEncoder(GameLoginOptions options)
    : GameCmdMessageEncoder<GameLoginResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GameLoginResponse message) =>
        message.Succeeded
            ? OnePayload(GameProtocol.LoginType, EncodeSuccessBody(options))
            : throw new InvalidOperationException(
                "The observed Game LOGIN protocol has no failure body yet.");

    /// <summary>
    /// Field order is taken from the client's read sequence in the CMD 0x0001 handler
    /// (RVA 0x008C7E20): four u8, a raw 4-byte port, a length-prefixed host string, then two u32
    /// that the client hands to SetEndpoint together with the host. The legacy port of this body
    /// carried a fifth leading u8, which pushed every later field one byte along; the client then
    /// read the host length as 0x00000900, failed ReadDString's bounds check (RVA 0x01C92000) and
    /// configured no endpoint at all.
    /// </summary>
    internal static byte[] EncodeSuccessBody(GameLoginOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var serverIp = Encoding.ASCII.GetBytes(options.ServerIp);
        var body = new byte[checked(44 + serverIp.Length)];
        body[0] = 0x01;
        body[1] = 18;
        body[2] = 0x00;
        body[3] = options.Environment;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4, 4), options.LoginChannelPort);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8, 4), serverIp.Length);
        serverIp.CopyTo(body, 12);
        var offset = 12 + serverIp.Length;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset, 4), options.UnknownPort);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset + 4, 4), options.UnknownPort);
        return body;
    }
}
