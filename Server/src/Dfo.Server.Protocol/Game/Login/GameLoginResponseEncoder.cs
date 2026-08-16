using System.Buffers.Binary;
using System.Text;

namespace Dfo.Server.Protocol.Game.Login;

internal sealed class GameLoginResponseEncoder : GameCmdMessageEncoder<GameLoginResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GameLoginResponse message) =>
        message.Succeeded
            ? OnePayload(GameProtocol.LoginType, EncodeSuccessBody(message))
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
    internal static byte[] EncodeSuccessBody(GameLoginResponse message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var endpointHost = Encoding.ASCII.GetBytes(message.EndpointHost);
        if (endpointHost.Length > 256)
        {
            throw new InvalidDataException(
                "The Game LOGIN endpoint host cannot exceed 256 ASCII bytes.");
        }

        ValidatePort(message.ConnectionPort, nameof(message.ConnectionPort));
        ValidatePort(message.EndpointPortA, nameof(message.EndpointPortA));
        ValidatePort(message.EndpointPortB, nameof(message.EndpointPortB));

        var body = new byte[checked(44 + endpointHost.Length)];
        body[0] = 0x01;
        body[1] = 18;
        body[2] = 0x00;
        body[3] = message.Environment;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4, 4), message.ConnectionPort);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8, 4), endpointHost.Length);
        endpointHost.CopyTo(body, 12);
        var offset = 12 + endpointHost.Length;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset, 4), message.EndpointPortA);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset + 4, 4), message.EndpointPortB);
        return body;
    }

    private static void ValidatePort(int port, string fieldName)
    {
        if (port is < 1 or > ushort.MaxValue)
        {
            throw new InvalidDataException(
                $"Game LOGIN field '{fieldName}' must be a valid TCP port.");
        }
    }
}
