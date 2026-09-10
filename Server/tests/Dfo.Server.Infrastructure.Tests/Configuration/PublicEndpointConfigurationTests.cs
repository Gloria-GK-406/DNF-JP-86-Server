using Dfo.Server.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Infrastructure.Tests.Configuration;

public sealed class PublicEndpointConfigurationTests
{
    [Fact]
    public void ThreePublicEndpointKindsRemainIndependentAndAreProjectedExactly()
    {
        var snapshot = PublicEndpointConfiguration.FromConfiguration(Configuration());

        Assert.Equal("203.0.113.10", snapshot.ChannelBootstrap.Host);
        Assert.Equal(7001, snapshot.ChannelBootstrap.Port);
        Assert.Collection(
            snapshot.ChannelGameEndpoints,
            endpoint =>
            {
                Assert.Equal("198.51.100.11", endpoint.Host);
                Assert.Equal(10011, endpoint.Port);
            },
            endpoint =>
            {
                Assert.Equal("198.51.100.12", endpoint.Host);
                Assert.Equal(10012, endpoint.Port);
            });
        Assert.Equal("192.0.2.20", snapshot.GameLogin.Host);
        Assert.Equal(10128, snapshot.GameLogin.ConnectionPort);
        Assert.Equal(17200, snapshot.GameLogin.EndpointPortA);
        Assert.Equal(17201, snapshot.GameLogin.EndpointPortB);
    }

    [Theory]
    [InlineData("LauncherAuth:PublicChannel:Host", null)]
    [InlineData("LauncherAuth:PublicChannel:Host", "::1")]
    [InlineData("LauncherAuth:PublicChannel:Port", null)]
    [InlineData("LauncherAuth:PublicChannel:Port", "0")]
    [InlineData("Channel:Channels:0:Host", null)]
    [InlineData("Channel:Channels:0:Host", "not-an-ip")]
    [InlineData("Channel:Channels:0:Port", null)]
    [InlineData("Channel:Channels:0:Port", "65536")]
    [InlineData("Game:EndpointHost", null)]
    [InlineData("Game:EndpointHost", "::1")]
    [InlineData("GameLogin:ConnectionPort", null)]
    [InlineData("GameLogin:ConnectionPort", "nope")]
    [InlineData("GameLogin:EndpointPortA", "0")]
    [InlineData("GameLogin:EndpointPortB", "65536")]
    public void MissingOrInvalidEndpointValuesFailClosed(string key, string? value)
    {
        var configuration = Configuration();
        configuration[key] = value;

        var exception = Assert.Throws<InvalidOperationException>(
            () => PublicEndpointConfiguration.FromConfiguration(configuration));

        Assert.Contains(key, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingAdvertisedChannelCollectionDoesNotFallBackToListeners()
    {
        var values = Baseline()
            .Where(entry => !entry.Key.StartsWith("Channel:Channels:", StringComparison.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        values["Networking:GamePorts:0"] = "10011";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => PublicEndpointConfiguration.FromConfiguration(configuration));

        Assert.Contains("Channel:Channels", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AdvertisedChannelsMustMapOneToOneToDistinctListeners()
    {
        var countMismatch = new ConfigurationBuilder().AddInMemoryCollection(
            Baseline()
                .Where(entry => entry.Key != "Networking:GamePorts:1")
                .ToDictionary(entry => entry.Key, entry => entry.Value)).Build();
        var mismatch = Assert.Throws<InvalidOperationException>(
            () => PublicEndpointConfiguration.FromConfiguration(countMismatch));
        Assert.Contains("one-to-one", mismatch.Message, StringComparison.Ordinal);

        var duplicate = Configuration();
        duplicate["Networking:GamePorts:1"] = "10011";
        var ambiguity = Assert.Throws<InvalidOperationException>(
            () => PublicEndpointConfiguration.FromConfiguration(duplicate));
        Assert.Contains("duplicate", ambiguity.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IConfigurationRoot Configuration() =>
        new ConfigurationBuilder().AddInMemoryCollection(Baseline()).Build();

    private static Dictionary<string, string?> Baseline() => new()
    {
        ["LauncherAuth:PublicChannel:Host"] = "203.0.113.10",
        ["LauncherAuth:PublicChannel:Port"] = "7001",
        ["Channel:Channels:0:Host"] = "198.51.100.11",
        ["Channel:Channels:0:Port"] = "10011",
        ["Channel:Channels:1:Host"] = "198.51.100.12",
        ["Channel:Channels:1:Port"] = "10012",
        ["Networking:GamePorts:0"] = "10011",
        ["Networking:GamePorts:1"] = "10012",
        ["Game:EndpointHost"] = "192.0.2.20",
        ["GameLogin:ConnectionPort"] = "10128",
        ["GameLogin:EndpointPortA"] = "17200",
        ["GameLogin:EndpointPortB"] = "17201",
    };
}
