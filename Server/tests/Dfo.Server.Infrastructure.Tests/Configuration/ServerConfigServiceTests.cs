using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Infrastructure.Tests.Configuration;

public sealed class ServerConfigServiceTests
{
    [Fact]
    public void GetConfig_ReadsRequiredScalarValuesFromTheStartupSnapshot()
    {
        var values = new Dictionary<string, string?>
        {
            ["GameLogin:EndpointHost"] = "192.0.2.20",
            ["GameLogin:ConnectionPort"] = "10128",
            ["GameLogin:Environment"] = "1",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        IServerConfigService service = new ServerConfigService(configuration);

        Assert.Equal("192.0.2.20", service.GetConfig<string>("GameLogin:EndpointHost"));
        Assert.Equal(10128, service.GetConfig<int>("GameLogin:ConnectionPort"));
        Assert.Equal((byte)1, service.GetConfig<byte>("GameLogin:Environment"));

        configuration["GameLogin:EndpointHost"] = "198.51.100.8";
        Assert.Equal("192.0.2.20", service.GetConfig<string>("GameLogin:EndpointHost"));
    }

    [Fact]
    public void GetConfig_RejectsMissingAndMalformedValuesWithTheKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GameLogin:ConnectionPort"] = "not-a-port",
            })
            .Build();
        IServerConfigService service = new ServerConfigService(configuration);

        var missing = Assert.Throws<InvalidOperationException>(
            () => service.GetConfig<string>("GameLogin:EndpointHost"));
        Assert.Contains("GameLogin:EndpointHost", missing.Message, StringComparison.Ordinal);

        var malformed = Assert.Throws<InvalidOperationException>(
            () => service.GetConfig<int>("GameLogin:ConnectionPort"));
        Assert.Contains("GameLogin:ConnectionPort", malformed.Message, StringComparison.Ordinal);
    }
}
