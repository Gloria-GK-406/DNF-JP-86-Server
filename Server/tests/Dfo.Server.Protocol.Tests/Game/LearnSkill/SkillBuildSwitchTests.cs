using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.LearnSkill;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Protocol.Tests.Game.LearnSkill;
public sealed class SkillBuildSwitchTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Registered_decoder_accepts_only_the_A21_two_page_shape(byte page)
    {
        var services=new ServiceCollection();
        new ProtocolModule().ConfigureServices(services,new ConfigurationBuilder().Build());
        using var provider=services.BuildServiceProvider();
        var registration=Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),r=>r.Selector==1&&r.Type==0x0104);
        var decoder=(IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);
        Assert.Equal(page,Assert.IsType<SwitchSkillBuildRequest>(decoder.Decode(GameMessageMetadata.Empty,[page])).Current);
        Assert.Throws<InvalidDataException>(()=>decoder.Decode(GameMessageMetadata.Empty,[]));
        Assert.Throws<InvalidDataException>(()=>decoder.Decode(GameMessageMetadata.Empty,[2]));
        Assert.Throws<InvalidDataException>(()=>decoder.Decode(GameMessageMetadata.Empty,[0,1]));
    }
}
