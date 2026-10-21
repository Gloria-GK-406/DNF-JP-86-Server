using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Channel;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

public sealed class GmPlayerBrowserFixtureTests
{
    /// <summary>Opt-in isolated real HTTP fixture for the controller's production-bundle browser acceptance.</summary>
    [Fact]
    public async Task ExplicitBrowserFixture()
    {
        if (Environment.GetEnvironmentVariable("DNF_GM_BROWSER_FIXTURE") != "1") return;
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var allowed = Path.GetFullPath(Path.Combine(workspace, "artifacts/gm-directory-browser/spec003"));
        var directory = Path.GetFullPath(Environment.GetEnvironmentVariable("DNF_GM_BROWSER_FIXTURE_DIRECTORY") ?? allowed);
        if (!string.Equals(directory, allowed, StringComparison.OrdinalIgnoreCase)
            && !directory.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Browser fixture database must remain inside the isolated artifact directory.");
        if (File.Exists(Path.Combine(directory, "test.db"))) throw new InvalidOperationException("Use a fresh fixture directory; this entry never resets an existing database.");
        Directory.CreateDirectory(directory);
        await using var host = await GmTestHost.StartAsync(directory: directory, configuration: GmPlayerDirectoryHttpTests.ChannelConfig,
            listenerPort: 18083, configureServices: services => services.AddSingleton(new ChannelListenerOptions([21011, 21012], false)));
        await GmPlayerDirectoryHttpTests.SeedAsync(host);
        var first = await GmPlayerDirectoryHttpTests.ConnectAsync(host, 40, 21011);
        var session = GmPlayerDirectoryHttpTests.Publish(host, first, 40, 40, 21011);
        host.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().StepTo(session, 123, 234, 1);
        await GmPlayerDirectoryHttpTests.ConnectAsync(host, 41, 21012);
        using var page = await host.Client.GetAsync("/gm/"); page.EnsureSuccessStatusCode();
        Assert.Contains("<div id=\"root\"></div>", await page.Content.ReadAsStringAsync());
        await host.LoginAsync(await host.CsrfAsync());
        var overview = await GmPlayerDirectoryHttpTests.GetAsync(host, "overview");
        Assert.Equal(45, overview.GetProperty("totalAccounts").GetInt32());
        Assert.Equal(2, overview.GetProperty("onlineAccounts").GetInt32());
        Assert.Equal(1, overview.GetProperty("onlineCharacters").GetInt32());
        await File.WriteAllTextAsync(Path.Combine(directory, "ready.txt"), "http://127.0.0.1:18083/gm/\n");
        Console.WriteLine("GM_BROWSER_FIXTURE_URL=http://127.0.0.1:18083/gm/");
        Console.WriteLine("GM_BROWSER_FIXTURE_LOGIN=operator; PASSWORD=" + GmTestHost.Password);
        Console.WriteLine("45 accounts / 47 characters; online accounts 40,41; entered character 40 at (123,234), channel 11; stop by creating stop.txt.");
        if (Environment.GetEnvironmentVariable("DNF_GM_BROWSER_FIXTURE_VERIFY_ONLY") == "1") return;
        var deadline = DateTimeOffset.UtcNow.AddMinutes(10);
        while (DateTimeOffset.UtcNow < deadline && !File.Exists(Path.Combine(directory, "stop.txt"))) await Task.Delay(500);
    }
}
