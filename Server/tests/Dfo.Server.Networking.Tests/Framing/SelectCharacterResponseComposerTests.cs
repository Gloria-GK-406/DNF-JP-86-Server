using System.Buffers.Binary;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Networking.Framing;

namespace Dfo.Server.Networking.Tests.Framing;

public sealed class SelectCharacterResponseComposerTests
{
    [Fact]
    public void Encode_PreservesObservedSelectionAcknowledgement()
    {
        var createdAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var character = new ActiveCharacterState(
            new CharacterIdentityState(new CharacterId(0x1234), 4, [0x41], 7, 3),
            new CharacterProgressionState(86, 123),
            new CharacterFatigueState(156, 17),
            new CharacterPositionState(1, 2, 3, 4, 5),
            createdAt,
            null);
        var result = new SelectCharacterResult(
            character,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);

        var frame = Assert.Single(SelectCharacterResponseComposer.Encode(result));

        Assert.Equal(GameProtocol.Command, frame[0]);
        Assert.Equal(
            GameProtocol.SelectCharacterType,
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));
        Assert.Equal(frame.Length, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(3, 4)));
        var body = frame.AsSpan(GameServerFrameEncoder.HeaderLength);
        Assert.Equal(262, body.Length);
        Assert.Equal(1, body[0]);
        Assert.Equal(1_700_000_000, BinaryPrimitives.ReadInt32LittleEndian(body[5..9]));
        Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16LittleEndian(body[9..11]));
        Assert.Equal(156, BinaryPrimitives.ReadUInt16LittleEndian(body[13..15]));
        Assert.Equal(17, BinaryPrimitives.ReadUInt16LittleEndian(body[15..17]));
        Assert.Equal(4, body[218]);
        Assert.Equal(new byte[] { 1, 0x4E }, body[220..222].ToArray());
    }
}
