using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Execution;

namespace Dfo.Server.Host.Tests;

/// <summary>
/// RQ-007 and spec appendix A row 10 through the real HTTP pipeline: hashing stays off the executor,
/// the credential writes run as the launcher's HTTP CMDs and the GM login CMD.
/// </summary>
public sealed class CredentialEntryHttpTests
{
    [Fact]
    public async Task Password_hashing_runs_off_the_executor_and_credential_writes_run_as_http_and_gm_cmds()
    {
        var passwords = new GameplayCallLog();
        var stores = new GameplayCallLog();
        var launcher = LauncherHost.Create();
        await using var host = await GmTestHost.StartAsync(directory: launcher.Directory, configuration: launcher.Settings,
            configureServices: services =>
            {
                passwords.Decorate<IPasswordCredentialRuleService>(services);
                stores.Decorate<IAuthenticationPersistence>(services);
                stores.Decorate<IGmAdministrationPersistence>(services);
            });
        using var http = new HttpClient { BaseAddress = launcher.Url };
        var credentials = new { loginId = "entry-player", password = "entry password 1" };

        using (var registered = await http.PostAsJsonAsync("/api/launcher/v1/register", credentials))
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        using (var login = await http.PostAsJsonAsync("/api/launcher/v1/login", credentials))
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using (var gm = await host.LoginAsync(await host.CsrfAsync()))
            Assert.Equal(HttpStatusCode.OK, gm.StatusCode);

        // Bootstrap and registration hash; the launcher and GM logins verify.
        Assert.True(passwords.Of(nameof(IPasswordCredentialRuleService.Hash)).Count >= 2);
        Assert.True(passwords.Of(nameof(IPasswordCredentialRuleService.Verify)).Count >= 2);
        Assert.All(passwords.Calls, call => Assert.False(call.OnExecutor, $"{call.Method} ran on the executor as {call.CommandType}"));

        var created = Assert.Single(stores.Of(nameof(IAuthenticationPersistence.TryCreateAsync)));
        AssertCmd(created, GameplayCallSource.Http, "http:POST /api/launcher/v1/register");
        var issued = Assert.Single(stores.Calls, call => call.Service == nameof(IAuthenticationPersistence)
            && call.Method == nameof(IAuthenticationPersistence.AcquireWriteIntentAsync));
        AssertCmd(issued, GameplayCallSource.Http, "http:POST /api/launcher/v1/login");
        var session = Assert.Single(stores.Of(nameof(IGmAdministrationPersistence.SaveSessionAsync)));
        AssertCmd(session, GameplayCallSource.Gm, "gm:POST /api/gm/v1/auth/login");
    }

    [Fact]
    public async Task A_gm_administrator_disabled_between_the_credential_check_and_the_login_cmd_is_refused_and_audited()
    {
        var stores = new GameplayCallLog();
        var armed = false;
        GmTestHost? started = null;
        stores.After = async call =>
        {
            if (!armed || call.OnExecutor || call.Method != nameof(IGmAdministrationPersistence.FindAdministratorAsync)) return;
            armed = false;
            await started!.ExecuteAsync("UPDATE gm_administrators SET enabled=0;");
        };
        await using var host = started = await GmTestHost.StartAsync(configureServices: services =>
            stores.Decorate<IGmAdministrationPersistence>(services));
        var csrf = await host.CsrfAsync();

        armed = true;
        using var login = await host.LoginAsync(csrf);

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.False(armed);
        Assert.Equal(new[] { "Rejected" }, await host.ReadAsync("SELECT outcome FROM gm_audit WHERE action='Login';"));
        Assert.Equal(new[] { "0" }, await host.ReadAsync("SELECT COUNT(*) FROM gm_sessions;"));
    }

    internal static void AssertCmd(GameplayCall call, GameplayCallSource source, string commandType)
    {
        Assert.True(call.OnExecutor, $"{call.Service}.{call.Method} ran off the executor");
        Assert.Equal(source, call.Source);
        Assert.Equal(commandType, call.CommandType);
    }
}

/// <summary>A GM test host that also serves the launcher routes and the game ports, so a launcher login is served.</summary>
internal sealed record LauncherHost(string Directory, Uri Url, Dictionary<string, string?> Settings)
{
    internal static LauncherHost Create()
    {
        var directory = GmTestHost.NewDirectory();
        var script = Path.Combine(directory, "channel_info.etc");
        File.WriteAllBytes(script, [1]);
        var (channel, game, http) = (FreePort(), FreePort(), FreePort());
        var url = new Uri($"http://127.0.0.1:{http}");
        return new(directory, url, new()
        {
            ["Networking:Enabled"] = "true",
            ["Networking:UdpDiscoveryEnabled"] = "false",
            ["Networking:ChannelPort"] = channel.ToString(),
            ["Networking:GamePorts:0"] = game.ToString(),
            ["Channel:ScriptPath"] = script,
            ["Channel:PublicServerIp"] = "127.0.0.1",
            ["Channel:Channels:0:Id"] = "11",
            ["Channel:Channels:0:Name"] = "#ch.11",
            ["Channel:Channels:0:Host"] = "127.0.0.1",
            ["Channel:Channels:0:Port"] = game.ToString(),
            ["LauncherAuth:Enabled"] = "true",
            ["LauncherAuth:ListenUrl"] = url.AbsoluteUri,
            ["LauncherAuth:PublicChannel:Host"] = "127.0.0.1",
            ["LauncherAuth:PublicChannel:Port"] = channel.ToString(),
        });
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
