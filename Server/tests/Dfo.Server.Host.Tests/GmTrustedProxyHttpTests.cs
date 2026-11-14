using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Dfo.Server.Host.Tests;

/// <summary>
/// #305 on the GM side: forwarded client address and scheme are adopted only from configured proxies,
/// and then drive the HTTPS rule, the same-origin check, the session cookie's Secure flag and the login
/// limiter. Without <see cref="GmTestHost.PeerHeader"/> a request is the real loopback connection.
/// </summary>
public sealed class GmTrustedProxyHttpTests
{
    private const string Proxy = "192.0.2.1";
    private const string ClientOne = "198.51.100.11";
    private const string ClientTwo = "198.51.100.12";
    private const string Session = "Dfo.Gm.Session=";
    private const string HttpsRequired = "远程管理必须使用 HTTPS。";

    private sealed record Via(string? Peer = null, string? For = null, string? Proto = null, string? Origin = null);

    private static Dictionary<string, string?> Trusting(params string[] proxies)
        => proxies.Select((proxy, index) => KeyValuePair.Create($"GmWeb:TrustedProxies:{index}", (string?)proxy)).ToDictionary();

    private static HttpRequestMessage Request(HttpMethod method, string path, Via via, string? csrf = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (via.Peer is not null) request.Headers.Add(GmTestHost.PeerHeader, via.Peer);
        if (via.For is not null) request.Headers.Add("X-Forwarded-For", via.For);
        if (via.Proto is not null) request.Headers.Add("X-Forwarded-Proto", via.Proto);
        if (via.Origin is not null) request.Headers.Add("Origin", via.Origin);
        if (csrf is not null) request.Headers.Add("X-GM-CSRF", csrf);
        return request;
    }

    private static async Task<string> CsrfAsync(GmTestHost host, Via via)
    {
        using var response = await host.Client.SendAsync(Request(HttpMethod.Get, "/api/gm/v1/auth/csrf", via));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private static Task<HttpResponseMessage> LoginAsync(GmTestHost host, Via via, string csrf, string password = GmTestHost.Password)
        => host.Client.SendAsync(Request(HttpMethod.Post, "/api/gm/v1/auth/login", via, csrf, new { loginName = "operator", password }));

    private static string SessionCookie(HttpResponseMessage login)
        => login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(Session, StringComparison.Ordinal)).ToLowerInvariant();

    private static string SameSite(GmTestHost host) => "https://" + host.Address.Authority;

    private static async Task AssertLimitedAfterFiveFailuresAsync(GmTestHost host, Func<int, Via> via)
    {
        var csrf = await CsrfAsync(host, via(0));
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await LoginAsync(host, via(attempt), csrf, "wrong-password");
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }
        using var limited = await LoginAsync(host, via(5), csrf, "wrong-password");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    /// <summary>AC-030: unconfigured, forwarding headers change neither the limiter key, the cookie nor the origin check.</summary>
    [Fact]
    public async Task Without_trusted_proxies_forwarding_headers_change_nothing()
    {
        await using var host = await GmTestHost.StartAsync();
        var forged = new Via(For: ClientOne, Proto: "https");
        var csrf = await CsrfAsync(host, forged);
        using (var login = await LoginAsync(host, forged, csrf))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.DoesNotContain("secure", SessionCookie(login));
        }
        using (var crossScheme = await host.Client.SendAsync(Request(HttpMethod.Post, "/api/gm/v1/auth/logout",
            forged with { Origin = SameSite(host) }, await CsrfAsync(host, forged))))
            Assert.Equal(HttpStatusCode.Forbidden, crossScheme.StatusCode);

