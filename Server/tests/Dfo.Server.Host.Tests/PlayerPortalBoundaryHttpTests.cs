using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dfo.Server.GmWeb;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Host.Tests.PlayerPortalTestSupport;

namespace Dfo.Server.Host.Tests;

/// <summary>
/// SD-2: one boundary for both prefixes. Identities never cross (RQ-002), the transport and cross-site rules are the same
/// code (RQ-006), and the two logins are limited apart, also behind a trusted proxy (RQ-031, RQ-033).
/// </summary>
public sealed class PlayerPortalBoundaryHttpTests
{
    private const string Proxy = "192.0.2.1";
    private sealed record Via(string? Peer = null, string? For = null, string? Proto = null, string? Origin = null, string? Site = null);

    private static HttpRequestMessage Request(HttpMethod method, string path, Via via, string? csrf = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (via.Peer is not null) request.Headers.Add(GmTestHost.PeerHeader, via.Peer);
        if (via.For is not null) request.Headers.Add("X-Forwarded-For", via.For);
        if (via.Proto is not null) request.Headers.Add("X-Forwarded-Proto", via.Proto);
        if (via.Origin is not null) request.Headers.Add("Origin", via.Origin);
        if (via.Site is not null) request.Headers.Add("Sec-Fetch-Site", via.Site);
        if (csrf is not null) request.Headers.Add("X-GM-CSRF", csrf);
        return request;
    }
    private static async Task<string> TokenAsync(HttpClient client, string path, Via via)
    {
        using var response = await client.SendAsync(Request(HttpMethod.Get, path, via));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("token").GetString()!;
    }
    private static Task<HttpResponseMessage> PlayerLoginAsync(HttpClient client, Via via, string csrf, string password = Password, string name = "alice") =>
        client.SendAsync(Request(HttpMethod.Post, "/api/player/v1/auth/login", via, csrf, new { loginName = name, password }));
    private static Task<HttpResponseMessage> GmLoginAsync(HttpClient client, Via via, string csrf, string password = GmTestHost.Password, string name = "operator") =>
        client.SendAsync(Request(HttpMethod.Post, "/api/gm/v1/auth/login", via, csrf, new { loginName = name, password }));
    private static Dictionary<string, string?> Trusting(string proxy) => new() { ["GmWeb:TrustedProxies:0"] = proxy };

