using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>
    /// RQ-050 through the production entry (TASK-31 section 5.2): a launcher login that supersedes a logged-in game
    /// connection closes it from its own <c>http:</c> CMD, so that connection's teardown is the CMD's child. The
    /// inheritance itself is pinned by <c>LifecycleExecutorWireTests</c> on a <c>bus:</c> root; this pins the root.
    /// </summary>
    [Fact]
    public async Task A_launcher_login_that_supersedes_a_game_connection_is_the_parent_of_its_teardown()
    {
        var teardowns = new TeardownRecorder();
        var launcher = LauncherHost.Create();
        // The LOGIN answer names the login environment; the launcher host leaves it unset.
        var settings = new Dictionary<string, string?>(launcher.Settings) { ["GameLogin:Environment"] = "1" };
        await using var host = await GmTestHost.StartAsync(directory: launcher.Directory, configuration: settings,
            configureServices: services => services.AddSingleton<Dfo.Server.Application.Commands.INetworkConnectionLifecycleHandler>(teardowns));
        using var http = new HttpClient { BaseAddress = launcher.Url };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var credentials = new { loginId = "superseded-player", password = "superseded password 1" };
        using (var registered = await http.PostAsJsonAsync("/api/launcher/v1/register", credentials, timeout.Token))
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var (mId, ticket) = await LoginAsync(http, credentials, timeout.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, int.Parse(launcher.Settings["Networking:GamePorts:0"]!), timeout.Token);
        var stream = client.GetStream();
        _ = await ReadFrameAsync(stream, timeout.Token);
        await stream.WriteAsync(GameFrame(0x0001, DStrings(mId, ticket)), timeout.Token);
        var login = await ReadFrameAsync(stream, timeout.Token);
        Assert.Equal((byte)1, login![0]);
        Assert.Equal(0x0001, BinaryPrimitives.ReadUInt16LittleEndian(login.AsSpan(1)));

        // A second launcher login of the same account supersedes the bound game connection.
        _ = await LoginAsync(http, credentials, timeout.Token);

        while (await ReadFrameAsync(stream, timeout.Token) is not null) { }
        var teardown = await teardowns.FirstAsync(timeout.Token);
        Assert.True(teardown.OnExecutor, "the teardown ran off the executor");
        var chain = Assert.NotNull(teardown.Chain);
        Assert.Equal(GameplayCallSource.Lifecycle, chain.Source);
        Assert.Equal("lifecycle:disconnected", chain.CommandType);
        Assert.Equal(1, chain.Depth);
        Assert.Null(chain.SourceConnection);
        Assert.Equal(new GameplayCallHop(GameplayCallSource.Http, "http:POST /api/launcher/v1/login"), Assert.Single(chain.Ancestors));
    }

    private static async Task<(string MId, string Ticket)> LoginAsync(HttpClient http, object credentials, CancellationToken ct)
    {
        using var login = await http.PostAsJsonAsync("/api/launcher/v1/login", credentials, ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var json = JsonDocument.Parse(await login.Content.ReadAsStreamAsync(ct));
        return (json.RootElement.GetProperty("mId").GetString()!, json.RootElement.GetProperty("ticket").GetString()!);
    }

    private static byte[] GameFrame(ushort type, byte[] body)
    {
        var frame = new byte[14 + body.Length];
        frame[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), type);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), (uint)frame.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(12), 1);
        body.CopyTo(frame.AsSpan(14));
        return frame;
    }

    private static byte[] DStrings(params string[] values) =>
    [
        .. values.SelectMany(value =>
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(value);
            var length = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            return length.Concat(bytes);
        }),
    ];

    /// <summary>One whole server frame (15-byte header), or null at a FIN between frames.</summary>
    private static async Task<byte[]?> ReadFrameAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[15];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        if (read == 0) return null;
        Assert.Equal(header.Length, read);
        var frame = new byte[checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(3)))];
        header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(header.Length), ct);
        return frame;
    }

    /// <summary>Read-only: where each teardown ran and as which chain.</summary>
    private sealed class TeardownRecorder : Dfo.Server.Application.Commands.INetworkConnectionLifecycleHandler
    {
        private readonly TaskCompletionSource<(bool OnExecutor, GameplayCallChain? Chain)> first =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask ConnectedAsync(Dfo.Server.Application.Commands.CommandExecutionContext execution, NetworkConnectionContext connection, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DisconnectedAsync(Dfo.Server.Application.Commands.CommandExecutionContext execution, NetworkConnectionContext connection, CancellationToken cancellationToken)
        {
            if (connection.EndpointKind == NetworkEndpointKind.Game)
                first.TrySetResult((GameplayJob.IsOnExecutor, GameplayJob.Current));
            return ValueTask.CompletedTask;
        }

        public Task<(bool OnExecutor, GameplayCallChain? Chain)> FirstAsync(CancellationToken ct) => first.Task.WaitAsync(ct);
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