        await AssertLimitedAfterFiveFailuresAsync(host, attempt => new Via(For: $"198.51.100.{20 + attempt}", Proto: "https"));
    }

    /// <summary>AC-031 (GM side): the proxy's clients are limited separately; only the nearest hop counts; others count by connection.</summary>
    [Fact]
    public async Task Clients_behind_a_trusted_proxy_are_limited_separately_and_forged_headers_count_by_connection()
    {
        await using var host = await GmTestHost.StartAsync(configuration: Trusting(Proxy), peerOverride: true);
        await AssertLimitedAfterFiveFailuresAsync(host, _ => new Via(Proxy, ClientOne, "https"));

        var csrf = await CsrfAsync(host, new Via(Proxy, ClientTwo, "https"));
        // The client wrote ClientTwo itself; the proxy appended the real ClientOne. ForwardLimit = 1 reads only that hop.
        using (var nearestHop = await LoginAsync(host, new Via(Proxy, $"{ClientTwo}, {ClientOne}", "https"), csrf, "wrong-password"))
            Assert.Equal(HttpStatusCode.TooManyRequests, nearestHop.StatusCode);
        using (var other = await LoginAsync(host, new Via(Proxy, ClientTwo, "https"), csrf))
            Assert.Equal(HttpStatusCode.OK, other.StatusCode);

        // The real loopback connection is not a trusted proxy here: rotating X-Forwarded-For does not escape its own count.
        await AssertLimitedAfterFiveFailuresAsync(host, attempt => new Via(For: $"198.51.100.{30 + attempt}", Proto: "https"));
    }

    /// <summary>RQ-031: one hop at most, even when the hop it reaches is itself a listed proxy.</summary>
    [Fact]
    public async Task Only_one_forwarded_hop_is_read_even_when_it_is_a_listed_proxy()
    {
        await using var host = await GmTestHost.StartAsync(configuration: Trusting(Proxy, ClientOne), peerOverride: true);
        await AssertLimitedAfterFiveFailuresAsync(host, _ => new Via(Proxy, ClientOne, "https"));
        var chained = new Via(Proxy, $"{ClientTwo}, {ClientOne}", "https");
        using var response = await LoginAsync(host, chained, await CsrfAsync(host, chained), "wrong-password");
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    /// <summary>AC-032 (GM side): forwarded https gives a Secure cookie and a same-site https Origin passes; a forged one does neither.</summary>
    [Fact]
    public async Task Forwarded_https_from_the_proxy_sets_secure_and_passes_same_origin_while_a_forged_one_does_not()
    {
        var cookies = new CookieContainer();
        await using var host = await GmTestHost.StartAsync(configuration: Trusting(Proxy), peerOverride: true, cookies: cookies);
        var proxied = new Via(Proxy, ClientOne, "https", SameSite(host));
        var csrf = await CsrfAsync(host, proxied);
        using (var foreign = await LoginAsync(host, proxied with { Origin = "https://untrusted.example" }, csrf))
            Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        using (var login = await LoginAsync(host, proxied, csrf))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var cookie = SessionCookie(login);
            Assert.Contains("secure", cookie);
            // The test connection is plain http, where the client would withhold a Secure cookie; resend it as a plain one.
            cookies.Add(host.Address, new Cookie("Dfo.Gm.Session", login.Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith(Session, StringComparison.Ordinal)).Split(';')[0][Session.Length..], "/"));
        }
        csrf = await CsrfAsync(host, proxied);
        using (var session = await host.Client.SendAsync(Request(HttpMethod.Get, "/api/gm/v1/auth/session", proxied)))
            Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        using (var foreign = await host.Client.SendAsync(Request(HttpMethod.Post, "/api/gm/v1/auth/logout", proxied with { Origin = "https://untrusted.example" }, csrf)))
            Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        using (var logout = await host.Client.SendAsync(Request(HttpMethod.Post, "/api/gm/v1/auth/logout", proxied, csrf)))
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // The loopback connection is not trusted: its forged https changes neither the cookie nor the scheme the Origin is compared with.
        var forged = new Via(For: ClientTwo, Proto: "https");
        csrf = await CsrfAsync(host, forged);
        using (var sameSite = await LoginAsync(host, forged with { Origin = SameSite(host) }, csrf))
            Assert.Equal(HttpStatusCode.Forbidden, sameSite.StatusCode);
        using (var login = await LoginAsync(host, forged, csrf))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.DoesNotContain("secure", SessionCookie(login));
        }
    }

    /// <summary>AC-033: forwarded remote plain http is refused; the proxy's own header-less request is local.</summary>
    [Fact]
    public async Task Forwarded_remote_plain_http_is_refused_and_the_proxys_own_request_is_local()
    {
        await using var host = await GmTestHost.StartAsync(configuration: Trusting("127.0.0.1"));
        foreach (var via in new[] { new Via(For: "198.51.100.20", Proto: "http"), new Via(For: "198.51.100.20") })
        {
            using (var session = await host.Client.SendAsync(Request(HttpMethod.Get, "/api/gm/v1/auth/session", via)))
            {
                Assert.Equal(HttpStatusCode.Forbidden, session.StatusCode);
                Assert.Equal(HttpsRequired, (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
            }
            using var login = await LoginAsync(host, via, "irrelevant");
            Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
            Assert.Equal(HttpsRequired, (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
        }

        var local = new Via();
        using var accepted = await LoginAsync(host, local, await CsrfAsync(host, local));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Empty(await host.ReadAsync("SELECT operation_id FROM gm_audit WHERE outcome<>'Succeeded';"));
    }

    /// <summary>AC-034: an IPv4-mapped peer matches an IPv4 entry and a mapped entry matches an IPv4 peer.</summary>
    [Fact]
    public async Task A_mapped_peer_matches_an_IPv4_entry()
    {
        await using var host = await GmTestHost.StartAsync(configuration: Trusting("127.0.0.1"), peerOverride: true);
        const string mapped = "::ffff:127.0.0.1";
        var proxied = new Via(mapped, ClientOne, "https");
        using (var login = await LoginAsync(host, proxied, await CsrfAsync(host, proxied)))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.Contains("secure", SessionCookie(login));
        }
        // Counted per forwarded client: were the mapped peer untrusted, both clients would share its one count.
        await AssertLimitedAfterFiveFailuresAsync(host, _ => proxied);
        var other = new Via(mapped, ClientTwo, "https");
        using var notLimited = await LoginAsync(host, other, await CsrfAsync(host, other), "wrong-password");
        Assert.Equal(HttpStatusCode.Unauthorized, notLimited.StatusCode);
    }

    [Fact]
    public async Task A_mapped_entry_matches_an_IPv4_peer()
    {
        await using var host = await GmTestHost.StartAsync(configuration: Trusting("::ffff:" + Proxy), peerOverride: true);
        var proxied = new Via(Proxy, ClientOne, "https");
        using var login = await LoginAsync(host, proxied, await CsrfAsync(host, proxied));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("secure", SessionCookie(login));
    }

    [Fact]
    public async Task A_peer_outside_the_list_stays_a_remote_plain_http_connection()
    {
        await using var host = await GmTestHost.StartAsync(configuration: Trusting(Proxy), peerOverride: true);
        using var session = await host.Client.SendAsync(Request(HttpMethod.Get, "/api/gm/v1/auth/session", new Via("192.0.2.2", "127.0.0.1", "https")));
        Assert.Equal(HttpStatusCode.Forbidden, session.StatusCode);
        Assert.Equal(HttpsRequired, (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
    }
}
