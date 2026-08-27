using System.Buffers.Binary;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.ContentsPlayInfo;
using Dfo.Server.Protocol.Game.SaveGameOption;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests.Game.RosterScreenQueries;

/// <summary>
/// The three roster-screen protocols the 2026-08-16 capture pinned down: the option write-back,
/// the one-byte query that precedes selection, and selection itself.
/// </summary>
public sealed class RosterScreenQueryCodecTests
{
    private static byte[] OptionBody(int declaredLength, int actualBytes)
    {
        var body = new byte[sizeof(int) + actualBytes];
        BinaryPrimitives.WriteInt32LittleEndian(body, declaredLength);
        return body;
    }

    [Fact]
    public void SaveGameOption_ReadsTheLengthPrefixedBlob()
    {
        // The capture's real shape: u32(134) then 134 bytes, the same blob the server had just
        // pushed in NOTI 0x00AD.
        var body = OptionBody(134, 134);
        body[sizeof(int)] = 0x86;

        var request = SaveGameOption1PayloadCodec.Decode(GameMessageMetadata.Empty, body);

        Assert.Equal(134, request.Options.Length);
        Assert.Equal(0x86, request.Options[0]);
    }

    [Fact]
    public void SaveGameOption_KeepsTheDeclaredLengthEvenWhenTheBodyIsLonger()
    {
        // The length is a prefix, not the body size. Trusting the body size instead would silently
        // absorb any trailing bytes into the blob.
        var request = SaveGameOption1PayloadCodec.Decode(
            GameMessageMetadata.Empty,
            OptionBody(4, 12));

        Assert.Equal(4, request.Options.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void SaveGameOption_RefusesABodyTooShortForTheLengthPrefix(int bodyLength) =>
        Assert.Throws<InvalidDataException>(
            () => SaveGameOption1PayloadCodec.Decode(
                GameMessageMetadata.Empty, new byte[bodyLength]));

    [Fact]
    public void SaveGameOption_RefusesALengthThatRunsPastTheBody() =>
        // The hostile case: a declared length longer than what arrived must not read past the body.
        Assert.Throws<InvalidDataException>(
            () => SaveGameOption1PayloadCodec.Decode(
                GameMessageMetadata.Empty, OptionBody(64, 8)));

    [Fact]
    public void SaveGameOption_RefusesANegativeLength() =>
        Assert.Throws<InvalidDataException>(
            () => SaveGameOption1PayloadCodec.Decode(
                GameMessageMetadata.Empty, OptionBody(-1, 8)));

    [Fact]
    public void ContentsPlayInfo_ReadsItsSingleByte()
    {
        var request = ContentsPlayInfoPayloadCodec.Decode(
            GameMessageMetadata.Empty, [0x01]);

        Assert.Equal(0x01, request.Selector);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ContentsPlayInfo_RefusesAnyOtherLength(int bodyLength) =>
        Assert.Throws<InvalidDataException>(
            () => ContentsPlayInfoPayloadCodec.Decode(
                GameMessageMetadata.Empty, new byte[bodyLength]));

    [Fact]
    public void ContentsPlayInfoReplyIsTheEnvelopeThenEighteenBytes()
    {
        // Eighteen is what the client's handler reads (u8, u8, u32 x4) before its trail enters the
        // packed region. Under-filling is the dangerous direction: a short body makes the handler
        // pull the shortfall out of the next frame.
        var reply = ContentsPlayInfoPayloadCodec.EncodeResponse();

        Assert.Equal(19, reply.Length);
        Assert.Equal(0x01, reply[0]);
        Assert.All(reply[1..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void SelectCharacter_ReadsTheRosterPosition()
    {
        var request = SelectCharacterPayloadCodec.Decode(
            GameMessageMetadata.Empty, [0x01, 0x00]);

        Assert.Equal(1, request.RosterIndex);
    }

    [Fact]
    public void SelectCharacter_RefusesABodyTooShortForTheIndex() =>
        Assert.Throws<InvalidDataException>(
            () => SelectCharacterPayloadCodec.Decode(GameMessageMetadata.Empty, [0x01]));

    [Fact]
    public void TheUnboughtSkillPageEncodesAsAWireSentinelRatherThanOverflowing()
    {
        // Storage keeps -1 for "second skill page never bought"; the wire byte is 0xFF. Encoding
        // used to be a checked cast, so every character in that state -- which is every newly
        // created one -- threw OverflowException while building the entry batch.
        //
        // Read from USERINFO subtype 1, not subtype 0. A21's subtype 0 ends in a fixed block that
        // carries no skill-tree field, so the byte this guards no longer has a home there; subtype
        // 1 still writes it, and that is where the overflow would reappear.
        var unbought = EncodeExtendedUserStateWithSkillTree(-1);
        var pageZero = EncodeExtendedUserStateWithSkillTree(0);
        var pageOne = EncodeExtendedUserStateWithSkillTree(1);

        Assert.Equal(pageZero.Length, unbought.Length);
        Assert.Equal(pageZero.Length, pageOne.Length);

        // The field is one byte, so exactly one position may differ between any two encodings.
        // Asserting the value alone would pass on a body full of incidental 0x00s and 0x01s.
        var differing = Enumerable.Range(0, pageZero.Length)
            .Where(index => unbought[index] != pageZero[index] || pageZero[index] != pageOne[index])
            .ToArray();
        var offset = Assert.Single(differing);

        Assert.Equal(0xFF, unbought[offset]);
        Assert.Equal(0x00, pageZero[offset]);
        Assert.Equal(0x01, pageOne[offset]);
    }

    private static byte[] EncodeExtendedUserStateWithSkillTree(int storedIndex)
    {
        var source = Dfo.Server.Protocol.Tests.Game.SelectCharacter
            .SelectCharacterPayloadTestData.CreateComplete();
        var entry = source.CharacterEntry with
        {
            Public = source.CharacterEntry.Public with
            {
                Extras = source.CharacterEntry.Public.Extras with
                {
                    SkillTreeIndex = storedIndex,
                },
            },
        };

        return new ExtendedUserStateMessage(
            source.Character,
            entry.Public,
            entry.Skills,
            source.CharacterInventory.Containers
                .Single(value => value.ContainerType == InventoryContainerType.Equipment).Items,
            source.World.PreparedAt).EncodeBody();
    }
}
