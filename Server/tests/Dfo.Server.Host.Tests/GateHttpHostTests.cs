using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

/// <summary>The gate endpoints in a real Host beside GmWeb: shared facts with server-info and the listener boundary.</summary>
public sealed class GateHttpHostTests
{
    private const string Status = "/api/gate/v1/status";
    private const string Login = "/api/gate/v1/login";
    private const string Digest = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";
    private const string UnconfiguredWarning = "'LauncherAuth:GateToken' is not set";

    /// <summary>RQ-007: status and server-info read version, game ports and the archive digest through one implementation each.</summary>
    [Fact]
    public async Task Status_reports_the_version_game_ports_and_archive_digest_that_server_info_reports()
    {
        var token = GateTestToken.New();
        await using var gate = await GateHost.StartAsync(token, services => services.AddSingleton<IContentArchiveIdentity>(new FixedDigest(Digest)));

        var status = await gate.StatusAsync(token);
        var info = await gate.ServerInfoAsync();

        Assert.Equal(info.GetProperty("version").GetString(), status.GetProperty("version").GetString());
        Assert.Equal(info.GetProperty("gamePorts").GetRawText(), status.GetProperty("gamePorts").GetRawText());
        Assert.Equal(Digest, info.GetProperty("pvfSha256").GetString());
        Assert.Equal(Digest, status.GetProperty("pvfSha256").GetString());
        // No LauncherAuth:AdminUrl: the address comes from the GmWeb listener.
        Assert.Equal(new Uri(gate.Host.Address, "gm/").AbsoluteUri, status.GetProperty("admin").GetString());
        Assert.DoesNotContain(gate.Host.Logs.Messages, message => message.Contains(UnconfiguredWarning, StringComparison.Ordinal));
    }

    /// <summary>RQ-007: startedAt is fixed when the Host starts, not when status is first asked.</summary>
    [Fact]
    public async Task Status_started_at_is_when_the_host_started_not_when_status_was_first_asked()
    {
        var token = GateTestToken.New();
        var beforeStart = DateTimeOffset.UtcNow;
        await using var gate = await GateHost.StartAsync(token, services => services.AddSingleton<IContentArchiveIdentity>(new FixedDigest(Digest)));
        var afterStart = DateTimeOffset.UtcNow;
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        var asked = DateTimeOffset.UtcNow;

        var startedAt = (await gate.StatusAsync(token)).GetProperty("startedAt").GetDateTimeOffset();

        Assert.InRange(startedAt, beforeStart, afterStart);
        Assert.True(startedAt < asked, $"startedAt {startedAt:O} is not before the first status request at {asked:O}");
    }

    [PvfFact]
    public async Task Status_reports_the_real_archive_digest_that_server_info_reports()
    {
        var token = GateTestToken.New();
        await using var gate = await GateHost.StartAsync(token);

        var status = await gate.StatusAsync(token);
        var info = await gate.ServerInfoAsync();

        Assert.Matches("^[0-9a-f]{64}$", status.GetProperty("pvfSha256").GetString()!);
        Assert.Equal(info.GetProperty("pvfSha256").GetString(), status.GetProperty("pvfSha256").GetString());
    }

    /// <summary>The server-info reading moved into the shared implementation keeps its default.</summary>
    [Fact]
    public async Task Server_info_reports_the_default_game_port_when_none_is_configured()
    {
        await using var host = await GmTestHost.StartAsync();
        using var login = await host.LoginAsync(await host.CsrfAsync());

        var info = await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/server-info");

        Assert.Equal("[10011]", info.GetProperty("gamePorts").GetRawText());
    }

    /// <summary>AC-004's last row: on the GM listener a gate path is not found, before any authentication or counting.</summary>
    [Fact]
    public async Task Gate_paths_on_the_gm_listener_are_not_found_and_not_counted()
    {
        var token = GateTestToken.New();
        await using var gate = await GateHost.StartAsync(token, services => services.AddSingleton<IContentArchiveIdentity>(new FixedDigest(Digest)));

        for (var i = 0; i < 12; i++)
        {
            using var request = Get(Status, "wrong");
            using var response = await gate.Host.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using (var refused = await gate.Launcher.SendAsync(Get(Status, "wrong")))
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        using (var status = await gate.Launcher.SendAsync(Get(Status, token)))
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
    }

    /// <summary>
    /// Routing answers a method mismatch with 405 and <c>Allow</c> before the listener boundary or the gate
    /// looks at the request, so a gate path with the wrong method is 405 on the GM listener too, while the
    /// right method there is 404.
    /// </summary>
    [Fact]
    public async Task On_the_gm_listener_a_gate_path_is_not_found_but_a_wrong_method_is_not_allowed()
    {
        var token = GateTestToken.New();
        await using var gate = await GateHost.StartAsync(token, services => services.AddSingleton<IContentArchiveIdentity>(new FixedDigest(Digest)));

        using (var wrongMethod = await gate.Host.Client.SendAsync(Get(Login, token)))
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
            Assert.Contains(HttpMethod.Post.Method, wrongMethod.Content.Headers.Allow);
        }

        using (var rightMethod = await gate.Host.Client.SendAsync(PostCredentials(Login, token)))
            Assert.Equal(HttpStatusCode.NotFound, rightMethod.StatusCode);
    }

