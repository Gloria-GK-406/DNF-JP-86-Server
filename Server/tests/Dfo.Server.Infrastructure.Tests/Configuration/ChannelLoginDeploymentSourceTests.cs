using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Infrastructure.Tests.Configuration;

public sealed class ChannelLoginDeploymentSourceTests
{
    [Fact]
    public void Construction_is_lazy_and_fields_are_read_in_original_order_and_types()
    {
        var config = new RecordingConfig();
        var source = new ChannelLoginDeploymentSource(config);
        Assert.Empty(config.Reads);
        Assert.Equal((byte)1, source.ReadDefaultEnvironment());
        var endpoints = source.ReadEndpoints();
        Assert.Equal("original-host", endpoints.Host);
        Assert.Equal(-17, endpoints.ConnectionPort); // No new TCP validation in a legacy deployment.
        Assert.Equal(70000, endpoints.EndpointPortA);
        Assert.Equal(0, endpoints.EndpointPortB);
        Assert.Equal(["GameLogin:Environment:Byte", "GameLogin:ConnectionPort:Int32", "Game:EndpointHost:String",
            "GameLogin:EndpointPortA:Int32", "GameLogin:EndpointPortB:Int32"], config.Reads);
    }

    [Fact]
    public void Registration_keeps_the_same_snapshot_and_disabled_sparse_graph_is_resolvable()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        new InfrastructureModule().ConfigureServices(services, configuration);
        using var provider = services.BuildServiceProvider();
        var source = provider.GetRequiredService<IChannelLoginDeploymentSource>();
        configuration["GameLogin:Environment"] = "23";
        var missing = Assert.Throws<InvalidOperationException>(() => source.ReadDefaultEnvironment());
        Assert.Contains("GameLogin:Environment", missing.Message);
    }

    [Fact]
    public void Existing_snapshot_values_do_not_change_when_original_configuration_changes()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["GameLogin:Environment"]="13", ["GameLogin:ConnectionPort"]="-1", ["Game:EndpointHost"]="old",
            ["GameLogin:EndpointPortA"]="70000", ["GameLogin:EndpointPortB"]="0",
        }).Build();
        var snapshot = new ServerConfigService(configuration);
        var source = new ChannelLoginDeploymentSource(snapshot);
        configuration["GameLogin:Environment"]="24"; configuration["Game:EndpointHost"]="new";
        Assert.Equal((byte)13, source.ReadDefaultEnvironment());
        Assert.Equal("old",source.ReadEndpoints().Host);
        Assert.Equal(-1,source.ReadEndpoints().ConnectionPort);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bad")]
    [InlineData("256")]
    public void Missing_or_malformed_byte_is_validated_only_when_requested(string? value)
    {
        var source = new ChannelLoginDeploymentSource(new ServerConfigService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string,string?> { ["GameLogin:Environment"]=value }).Build()));
        var failure = Assert.Throws<InvalidOperationException>(() => source.ReadDefaultEnvironment());
        Assert.Contains("GameLogin:Environment",failure.Message);
    }

    private sealed class RecordingConfig : IServerConfigService
    {
        public List<string> Reads { get; } = [];
        public T GetConfig<T>(string key)
        {
            Reads.Add(key+":"+typeof(T).Name);
            object result=key switch
            {
                "GameLogin:Environment" => (byte)1, "GameLogin:ConnectionPort" => -17,
                "Game:EndpointHost" => "original-host", "GameLogin:EndpointPortA" => 70000,
                "GameLogin:EndpointPortB" => 0, _ => throw new InvalidOperationException(key),
            };
            return Assert.IsType<T>(result);
        }
    }
}
