using System.Buffers.Binary;
using System.Text;

namespace Dfo.Server.Protocol.Game.Login;

/// <summary>
/// Decodes the Game LOGIN request body: two length-prefixed ASCII strings.
/// </summary>
/// <remarks>
/// Inbound only. The success-body encoding that used to live here moved to the login encoder,
/// which is where its dependency on transport configuration belongs.
/// </remarks>
public static class GameLoginPayloadCodec
{
    private const int MaximumStringLength = 256;

    public static bool TryDecode(
        ReadOnlySpan<byte> body,
        out string loginId,
        out string ticket)
    {
        var offset = 0;
        if (!TryReadDString(body, ref offset, out loginId)
            || !TryReadDString(body, ref offset, out ticket)
            || string.IsNullOrEmpty(loginId))
        {
            loginId = string.Empty;
            ticket = string.Empty;
            return false;
        }

        return true;
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
