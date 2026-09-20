using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.CharacterDeath;

namespace Dfo.Server.Protocol.Tests.Game.CharacterDeath;

public sealed class CharacterDeathCodecTests
{
    [Fact]
    public void Observed_death_preserves_all_three_words()
    {
        var request = Assert.IsType<CharacterDeathRequest>(new CharacterDeathDecoder().Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("640226010000")));
        Assert.Equal((ushort)612, request.First);
        Assert.Equal((ushort)294, request.Second);
        Assert.Equal((ushort)0, request.Third);
        Assert.Equal(ClientMessageType.GameCharacterDeath, request.MessageType);
    }

    [Fact]
    public void Alternate_builder_has_no_third_word()
    {
        var request = Assert.IsType<CharacterDeathRequest>(new CharacterDeathDecoder().Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("64022601")));
        Assert.Null(request.Third);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public void Unexpected_lengths_are_refused(int length) => Assert.Throws<InvalidDataException>(
        () => new CharacterDeathDecoder().Decode(GameMessageMetadata.Empty, new byte[length]));

    [Fact]
    public void Death_notification_is_actor_and_two_zero_bytes() =>
        Assert.Equal(Convert.FromHexString("34120000"), CharacterLifeStateEncoder.EncodeDeath(0x1234));
}