    /// <summary>
    /// Kestrel strips the surrounding whitespace of a header value before the gate sees it, so the right
    /// token followed by spaces or a tab passes. Sent over a raw socket so no client library normalizes it.
    /// </summary>
    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task Kestrel_strips_trailing_whitespace_so_the_right_token_still_passes(string trailing)
    {
        var token = GateTestToken.New();
        await using var gate = await GateHost.StartAsync(token, services => services.AddSingleton<IContentArchiveIdentity>(new FixedDigest(Digest)));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var address = gate.Launcher.BaseAddress!;
        using var client = new TcpClient();
        await client.ConnectAsync(address.Host, address.Port, timeout.Token);
        await using var stream = client.GetStream();
        var request = $"GET {Status} HTTP/1.1\r\nHost: {address.Authority}\r\nAuthorization: Bearer {token}{trailing}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), timeout.Token);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        var statusLine = await reader.ReadLineAsync(timeout.Token);

        Assert.Equal("HTTP/1.1 200 OK", statusLine);
    }

    /// <summary>RQ-002: without a token the gate is not there, and the Host says so once at startup.</summary>
    [Fact]
    public async Task Without_a_gate_token_the_gate_paths_are_not_found_and_the_host_warns()
    {
        await using var gate = await GateHost.StartAsync(token: null);

        foreach (var bearer in new[] { null, GateTestToken.New() })
        {
            using (var status = await gate.Launcher.SendAsync(Get(Status, bearer)))
                Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);
            foreach (var path in new[] { "/api/gate/v1/register", "/api/gate/v1/login" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, path)
                {
                    Content = JsonContent.Create(new { loginId = "gate-player", password = "gate password 1" }),
                };
                if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                using var response = await gate.Launcher.SendAsync(request);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.Equal("not_found", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            }
        }

        // The method check comes before the gate, so even without a token a wrong method is 405.
        using (var wrongMethod = await gate.Launcher.SendAsync(Get(Login, null)))
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
            Assert.Contains(HttpMethod.Post.Method, wrongMethod.Content.Headers.Allow);
        }

        Assert.Single(gate.Host.Logs.Messages, message => message.Contains(UnconfiguredWarning, StringComparison.Ordinal));
    }

    private static HttpRequestMessage Get(string path, string? bearer)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return request;
    }

    private static HttpRequestMessage PostCredentials(string path, string bearer)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { loginId = "gate-player", password = "gate password 1" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return request;
    }

    private sealed class GateHost(GmTestHost host, HttpClient launcher) : IAsyncDisposable
    {
        public GmTestHost Host { get; } = host;
        public HttpClient Launcher { get; } = launcher;

        /// <summary>A GmWeb Host that also serves LauncherAuth and the game ports, with the token passed through configuration only.</summary>
        public static async Task<GateHost> StartAsync(string? token, Action<IServiceCollection>? configureServices = null)
        {
            var launcher = LauncherHost.Create();
            var settings = new Dictionary<string, string?>(launcher.Settings) { ["LauncherAuth:GateToken"] = token };
            var host = await GmTestHost.StartAsync(directory: launcher.Directory, configuration: settings, configureServices: configureServices);
            return new GateHost(host, new HttpClient { BaseAddress = launcher.Url });
        }

        public async Task<JsonElement> StatusAsync(string token)
        {
            using var response = await Launcher.SendAsync(Get(Status, token));
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode} {body}");
            return JsonDocument.Parse(body).RootElement.Clone();
        }

        public async Task<JsonElement> ServerInfoAsync()
        {
            using (var login = await Host.LoginAsync(await Host.CsrfAsync()))
                Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            return await Host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/server-info");
        }

        public async ValueTask DisposeAsync()
        {
            Launcher.Dispose();
            await Host.DisposeAsync();
        }
    }

    private sealed class FixedDigest(string digest) : IContentArchiveIdentity
    {
        public ValueTask<string?> Sha256Async(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(digest);
    }
}