    private static (string Method, string Path)[] Routes(GmTestHost host, string prefix) => host.Services.GetRequiredService<EndpointDataSource>().Endpoints
        .OfType<RouteEndpoint>()
        .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(prefix, StringComparison.Ordinal) == true
            && endpoint.Metadata.GetMetadata<GmPublicEndpoint>() is null)
        .SelectMany(endpoint => endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Select(method => (method, endpoint.RoutePattern.RawText!)))
        .ToArray();
    private static string Concrete(string template) => Regex.Replace(template, @"\{[^}]+\}", "1");

    private static async Task<HttpResponseMessage> WithCookieAsync(HttpClient raw, string method, string path, string cookie)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), Concrete(path));
        if (method is not ("GET" or "HEAD")) request.Content = JsonContent.Create(new { });
        request.Headers.Add("Cookie", cookie);
        return await raw.SendAsync(request);
    }

    /// <summary>AC-003 and R-2: a player session opens no GM route, item catalog and icons included, and the reverse.</summary>
    [Fact]
    public async Task Neither_session_opens_the_other_prefix()
    {
        var gmCookies = new CookieContainer();
        await using var host = await GmTestHost.StartAsync(cookies: gmCookies);
        await SeedAccountAsync(host, 1, "alice"); await SetPortalAsync(host);
        await GmCeraHttpTests.Login(host);
        var gmSecret = gmCookies.GetCookies(host.Address).Single(cookie => cookie.Name == "Dfo.Gm.Session").Value;
        using var player = new PlayerClient(host); await player.SignInAsync("alice");
        var playerSecret = player.Secret!;
        using var raw = RawClient(host);

        var gmRoutes = Routes(host, "/api/gm/v1/");
        foreach (var required in new[] { ("GET", "/api/gm/v1/mail/items"), ("GET", "/api/gm/v1/mail/items/{itemId:int}"), ("GET", "/api/gm/v1/items/{itemId:int}/icon"),
            ("GET", "/api/gm/v1/items/icon-pack"), ("GET", "/api/gm/v1/items/{itemId:int}/tooltip"),
            ("GET", "/api/gm/v1/player-portal/settings"), ("POST", "/api/gm/v1/player-portal/settings"), ("GET", "/api/gm/v1/player-portal/sessions"),
            ("POST", "/api/gm/v1/player-portal/sessions/kick"), ("POST", "/api/gm/v1/player-portal/sessions/kick-all"), ("GET", "/api/gm/v1/auth/session") })
            Assert.Contains(required, gmRoutes);
        foreach (var (method, path) in gmRoutes)
        {
            foreach (var cookie in new[] { $"{SessionCookie}={playerSecret}", $"Dfo.Gm.Session={playerSecret}" })
            {
                using var response = await WithCookieAsync(raw, method, path, cookie);
                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path} with {cookie[..cookie.IndexOf('=')]}: {(int)response.StatusCode}");
                Assert.Equal("请先登录管理后台。", (await JsonAsync(response)).GetProperty("message").GetString());
            }
        }
        var playerRoutes = Routes(host, "/api/player/v1/");
        Assert.Contains(("GET", "/api/player/v1/me"), playerRoutes);
        Assert.Contains(("POST", "/api/player/v1/auth/logout"), playerRoutes);
        foreach (var required in new[] { ("POST", "/api/player/v1/mail/send"), ("GET", "/api/player/v1/mail/items"), ("GET", "/api/player/v1/mail/items/{itemId:int}"),
            ("GET", "/api/player/v1/items/icon-pack"), ("GET", "/api/player/v1/items/{itemId:int}/icon"), ("GET", "/api/player/v1/items/{itemId:int}/tooltip") })
            Assert.Contains(required, playerRoutes);
        foreach (var (method, path) in playerRoutes)
        {
            foreach (var cookie in new[] { $"Dfo.Gm.Session={gmSecret}", $"{SessionCookie}={gmSecret}" })
            {
                using var response = await WithCookieAsync(raw, method, path, cookie);
                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path} with {cookie[..cookie.IndexOf('=')]}: {(int)response.StatusCode}");
                Assert.Equal("unauthenticated", await CodeAsync(response));
            }
        }
        // Each credential still works where it belongs.
        using (var own = await WithCookieAsync(raw, "GET", "/api/player/v1/me", $"{SessionCookie}={playerSecret}")) Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using (var own = await WithCookieAsync(raw, "GET", "/api/gm/v1/auth/session", $"Dfo.Gm.Session={gmSecret}")) Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    /// <summary>An antiforgery token is bound to its identity: a GM token is no player token and the reverse, in one browser.</summary>
    [Fact]
    public async Task An_antiforgery_token_does_not_cross_identities()
    {
        await using var host = await GmTestHost.StartAsync();
        await SeedAccountAsync(host, 1, "alice"); await SetPortalAsync(host);
        using var browser = new PlayerClient(host);
        using (var gm = await GmLoginAsync(browser.Client, new Via(), await TokenAsync(browser.Client, "/api/gm/v1/auth/csrf", new Via())))
            Assert.Equal(HttpStatusCode.OK, gm.StatusCode);
        await browser.SignInAsync("alice");
        var gmToken = await TokenAsync(browser.Client, "/api/gm/v1/auth/csrf", new Via());
        var playerToken = await TokenAsync(browser.Client, "/api/player/v1/auth/csrf", new Via());
        using (var crossed = await browser.Client.SendAsync(Request(HttpMethod.Post, "/api/player/v1/auth/logout", new Via(), gmToken)))
        { Assert.Equal(HttpStatusCode.Forbidden, crossed.StatusCode); Assert.Equal("forbidden", await CodeAsync(crossed)); }
        using (var crossed = await browser.Client.SendAsync(Request(HttpMethod.Post, "/api/gm/v1/auth/logout", new Via(), playerToken)))
            Assert.Equal(HttpStatusCode.Forbidden, crossed.StatusCode);
        using (var own = await browser.Client.SendAsync(Request(HttpMethod.Post, "/api/player/v1/auth/logout", new Via(), playerToken)))
            Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);
        using (var own = await browser.Client.SendAsync(Request(HttpMethod.Post, "/api/gm/v1/auth/logout", new Via(), gmToken)))
            Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);
    }

    /// <summary>AC-007: the player login and a player change pass the same antiforgery, origin and transport rules.</summary>
    [Fact]
    public async Task Player_requests_pass_the_same_cross_site_and_transport_rules()
    {
        await using (var host = await GmTestHost.StartAsync())
        {
            await SeedAccountAsync(host, 1, "alice"); await SetPortalAsync(host);
            using var player = new PlayerClient(host);
            var csrf = await player.CsrfAsync();
            var foreign = new Via(Origin: "https://untrusted.example");
            var crossSite = new Via(Site: "cross-site");
            foreach (var (via, token) in new[] { (new Via(), (string?)null), (new Via(), "not-a-token"), (foreign, csrf), (crossSite, csrf) })
            {
                using var refused = await player.Client.SendAsync(Request(HttpMethod.Post, "/api/player/v1/auth/login", via, token, new { loginName = "alice", password = Password }));
                Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
                Assert.Equal("forbidden", await CodeAsync(refused));
            }
            csrf = await player.SignInAsync("alice");
            foreach (var (via, token) in new[] { (new Via(), (string?)null), (new Via(), "not-a-token"), (foreign, csrf), (crossSite, csrf) })
            {
                using var refused = await player.Client.SendAsync(Request(HttpMethod.Post, "/api/player/v1/auth/logout", via, token));
                Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
                Assert.Equal("forbidden", await CodeAsync(refused));
            }
            using (var sameOrigin = await player.Client.SendAsync(Request(HttpMethod.Post, "/api/player/v1/auth/logout", new Via(Origin: "http://" + host.Address.Authority), csrf)))
                Assert.Equal(HttpStatusCode.NoContent, sameOrigin.StatusCode);
        }
        await using (var remote = await GmTestHost.StartAsync(remoteConnection: true))
        {
            await SetPortalAsync(remote);
            using var player = new PlayerClient(remote);
            Assert.Equal(HttpStatusCode.OK, (await player.Client.GetAsync("/api/player/v1/auth/status")).StatusCode);
            foreach (var response in new[] { await player.Client.GetAsync("/api/player/v1/auth/csrf"),
                await player.PostAsync("/api/player/v1/auth/login", null, new { loginName = "alice", password = Password }), await player.Client.GetAsync("/api/player/v1/me") })
            {
                using (response)
                {
                    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                    Assert.Equal("远程访问必须使用 HTTPS。", (await JsonAsync(response)).GetProperty("message").GetString());
                }
            }
        }
        await using (var proxied = await GmTestHost.StartAsync(configuration: Trusting(Proxy), peerOverride: true))
        {
            await SetPortalAsync(proxied);
            using var refused = await proxied.Client.SendAsync(Request(HttpMethod.Post, "/api/player/v1/auth/login",
                new Via(Proxy, "198.51.100.20", "http"), null, new { loginName = "alice", password = Password }));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal("远程访问必须使用 HTTPS。", (await JsonAsync(refused)).GetProperty("message").GetString());
        }
        var launcher = LauncherHost.Create();
        await using (var both = await GmTestHost.StartAsync(directory: launcher.Directory, configuration: launcher.Settings))
        {
            await SetPortalAsync(both);
            using var atLauncher = new HttpClient { BaseAddress = launcher.Url };
            Assert.Equal(HttpStatusCode.NotFound, (await atLauncher.GetAsync("/api/player/v1/auth/status")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await atLauncher.PostAsync("/api/player/v1/auth/login", JsonContent.Create(new { loginName = "alice", password = Password }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await both.Client.GetAsync("/api/player/v1/auth/status")).StatusCode);
        }
    }

    /// <summary>AC-031 (player side): behind the proxy each client is limited alone, and the two logins never limit each other.</summary>
    [Fact]
    public async Task Behind_a_trusted_proxy_player_and_gm_logins_are_limited_per_client_and_apart()
    {
        await using var host = await GmTestHost.StartAsync(configuration: Trusting(Proxy), peerOverride: true);
        await SeedAccountAsync(host, 1, "alice"); await SetPortalAsync(host);
        Via Client(string address) => new(Proxy, address, "https");
        var c1 = Client("198.51.100.11");
        var playerCsrf = await TokenAsync(host.Client, "/api/player/v1/auth/csrf", c1);
        for (var attempt = 0; attempt < 5; attempt++)
            using (var failed = await PlayerLoginAsync(host.Client, c1, playerCsrf, "wrong-password")) Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        using (var limited = await PlayerLoginAsync(host.Client, c1, playerCsrf))
        { Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode); Assert.Equal("rate_limited", await CodeAsync(limited)); }
        // Use up C1's whole source window (20 attempts) over several names, so a shared limiter would refuse any name from C1.
        for (var attempt = 0; attempt < 15; attempt++)
            using (var failed = await PlayerLoginAsync(host.Client, c1, playerCsrf, "wrong-password", $"guess{attempt % 3}")) Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        using (var limited = await PlayerLoginAsync(host.Client, c1, playerCsrf, Password, "fresh-name")) Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using (var other = await PlayerLoginAsync(host.Client, Client("198.51.100.12"), playerCsrf)) Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        var gmCsrf = await TokenAsync(host.Client, "/api/gm/v1/auth/csrf", c1);
        using (var gm = await GmLoginAsync(host.Client, c1, gmCsrf)) Assert.Equal(HttpStatusCode.OK, gm.StatusCode);

        var c3 = Client("198.51.100.13");
        gmCsrf = await TokenAsync(host.Client, "/api/gm/v1/auth/csrf", c3);
        for (var attempt = 0; attempt < 20; attempt++)
            using (var failed = await GmLoginAsync(host.Client, c3, gmCsrf, "wrong-password", $"guess{attempt % 4}")) Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        using (var limited = await GmLoginAsync(host.Client, c3, gmCsrf)) Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        playerCsrf = await TokenAsync(host.Client, "/api/player/v1/auth/csrf", c3);
        using (var player = await PlayerLoginAsync(host.Client, c3, playerCsrf)) Assert.Equal(HttpStatusCode.OK, player.StatusCode);

        // From an untrusted connection a forged X-Forwarded-For counts against the connection itself.
        playerCsrf = await TokenAsync(host.Client, "/api/player/v1/auth/csrf", new Via());
        for (var attempt = 0; attempt < 5; attempt++)
            using (var failed = await PlayerLoginAsync(host.Client, new Via(For: $"198.51.100.{30 + attempt}"), playerCsrf, "wrong-password"))
                Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        using (var limited = await PlayerLoginAsync(host.Client, new Via(For: "198.51.100.40"), playerCsrf)) Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    /// <summary>AC-032 (player side): the proxy's https makes the player cookie Secure and the same-origin check https; a forged one does not.</summary>
    [Fact]
    public async Task Forwarded_https_from_the_proxy_secures_the_player_cookie_and_origin_while_a_forged_one_does_not()
    {
        var cookies = new CookieContainer();
        await using var host = await GmTestHost.StartAsync(configuration: Trusting(Proxy), peerOverride: true, cookies: cookies);
        await SeedAccountAsync(host, 1, "alice"); await SetPortalAsync(host);
        var proxied = new Via(Proxy, "198.51.100.11", "https", "https://" + host.Address.Authority);
        var csrf = await TokenAsync(host.Client, "/api/player/v1/auth/csrf", proxied);
        using (var login = await PlayerLoginAsync(host.Client, proxied, csrf))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var cookie = SetCookie(login, SessionCookie)!;
            Assert.Contains("secure", cookie.ToLowerInvariant());
            // The test connection is plain http, where the client would withhold a Secure cookie; resend it as a plain one.
            cookies.Add(host.Address, new Cookie(SessionCookie, CookieValue(cookie), "/api/player"));
        }
        csrf = await TokenAsync(host.Client, "/api/player/v1/auth/csrf", proxied);
        using (var foreign = await host.Client.SendAsync(Request(HttpMethod.Post, "/api/player/v1/auth/logout", proxied with { Origin = "https://untrusted.example" }, csrf)))
            Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        using (var logout = await host.Client.SendAsync(Request(HttpMethod.Post, "/api/player/v1/auth/logout", proxied, csrf)))
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var forged = new Via(For: "198.51.100.12", Proto: "https");
        csrf = await TokenAsync(host.Client, "/api/player/v1/auth/csrf", forged);
        using (var sameSite = await PlayerLoginAsync(host.Client, forged with { Origin = "https://" + host.Address.Authority }, csrf))
            Assert.Equal(HttpStatusCode.Forbidden, sameSite.StatusCode);
        using (var login = await PlayerLoginAsync(host.Client, forged, csrf))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.DoesNotContain("secure", SetCookie(login, SessionCookie)!.ToLowerInvariant());
        }
    }

    /// <summary>AC-034: with every player source slot taken the administrator still signs in, and a new player source is refused.</summary>
    [Fact]
    public async Task A_full_player_limiter_leaves_the_gm_login_alone()
    {
        await using var host = await GmTestHost.StartAsync();
        await SeedAccountAsync(host, 1, "alice"); await SetPortalAsync(host);
        var limiter = host.Services.GetRequiredKeyedService<GmLoginLimiter>(GmLoginLimiter.PlayerKey);
        Assert.NotSame(host.Services.GetRequiredService<GmLoginLimiter>(), limiter);
        for (var source = 0; source < GmLoginLimiter.SourceCapacity; source++)
            Assert.True(limiter.TryBegin($"198.51.{source / 256}.{source % 256}", "filler", out _));
        await GmCeraHttpTests.Login(host);
        using var player = new PlayerClient(host);
        using var refused = await player.LoginAsync("alice");
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("rate_limited", await CodeAsync(refused));
    }
}
