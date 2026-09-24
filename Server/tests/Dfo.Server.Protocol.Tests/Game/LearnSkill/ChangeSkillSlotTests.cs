using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.LearnSkill;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Protocol.Tests.Game.LearnSkill;
public sealed class ChangeSkillSlotTests
{
    [Fact]
    public void Captured_request_is_registered_and_decodes_slots()
    {
        var services=new ServiceCollection();
        new ProtocolModule().ConfigureServices(services,new ConfigurationBuilder().Build());
        using var provider=services.BuildServiceProvider();
        var registration=Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),r=>r.Selector==1&&r.Type==0x001C);
        var decoder=(IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);
        var result=Assert.IsType<ChangeSkillSlotRequest>(decoder.Decode(GameMessageMetadata.Empty,Convert.FromHexString("000500FFFFFFFFD74132E1")));
        Assert.Equal((byte)0,result.Page); Assert.Equal((byte)5,result.First); Assert.Equal((byte)0,result.Second);
    }
    [Fact]
    public void Corrupt_and_truncated_request_cannot_mutate_slots()
    {
        var bytes=Convert.FromHexString("000500FFFFFFFFD74132E1");
        var decoder=new ChangeSkillSlotDecoder();
        for(var i=0;i<bytes.Length;i++)
        {
            var bad=bytes.ToArray();bad[i]^=1;
            Assert.Throws<InvalidDataException>(()=>decoder.Decode(GameMessageMetadata.Empty,bad));
            Assert.Throws<InvalidDataException>(()=>decoder.Decode(GameMessageMetadata.Empty,bytes.AsSpan(0,i)));
        }
    }
}
