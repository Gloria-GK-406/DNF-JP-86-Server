using Dfo.Launcher.Configuration;

namespace Dfo.Launcher.Tests.Configuration;

public sealed class LauncherConfigurationTests
{
    [Fact]
    public void A_single_secure_base_url_is_normalized_and_unknown_settings_are_rejected()
    {
        var configuration = LauncherConfigurationParser.Parse(
            "# launcher endpoint\n[Launcher]\nBaseUrl=https://server.example/api\n");

        Assert.Equal(new Uri("https://server.example/api/"), configuration.BaseUrl);
        Assert.Throws<LauncherConfigurationException>(() =>
            LauncherConfigurationParser.Parse(
                "[Launcher]\nBaseUrl=https://server.example/\nChannelPort=7001\n"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("BaseUrl=https://server.example/")]
    [InlineData("[Other]\nBaseUrl=https://server.example/")]
    [InlineData("[Launcher]\n")]
    [InlineData("[Launcher]\nBaseUrl=")]
    [InlineData("[Launcher]\nBaseUrl=/relative")]
    [InlineData("[Launcher]\nBaseUrl=ftp://server.example/")]
    [InlineData("[Launcher]\nBaseUrl=http://server.example/")]
    [InlineData("[Launcher]\nBaseUrl=https://user:pass@server.example/")]
    [InlineData("[Launcher]\nBaseUrl=https://server.example/?q=1")]
    [InlineData("[Launcher]\nBaseUrl=https://server.example/#fragment")]
    [InlineData("[Launcher]\nBaseUrl=https://server.example/\nBaseUrl=https://other.example/")]
    [InlineData("[Launcher]\nBaseUrl=https://server.example/\n[Launcher]")]
    [InlineData("[Launcher]\nBaseUrl=https://server.example/\nGameHost=127.0.0.1")]
    public void Missing_duplicate_unknown_and_unsafe_settings_are_rejected(string text) =>
        Assert.Throws<LauncherConfigurationException>(() =>
            LauncherConfigurationParser.Parse(text));

    [Theory]
    [InlineData("http://localhost:8080", "http://localhost:8080/")]
    [InlineData("http://127.0.0.1:8080/api", "http://127.0.0.1:8080/api/")]
    [InlineData("http://[::1]:8080/", "http://[::1]:8080/")]
    [InlineData("https://203.0.113.10/service", "https://203.0.113.10/service/")]
    public void Loopback_http_and_any_https_base_are_accepted(string input, string expected)
    {
        var result = LauncherConfigurationParser.Parse($"[Launcher]\nBaseUrl={input}\n");

        Assert.Equal(new Uri(expected), result.BaseUrl);
    }
}
