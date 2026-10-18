using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Dfo.Server.Host.Tests;

public sealed class GmAuthenticationHttpTests
{
    [Fact]
    public async Task UnconfiguredAdministratorKeepsPrivateApiClosedWithoutPreventingStartup()
    {
        await using var host = await GmTestHost.StartAsync(null);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/gm/v1/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/gm/v1/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/gm/v1/unregistered-private-endpoint")).StatusCode);
        Assert.False((await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/auth/status")).GetProperty("initialized").GetBoolean());
    }
    [Fact]
    public async Task LoginRequiresActualCsrfAndSessionLogoutRevokesCapturedCookie()
    {
        await using var host = await GmTestHost.StartAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await host.LoginAsync("")).StatusCode);
        var csrf = await host.CsrfAsync(); Assert.Equal(HttpStatusCode.Forbidden, (await host.LoginAsync("incorrect-token")).StatusCode);
        using var login = await host.LoginAsync(csrf); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("Dfo.Gm.Session=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie.ToLowerInvariant()); Assert.Contains("samesite=strict", cookie.ToLowerInvariant());
        Assert.DoesNotContain("sessionSecret", await login.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/gm/v1/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/api/gm/v1/unregistered-private-endpoint")).StatusCode);
        csrf = await host.CsrfAsync(); Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync("/api/gm/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.PostAsync("/api/gm/v1/auth/logout", csrf)).StatusCode);
        using var replay = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = host.Client.BaseAddress };
        replay.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]); Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/gm/v1/audit")).StatusCode);
        var events = await host.ReadAsync("SELECT action || ':' || outcome FROM gm_audit;"); Assert.Contains("Login:Succeeded", events); Assert.Contains("Logout:Succeeded", events);
    }
    [Fact]
    public async Task CrossBrowserTokensAndCrossSiteOriginCannotCreateOrRevokeSession()
    {
        await using var host = await GmTestHost.StartAsync(); var csrf = await host.CsrfAsync();
        using var other = new HttpClient { BaseAddress = host.Client.BaseAddress };
        var otherToken = (await other.GetFromJsonAsync<JsonElement>("/api/gm/v1/auth/csrf")).GetProperty("token").GetString()!;
        Assert.Equal(HttpStatusCode.Forbidden, (await host.LoginAsync(otherToken)).StatusCode);
        host.Client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example"); Assert.Equal(HttpStatusCode.Forbidden, (await host.LoginAsync(csrf)).StatusCode);
        host.Client.DefaultRequestHeaders.Remove("Origin"); Assert.Equal(HttpStatusCode.OK, (await host.LoginAsync(csrf)).StatusCode);
        csrf = await host.CsrfAsync(); host.Client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync("/api/gm/v1/auth/logout", csrf)).StatusCode);
        host.Client.DefaultRequestHeaders.Remove("Sec-Fetch-Site"); Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/gm/v1/auth/session")).StatusCode);
    }
    [Fact]
    public async Task PlayerTicketCannotReadAuditAndPermissionsUseCurrentStorage()
    {
        await using var host = await GmTestHost.StartAsync(permissions: "QueryPlayers");
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", "player-login-ticket");
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/gm/v1/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.LoginAsync(await host.CsrfAsync())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/api/gm/v1/audit")).StatusCode);
        await host.ExecuteAsync("UPDATE gm_administrators SET permissions=8;"); Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/gm/v1/audit")).StatusCode);
        await host.ExecuteAsync("UPDATE gm_administrators SET permissions=1;"); Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/api/gm/v1/audit")).StatusCode);
        await host.ExecuteAsync("UPDATE gm_administrators SET enabled=0;"); Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/gm/v1/auth/session")).StatusCode);
    }
    [Fact]
    public async Task SessionSurvivesRestartWithoutBootstrapOverwritingIdentity()
    {
        var directory = GmTestHost.NewDirectory(); var cookies = new CookieContainer();
        await using (var first = await GmTestHost.StartAsync(directory: directory, cookies: cookies))
        { Assert.Equal(HttpStatusCode.OK, (await first.LoginAsync(await first.CsrfAsync())).StatusCode); await first.ExecuteAsync("UPDATE gm_administrators SET permissions=1;"); }
        await using var restarted = await GmTestHost.StartAsync(password: "different-bootstrap-secret", directory: directory, cookies: cookies);
        Assert.Equal(HttpStatusCode.OK, (await restarted.Client.GetAsync("/api/gm/v1/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await restarted.Client.GetAsync("/api/gm/v1/audit")).StatusCode);
        using var anonymous = new HttpClient { BaseAddress = restarted.Client.BaseAddress };
        var csrf = (await anonymous.GetFromJsonAsync<JsonElement>("/api/gm/v1/auth/csrf")).GetProperty("token").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/gm/v1/auth/login") { Content = JsonContent.Create(new { loginName = "operator", password = GmTestHost.Password }) }; request.Headers.Add("X-GM-CSRF", csrf);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.SendAsync(request)).StatusCode); Assert.Single(await restarted.ReadAsync("SELECT id FROM gm_administrators;"));
    }
    [Fact]
    public async Task MissingBootstrapFieldAndExpiredSessionAreRejected()
    {
        await using (var missing = await GmTestHost.StartAsync(password: "")) Assert.Empty(await missing.ReadAsync("SELECT id FROM gm_administrators;"));
        var clock = new GmTestClock(); await using var host = await GmTestHost.StartAsync(clock: clock);
        Assert.Equal(HttpStatusCode.OK, (await host.LoginAsync(await host.CsrfAsync())).StatusCode); clock.Advance(TimeSpan.FromHours(8));
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/gm/v1/auth/session")).StatusCode);
    }
    [Fact]
    public async Task FailuresAreAuditedAndLimitedWithoutRevokingExistingSession()
    {
        var clock = new GmTestClock(); await using var host = await GmTestHost.StartAsync(clock: clock);
        Assert.Equal(HttpStatusCode.OK, (await host.LoginAsync(await host.CsrfAsync())).StatusCode); var csrf = await host.CsrfAsync();
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await host.LoginAsync(csrf, password: "wrong-password")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.LoginAsync(csrf, password: "wrong-password")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/gm/v1/auth/session")).StatusCode);
        Assert.Equal(5, (await host.ReadAsync("SELECT operation_id FROM gm_audit WHERE outcome='Rejected';")).Count);
        clock.Advance(TimeSpan.FromMinutes(15));
        for (var i = 0; i < 20; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await host.LoginAsync(csrf, loginName: "unknown-" + i, password: "wrong-password")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.LoginAsync(csrf, loginName: "another-user")).StatusCode);
    }
    [Fact]
    public async Task AuditFiltersByOutcomeAndActionInsideTheQuery()
    {
        await using var host = await GmTestHost.StartAsync(); var csrf = await host.CsrfAsync();
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await host.LoginAsync(csrf, password: "wrong-password")).StatusCode);
        using var login = await host.LoginAsync(csrf);
        async Task<JsonElement> Page(string query) => await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/audit?" + query);
        var attention = await Page("outcome=attention&pageSize=1");
        Assert.Equal(2, attention.GetProperty("total").GetInt32()); Assert.Equal("Rejected", attention.GetProperty("items")[0].GetProperty("outcome").GetString());
        Assert.Equal(1, (await Page("outcome=Succeeded")).GetProperty("total").GetInt32());
        Assert.Equal(2, (await Page("outcome=Rejected&action=Login")).GetProperty("total").GetInt32());
        Assert.Equal(3, (await Page("action=Login")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await Page("action=SendOfficialMail&outcome=attention")).GetProperty("total").GetInt32());
        foreach (var query in new[] { "outcome=Failed", "outcome=succeeded", "action=DropTable", "outcome=Rejected&outcome=Unknown", "action=Login&action=Logout" })
        {
            var response = await host.Client.GetAsync("/api/gm/v1/audit?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(string.IsNullOrEmpty((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()));
        }
    }
    [Fact]
    public async Task AuditPaginationAndStoredRecordsContainNoCredentials()
    {
        await using var host = await GmTestHost.StartAsync(); using var login = await host.LoginAsync(await host.CsrfAsync());
        var page = await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/audit?page=1&pageSize=1");
        Assert.Equal(1, page.GetProperty("items").GetArrayLength()); Assert.Equal(1, page.GetProperty("total").GetInt32());
        foreach (var query in new[] { "page=0", "pageSize=101", "page=text" }) Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/gm/v1/audit?" + query)).StatusCode);
        var hash = Assert.Single(await host.ReadAsync("SELECT token_hash FROM gm_sessions;"));
        var raw = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("Dfo.Gm.Session=", StringComparison.Ordinal)).Split(';')[0].Split('=')[1];
        Assert.DoesNotContain(raw, await login.Content.ReadAsStringAsync());
        Assert.DoesNotContain(GmTestHost.Password, await login.Content.ReadAsStringAsync());
        Assert.NotEqual(raw, hash); Assert.DoesNotContain(raw, page.ToString()); Assert.DoesNotContain(hash, page.ToString());
        Assert.DoesNotContain(GmTestHost.Password, string.Join('|', await host.ReadAsync("SELECT password_record FROM gm_administrators;")));
        Assert.DoesNotContain(GmTestHost.Password, string.Join('|', host.Logs.Messages)); Assert.DoesNotContain(raw, string.Join('|', host.Logs.Messages));
    }
    [Fact]
    public async Task RemoteHttpRejectsSpoofedForwardingHeaders()
    {
        await using var host = await GmTestHost.StartAsync(remoteConnection: true); host.Client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1"); host.Client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        foreach (var path in new[] { "auth/csrf", "auth/session", "audit" }) Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/api/gm/v1/" + path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.LoginAsync("irrelevant")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync("/api/gm/v1/auth/logout", null)).StatusCode);
        Assert.Empty(await host.ReadAsync("SELECT token_hash FROM gm_sessions;"));
    }
    [Fact]
    public async Task RealHttpsIssuesSecureCookie()
    {
        await using var host = await GmTestHost.StartAsync(https: true, remoteConnection: true); using var response = await host.LoginAsync(await host.CsrfAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Contains("secure", response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("Dfo.Gm.Session=", StringComparison.Ordinal)).ToLowerInvariant());
    }
    [Fact]
    public async Task AuditFailureRollsBackLoginAndLogoutInSameTransaction()
    {
        var fault = new GmAuditFault(); await using var host = await GmTestHost.StartAsync(fault: fault); fault.Throw = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await host.LoginAsync(await host.CsrfAsync())).StatusCode); Assert.Empty(await host.ReadAsync("SELECT token_hash FROM gm_sessions;")); Assert.Empty(await host.ReadAsync("SELECT operation_id FROM gm_audit;"));
        fault.Throw = false; await host.LoginAsync(await host.CsrfAsync()); fault.Throw = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await host.PostAsync("/api/gm/v1/auth/logout", await host.CsrfAsync())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/gm/v1/auth/session")).StatusCode); Assert.Single(await host.ReadAsync("SELECT token_hash FROM gm_sessions;")); Assert.Single(await host.ReadAsync("SELECT operation_id FROM gm_audit;"));
    }
    [Fact]
    public async Task IdenticalPasswordsInIndependentDatabasesHaveDifferentSaltedRecords()
    {
        await using var first = await GmTestHost.StartAsync(); var firstRecord = Assert.Single(await first.ReadAsync("SELECT password_record FROM gm_administrators;"));
        await using var second = await GmTestHost.StartAsync(); var secondRecord = Assert.Single(await second.ReadAsync("SELECT password_record FROM gm_administrators;"));
        Assert.NotEqual(firstRecord, secondRecord);
        Assert.Equal(HttpStatusCode.OK, (await first.LoginAsync(await first.CsrfAsync())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.LoginAsync(await second.CsrfAsync())).StatusCode);
    }
}
