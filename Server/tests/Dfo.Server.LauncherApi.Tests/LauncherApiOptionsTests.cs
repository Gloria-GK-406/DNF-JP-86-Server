using Microsoft.Extensions.Configuration;

namespace Dfo.Server.LauncherApi.Tests;

public sealed class LauncherApiOptionsTests
{
    [Fact]
    public void DisabledApiDoesNotRequireOrInventAListener()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["LauncherAuth:Enabled"] = "false" }).Build();

        var options = LauncherApiOptions.FromConfiguration(configuration);

        Assert.False(options.Enabled);
        Assert.Null(options.ListenUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData("ftp://127.0.0.1:8080")]
    [InlineData("http://user@127.0.0.1:8080")]
    [InlineData("http://127.0.0.1:8080/?query=1")]
    [InlineData("http://127.0.0.1:8080/#fragment")]
    [InlineData("http://127.0.0.1:8080/api")]
    public void EnabledApiRejectsInvalidListenerUrls(string? value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["LauncherAuth:Enabled"] = "true",
                ["LauncherAuth:ListenUrl"] = value,
            }).Build();

        Assert.Throws<InvalidOperationException>(
            () => LauncherApiOptions.FromConfiguration(configuration));
    }

    [Fact]
    public void MalformedEnabledSwitchFailsInsteadOfSilentlyDisablingHttp()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LauncherAuth:Enabled"] = "sometimes",
            })
            .Build();

        Assert.Throws<InvalidOperationException>(
            () => LauncherApiOptions.FromConfiguration(configuration));
    }
}
