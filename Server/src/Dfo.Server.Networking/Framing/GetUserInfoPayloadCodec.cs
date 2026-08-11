using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Networking.Framing;

internal static class GetUserInfoPayloadCodec
{
    private const byte AccountRosterMode = 0x02;

    public static IClientNetworkMessage? Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body)
    {
        if (body.Length < 3)
        {
            return null;
        }

        _ = BinaryPrimitives.ReadUInt16LittleEndian(body);
        return body[2] switch
        {
            AccountRosterMode => new GetAccountCharacterRosterRequest(metadata),
            _ => null,
        };
    }
}
