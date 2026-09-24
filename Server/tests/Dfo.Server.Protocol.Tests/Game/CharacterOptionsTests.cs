using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.SaveCharacterOption;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Protocol.Tests.Game;
public sealed class CharacterOptionsTests
{
    [Fact]
    public void Upload_and_login_projection_round_trip_every_byte_without_aliasing()
    {
        var services=new ServiceCollection();new ProtocolModule().ConfigureServices(services,new ConfigurationBuilder().Build());
        using var provider=services.BuildServiceProvider();
        var registration=Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),r=>r.Selector==1&&r.Type==0x01C0);
        var decoder=(IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);
        var body=Enumerable.Range(0,516).Select(i=>(byte)(i%256)).ToArray();BinaryPrimitives.WriteUInt32LittleEndian(body,512);
        var request=Assert.IsType<SaveCharacterOptionRequest>(decoder.Decode(GameMessageMetadata.Empty,body));
        Assert.Equal(body,StateSynchronizationPayloadCodec.EncodeCharacterOption(request.Body));
        body[4]^=1;Assert.NotEqual(body[4],request.Body[4]);
        Assert.Equal(new byte[4],StateSynchronizationPayloadCodec.EncodeCharacterOption(null));
    }
    [Theory]
    [InlineData(0,0)]
    [InlineData(515,512)]
    [InlineData(517,512)]
    [InlineData(516,511)]
    [InlineData(516,-1)]
    public void Invalid_length_or_prefix_is_rejected(int length,int prefix)
    {
        var body=new byte[length];if(length>=4) BinaryPrimitives.WriteInt32LittleEndian(body,prefix);
        Assert.Throws<InvalidDataException>(()=>new SaveCharacterOptionDecoder().Decode(GameMessageMetadata.Empty,body));
    }
}
