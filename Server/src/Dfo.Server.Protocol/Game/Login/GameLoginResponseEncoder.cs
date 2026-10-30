using System.Buffers.Binary;
using System.Text;

namespace Dfo.Server.Protocol.Game.Login;

internal sealed class GameLoginResponseEncoder : GameCmdMessageEncoder<GameLoginResponse>
{
    /// <summary>The value the A21 server reports in the second body byte.</summary>
    /// <remarks>
    /// A12 reported eighteen here. Both are transcribed from what each client's own server sends;
    /// nothing here knows what the client does with it.
    /// </remarks>
    private const byte ProtocolRevision = 20;

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GameLoginResponse message) =>
        message.Succeeded
            ? OnePayload(message, EncodeSuccessBody(message))
            : throw new InvalidOperationException(
                "The observed Game LOGIN protocol has no failure body yet.");

    /// <summary>
    /// Five leading bytes, a raw 4-byte port, a length-prefixed host string, two u32, then a
    /// twenty-four byte tail.
    /// </summary>
    /// <remarks>
    /// The A12 client took four leading bytes here, and a fifth pushed every later field along
    /// until it read the host length as 0x00000900 and configured no endpoint at all. A21 takes
    /// five: its own server sends <c>01 14 00 01 00</c> before the port, and reports twenty where
    /// A12 reported eighteen.
    ///
    /// This one is worth being explicit about: the A21 client <em>accepted</em> the four-byte
    /// version and went on to the roster, so nothing here was failing. It is changed to match the
    /// session the client is known to accept, not to fix an observed break -- and a later field
    /// that turns out to depend on the difference would have been very hard to trace back here.
    /// </remarks>
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

        var body = new byte[checked(45 + endpointHost.Length)];
        body[0] = 0x01;
        body[1] = ProtocolRevision;
        body[2] = 0x00;
        body[3] = message.Environment;
        body[4] = 0x00;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(5, 4), message.ConnectionPort);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(9, 4), endpointHost.Length);
        endpointHost.CopyTo(body, 13);
        var offset = 13 + endpointHost.Length;
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
