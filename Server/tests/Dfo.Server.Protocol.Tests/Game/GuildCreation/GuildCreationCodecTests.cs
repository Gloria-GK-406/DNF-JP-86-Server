using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildCreation;

namespace Dfo.Server.Protocol.Tests.Game.GuildCreation;

public sealed class GuildCreationCodecTests
{
    [Fact]
    public void Name_and_created_notice_use_dstr_byte_length()
    {
        byte[] nameField = [6, 0, 0, 0, .. "公会"u8];
        var request = Assert.IsType<GuildCreationRequest>(
            new GuildCheckNameDecoder().Decode(GameMessageMetadata.Empty, nameField));
        Assert.False(request.Malformed);
        Assert.Equal("公会", request.Text);
        Assert.Equal(new byte[] { 12, 0, 0, 0 }.Concat(nameField),
            GuildCreationPayloadCodec.EncodeCreated(new(12), request.Text!));
        Assert.True(Assert.IsType<GuildCreationRequest>(
            new GuildCheckNameDecoder().Decode(GameMessageMetadata.Empty, [.. nameField, 0])).Malformed);
        Assert.True(Assert.IsType<GuildCreationRequest>(
            new GuildCheckNameDecoder().Decode(GameMessageMetadata.Empty, [4, 0, 0, 0, 0xB9, 0xAB, 0xBB, 0xE1])).Malformed);
    }

    [Fact]
    public void Final_creation_requires_exactly_the_captured_single_byte()
    {
        var decoder = new GuildFinalizeCreationDecoder();
        Assert.False(Assert.IsType<GuildCreationRequest>(decoder.Decode(GameMessageMetadata.Empty, [1])).Malformed);
        Assert.True(Assert.IsType<GuildCreationRequest>(decoder.Decode(GameMessageMetadata.Empty, [])).Malformed);
        Assert.True(Assert.IsType<GuildCreationRequest>(decoder.Decode(GameMessageMetadata.Empty, [1, 0])).Malformed);
    }
}
