using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.CharacterName;
using Dfo.Server.Protocol.Game.CreateCharacter;
using Dfo.Server.Protocol.Game.DeleteCharacter;

namespace Dfo.Server.Protocol.Tests.Game.CharacterLifecycle;

/// <summary>
/// Wire behaviour of the three character-lifecycle requests and their replies.
/// </summary>
/// <remarks>
/// Creation and deletion now have captured requests behind them; the name check still rests on the
/// reference server alone. That difference matters, because where a capture existed it contradicted
/// the reference: deletion's slot is a u16, not the byte both servers read, which made every
/// deletion decode as malformed.
///
/// The bounds cases are the part that holds regardless of whether any layout is right -- a hostile
/// length must not read past the body no matter what the fields turn out to mean.
/// </remarks>
public sealed class CharacterLifecyclePayloadCodecTests
{
    private static byte[] NameField(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        var field = new byte[sizeof(int) + bytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(field, bytes.Length);
        bytes.CopyTo(field.AsSpan(sizeof(int)));
        return field;
    }

    [Fact]
    public void CheckName_ReadsTheLengthPrefixedName()
    {
        var request = CheckCharacterNamePayloadCodec.Decode(
            GameMessageMetadata.Empty,
            NameField("さくら"));

        Assert.Equal(Encoding.UTF8.GetBytes("さくら"), request.RawName);
    }

    [Fact]
    public void Create_ReadsJobThenName()
    {
        var body = new byte[] { 0x07 }.Concat(NameField("Blade")).ToArray();

        var request = CreateCharacterPayloadCodec.Decode(GameMessageMetadata.Empty, body);

        Assert.Equal(7, request.Job);
        Assert.Equal("Blade"u8.ToArray(), request.RawName);
    }

    [Fact]
    public void Create_AcceptsABodyWithNoTrailingByte()
    {
        // The legacy server demanded one unread byte after the name. Dropping that demand is a
        // deliberate difference: refusing a valid creation over a byte whose meaning is unknown
        // would convert an open question into a user-visible failure.
        var body = new byte[] { 0x00 }.Concat(NameField("Ari")).ToArray();

        Assert.Equal("Ari"u8.ToArray(), CreateCharacterPayloadCodec
            .Decode(GameMessageMetadata.Empty, body).RawName);
    }

    [Fact]
    public void Delete_ReadsSlotThenName()
    {
        // Slot is a u16. A captured request is 06 00 | 04 00 00 00 | "00a6"; reading the slot as
        // one byte puts the length field over 00 04 00 00 and refuses every deletion.
        var body = new byte[] { 0x06, 0x00 }.Concat(NameField("00a6")).ToArray();

        var request = DeleteCharacterPayloadCodec.Decode(GameMessageMetadata.Empty, body);

        Assert.Equal(6, request.SlotIndex);
        Assert.Equal("00a6"u8.ToArray(), request.RawName);
    }

    [Fact]
    public void Delete_ReadsASlotAboveAByte()
    {
        var body = new byte[] { 0x01, 0x02 }.Concat(NameField("Far")).ToArray();

        Assert.Equal(0x0201, DeleteCharacterPayloadCodec
            .Decode(GameMessageMetadata.Empty, body).SlotIndex);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void ShortBodiesYieldAMessageWithNoName(int length)
    {
        // Never null: a dropped frame would leave the client waiting on a dialog forever, so a
        // malformed request has to survive decoding far enough to be refused out loud.
        var body = new byte[length];

        Assert.Null(CheckCharacterNamePayloadCodec.Decode(GameMessageMetadata.Empty, body).RawName);
        Assert.Null(CreateCharacterPayloadCodec.Decode(GameMessageMetadata.Empty, body).RawName);
        Assert.Null(DeleteCharacterPayloadCodec.Decode(GameMessageMetadata.Empty, body).RawName);
    }

    [Fact]
    public void ADeclaredLengthLongerThanTheBodyIsRefusedRatherThanRead()
    {
        // The length is attacker-controlled. Trusting it would read adjacent heap into a name.
        var body = new byte[] { 0x00, 0xFF, 0xFF, 0xFF, 0x7F, 0x41 };

        Assert.Null(CreateCharacterPayloadCodec.Decode(GameMessageMetadata.Empty, body).RawName);
        Assert.Null(DeleteCharacterPayloadCodec.Decode(GameMessageMetadata.Empty, body).RawName);
    }

    [Fact]
    public void ANegativeDeclaredLengthIsRefused()
    {
        var body = new byte[] { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x41 };

        Assert.Null(CreateCharacterPayloadCodec.Decode(GameMessageMetadata.Empty, body).RawName);
    }

    [Theory]
    [InlineData(CharacterNameStatus.Available, new byte[] { 0x01 })]
    [InlineData(CharacterNameStatus.AlreadyTaken, new byte[] { 0x00, 0x18 })]
    [InlineData(CharacterNameStatus.TooShort, new byte[] { 0x00, 0x14 })]
    [InlineData(CharacterNameStatus.TooLong, new byte[] { 0x00, 0x14 })]
    [InlineData(CharacterNameStatus.Malformed, new byte[] { 0x00, 0x9F })]
    public void CheckNameRepliesCarryTheLegacyCodes(CharacterNameStatus status, byte[] expected) =>
        Assert.Equal(expected, CheckCharacterNamePayloadCodec.Encode(status));

    [Theory]
    [InlineData(CharacterCreationStatus.Created, new byte[] { 0x01 })]
    [InlineData(CharacterCreationStatus.NameTaken, new byte[] { 0x00, 0x18 })]
    [InlineData(CharacterCreationStatus.InvalidName, new byte[] { 0x00, 0x9F })]
    [InlineData(CharacterCreationStatus.NoFreeSlot, new byte[] { 0x04 })]
    [InlineData(CharacterCreationStatus.MalformedRequest, new byte[] { 0x04 })]
    public void CreateRepliesKeepTheLegacyShapes(CharacterCreationStatus status, byte[] expected) =>
        Assert.Equal(expected, CreateCharacterPayloadCodec.Encode(status));

    [Fact]
    public void DeleteSuccessLeadsWithZeroAndCarriesTheCharacterId()
    {
        // Deliberately inverted against every other reply in this batch, where 0x00 leads a
        // refusal. Here the zero is a discriminator announcing that an id follows.
        Assert.Equal(
            [0x00, 0x2A, 0x00],
            DeleteCharacterPayloadCodec.Encode(CharacterDeletionStatus.Deleted, new CharacterId(42)));
    }

    [Theory]
    [InlineData(CharacterDeletionStatus.NameMismatch, new byte[] { 0x15, 0x00, 0x00 })]
    [InlineData(CharacterDeletionStatus.SlotEmpty, new byte[] { 0x02, 0x00, 0x00 })]
    [InlineData(CharacterDeletionStatus.MalformedRequest, new byte[] { 0x02, 0x00, 0x00 })]
    public void DeleteFailuresStillCarryAnId(CharacterDeletionStatus status, byte[] expected) =>
        // Three bytes even on failure: the client's handler reads the u16 on both branches, so a
        // one-byte refusal leaves it reading a nonexistent id and removing nothing.
        Assert.Equal(expected, DeleteCharacterPayloadCodec.Encode(status, null));
}
