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
/// No captured sample backs these layouts yet -- they are read off the legacy server, which is
/// evidence but not proof. What these tests pin down is therefore narrower than usual: that the
/// codec reproduces the layout we decided on, and above all that a hostile length cannot make it
/// read past the body. The second part holds regardless of whether the layout turns out correct.
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
        var body = new byte[] { 0x03 }.Concat(NameField("Gone")).ToArray();

        var request = DeleteCharacterPayloadCodec.Decode(GameMessageMetadata.Empty, body);

        Assert.Equal(3, request.SlotIndex);
        Assert.Equal("Gone"u8.ToArray(), request.RawName);
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
    [InlineData(CharacterDeletionStatus.NameMismatch, new byte[] { 0x15 })]
    [InlineData(CharacterDeletionStatus.SlotEmpty, new byte[] { 0x02 })]
    [InlineData(CharacterDeletionStatus.MalformedRequest, new byte[] { 0x02 })]
    public void DeleteFailuresAreBareNonZeroCodes(CharacterDeletionStatus status, byte[] expected) =>
        Assert.Equal(expected, DeleteCharacterPayloadCodec.Encode(status, null));
}
