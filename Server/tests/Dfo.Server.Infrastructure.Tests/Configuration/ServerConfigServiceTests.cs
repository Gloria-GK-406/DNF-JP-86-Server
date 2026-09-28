using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Infrastructure.Tests.Configuration;

public sealed class ServerConfigServiceTests
{
    [Fact]
    public void Black_diamond_defaults_on_and_can_be_disabled_at_startup()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        Assert.True(new ServerConfigService(configuration).GetConfig<bool>("Premium:BlackDiamondEnabled"));
        configuration["Premium:BlackDiamondEnabled"] = "false";
        var settings = new ServerConfigService(configuration);
        Assert.False(settings.GetConfig<bool>("Premium:BlackDiamondEnabled"));
        configuration["Premium:BlackDiamondEnabled"] = "true";
        Assert.False(settings.GetConfig<bool>("Premium:BlackDiamondEnabled"));
    }

    [Fact]
    public void Daily_reset_has_a_default_and_configuration_overrides_it()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        Assert.Equal(TimeSpan.FromHours(6), new ServerConfigService(configuration).GetConfig<TimeSpan>("Game:DailyResetTime"));
        configuration["Game:DailyResetTime"] = "08:00:00";
        var settings = new ServerConfigService(configuration);
        Assert.Equal(TimeSpan.FromHours(8), settings.GetConfig<TimeSpan>("Game:DailyResetTime"));
        configuration["Game:DailyResetTime"] = "09:00:00";
        Assert.Equal(TimeSpan.FromHours(8), settings.GetConfig<TimeSpan>("Game:DailyResetTime"));
    }

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
