using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Application.Channel;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Channel;

public sealed class ChannelEntryDomainServiceTests
{
    [Theory]
    [InlineData((byte)23)]
    [InlineData((byte)0)]
    public async Task Channel_environment_short_circuits_a_broken_default(byte environment)
    {
        var deployment = new Deployment { ThrowDefault=true };
        var domain = Domain(deployment,environment);
        var result = await domain.ResolveLoginAsync(10011,default);
        Assert.Equal(environment,result.Environment);
        Assert.Equal(["endpoints"],deployment.Reads);
    }

    [Fact]
    public async Task Fallback_is_read_before_endpoints_only_when_channel_has_no_override()
    {
        var deployment = new Deployment();
        var result = await Domain(deployment,null).ResolveLoginAsync(10011,default);
        Assert.Equal((byte)1,result.Environment);
        Assert.Equal(["environment","endpoints"],deployment.Reads);
    }

    [Fact]
    public async Task Ordinary_resolution_and_refusal_do_not_read_deployment()
    {
        var deployment = new Deployment { ThrowDefault=true,ThrowEndpoints=true };
        var domain = Domain(deployment,null);
        var unknown = await domain.ResolveAsync(19999,default);
        Assert.Equal(0,unknown.ChannelId); Assert.Equal(ChannelEntryKind.General,unknown.Kind);
        Assert.Null(domain.Refuse(unknown,1));
        var refusal=domain.Refuse(new(100,10011,ChannelEntryKind.SaintHorn),69);
        Assert.Equal((byte)0xFD,refusal!.ErrorCode);
        Assert.Equal("圣者之鸣号频道仅限70级及以上角色进入。",refusal.Message);
        Assert.Empty(deployment.Reads);
    }

    [Fact]
    public void Formal_channel_boundary_is_the_registered_concrete_singleton()
    {
        var services=new ServiceCollection(); services.AddLogging();
        services.AddSingleton(ChannelEntryOptions.Legacy);
        services.AddSingleton(new ChannelListenerOptions([],true));
        services.AddSingleton<IChannelLoginDeploymentSource>(new Deployment());
        services.AddSingleton<IChannelCatalog>(new Catalog(null));
        services.AddSingleton<IChannelScriptSource>(new Script());
        new ApplicationModule().ConfigureServices(services,new ConfigurationBuilder().Build());
        using var provider=services.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<ChannelEntryDomainService>(),provider.GetRequiredService<IChannelEntryDomainService>());
    }

    private static ChannelEntryDomainService Domain(Deployment deployment,byte? environment) => new(
        new ChannelContentRuleService(new Catalog(environment),new Script()),ChannelEntryOptions.Legacy,
        new ChannelListenerOptions([10011],false),deployment);
    private sealed class Catalog(byte? environment):IChannelCatalog
    {
        public ValueTask<IReadOnlyList<ChannelServerInfo>> GetChannelsAsync(CancellationToken ct)=>
            ValueTask.FromResult<IReadOnlyList<ChannelServerInfo>>([new(11,"#ch.11",500,0,"host",10011,environment)]);
    }
    private sealed class Script:IChannelScriptSource
    {
        public string Version=>throw new NotSupportedException();
        public ValueTask<byte[]> ReadAsync(CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Deployment:IChannelLoginDeploymentSource
    {
        public bool ThrowDefault {get;init;} public bool ThrowEndpoints {get;init;}
        public List<string> Reads {get;}=[];
        public byte ReadDefaultEnvironment()
        { Reads.Add("environment"); if(ThrowDefault) throw new InvalidOperationException("bad fallback");return 1; }
        public PublicGameLoginEndpoints ReadEndpoints()
        { Reads.Add("endpoints"); if(ThrowEndpoints) throw new InvalidOperationException("bad endpoints");return new("host",1,2,3); }
    }
}
