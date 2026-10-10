using System.Buffers.Binary;
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

    internal static bool HasValidChecksum(ReadOnlySpan<byte> body, ReadOnlySpan<byte> checksum) =>
        GameBodyChecksum.IsValid(body, checksum);
}
