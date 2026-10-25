using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Dfo.Server.Protocol.Game.CharacterDeath;

namespace Dfo.Server.Protocol.Tests.Game.CharacterDeath;

public sealed class CharacterDeathCodecTests
{
    [Theory]
    [InlineData(null,1)] [InlineData("0",0)] [InlineData("1",1)]
    public void Revival_decoder_uses_the_same_validated_startup_shape(string? configured,byte expected)
    {
        var services=new ServiceCollection().AddLogging();
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Dungeon:RevivalAckFlag"]=configured}).Build();
        new Dfo.Server.Protocol.ProtocolModule().ConfigureServices(services,config);
        using var provider=services.BuildServiceProvider();
        var options=provider.GetRequiredService<RevivalOptions>();Assert.Equal(expected,options.AckFlag);
        var decoder=provider.GetRequiredService<UseRevivalCoinDecoder>();
        Assert.Equal(expected,Assert.IsType<UseRevivalCoinRequest>(decoder.Decode(GameMessageMetadata.Empty,new byte[2])).AckFlag);
    }
    [Theory]
    [InlineData("2")] [InlineData("255")]
    public void Revival_startup_rejects_unsupported_ack_flags(string configured)
    {
        var services=new ServiceCollection().AddLogging();
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Dungeon:RevivalAckFlag"]=configured}).Build();
        Assert.Throws<InvalidOperationException>(()=>new Dfo.Server.Protocol.ProtocolModule().ConfigureServices(services,config));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Revival_ack_preserves_target_and_explicit_flag(byte flag)
    {
        var request = Assert.IsType<UseRevivalCoinRequest>(new UseRevivalCoinDecoder(new RevivalOptions(flag)).Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("3412")));
        Assert.Equal((ushort)0x1234, request.TargetActorId);
        Assert.Equal(flag,request.AckFlag);
        Assert.Equal(new byte[] { 1, 0x34, 0x12, flag },
            UseRevivalCoinEncoder.EncodeBody(new(true, request.TargetActorId, flag)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Revival_rejects_non_u16_requests(int length) => Assert.Throws<InvalidDataException>(
        () => new UseRevivalCoinDecoder().Decode(GameMessageMetadata.Empty, new byte[length]));

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
