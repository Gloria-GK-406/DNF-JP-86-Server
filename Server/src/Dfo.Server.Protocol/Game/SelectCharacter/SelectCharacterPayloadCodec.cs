using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

internal static class SelectCharacterPayloadCodec
{
    public static SelectCharacterRequest Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body)
    {
        if (body.Length < sizeof(ushort))
        {
            throw new InvalidDataException(
                $"SELECT_CHARACTER requires a two-byte roster index; received '{body.Length}' bytes.");
        }

        return new SelectCharacterRequest(
            metadata,
            BinaryPrimitives.ReadUInt16LittleEndian(body));
    }
}
