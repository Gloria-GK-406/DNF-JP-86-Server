using System.Buffers.Binary;
using System.Security.Cryptography;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.LearnSkill;

internal sealed class LearnSkillMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < 3)
            throw new InvalidDataException("BUY_SKILL requires page, count and mode.");
        var count = body[1];
        var logicalLength = 3 + 4 * count;
        var checksumLength = logicalLength <= 400 ? 4 : 0;
        if (body.Length != logicalLength + checksumLength)
            throw new InvalidDataException("BUY_SKILL count and body length disagree.");
        if (checksumLength != 0 && !HasValidChecksum(body[..logicalLength], body[logicalLength..]))
            throw new InvalidDataException("BUY_SKILL body checksum is invalid.");
        var changes = new CharacterSkillLearningChange[count];
        for (var index = 0; index < count; index++)
        {
            var offset = 2 + 4 * index;
            changes[index] = new(BinaryPrimitives.ReadUInt16LittleEndian(body[offset..]),
                body[offset + 2], body[offset + 3]);
        }
        return new LearnSkillRequest(metadata, body[0], Array.AsReadOnly(changes), body[logicalLength - 1]);
    }

    // A21 RVA 01034D10 -> 00026A20, documented with image hash in protocol/0x001D.
    // MD5 is a wire compatibility checksum, not an authentication primitive.
    private static bool HasValidChecksum(ReadOnlySpan<byte> body, ReadOnlySpan<byte> checksum)
    {
        Span<byte> md5 = stackalloc byte[16];
        MD5.HashData(body, md5);
        return checksum[0] == (byte)(md5[0] ^ md5[13] ^ md5[8] ^ md5[10] ^ 0x81)
            && checksum[1] == (byte)(md5[1] ^ md5[9] ^ md5[5] ^ md5[12] ^ 0x78)
            && checksum[2] == (byte)(md5[2] ^ md5[6] ^ md5[14] ^ md5[4] ^ 0x1a)
            && checksum[3] == (byte)(md5[3] ^ md5[15] ^ md5[11] ^ md5[7] ^ 0xbf);
    }
}
