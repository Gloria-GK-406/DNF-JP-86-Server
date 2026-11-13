using System.Net;
using System.Text.Json;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Data.Authentication;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Host.Tests.PlayerPortalTestSupport;

namespace Dfo.Server.Host.Tests;

/// <summary>#329: a player signs in with the game account, sees only the own account, and the switch is judged in the CMD.</summary>
public sealed class PlayerPortalAuthenticationHttpTests
{
    /// <summary>AC-001: login, identical failures for a wrong password and an unknown account, logout.</summary>
    [Fact]
    public async Task A_player_signs_in_and_out_and_every_credential_failure_answers_alike()
    {
        await using var host = await GmTestHost.StartAsync();
        await SeedAccountAsync(host, 1, "alice"); await SetPortalAsync(host);
        using var player = new PlayerClient(host);

        using (var login = await player.LoginAsync("alice"))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var body = await JsonAsync(login);
            Assert.Equal(1, body.GetProperty("accountId").GetInt32());
            Assert.Equal("alice", body.GetProperty("loginName").GetString());
            var cookie = SetCookie(login, SessionCookie)!.ToLowerInvariant();
            Assert.Contains("path=/api/player", cookie); Assert.Contains("httponly", cookie); Assert.Contains("samesite=strict", cookie);
            Assert.DoesNotContain("secure", cookie);
        }
        var bodies = new List<string>();
        foreach (var (name, password) in new[] { ("alice", "wrong-password"), ("no-such-account", Password), ("bad?name", Password) })
        {
            using var stranger = new PlayerClient(host);
            using var failed = await stranger.LoginAsync(name, password);
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
            Assert.Null(SetCookie(failed, SessionCookie));
            bodies.Add(await failed.Content.ReadAsStringAsync());
        }
        Assert.Single(bodies.Distinct());
        Assert.Equal("unauthenticated", JsonDocument.Parse(bodies[0]).RootElement.GetProperty("code").GetString());

        var csrf = await player.CsrfAsync();
        using (var session = await player.Client.GetAsync("/api/player/v1/auth/session"))
            Assert.Equal("alice", (await JsonAsync(session)).GetProperty("loginName").GetString());
        var secret = player.Secret!;
        using (var logout = await player.PostAsync("/api/player/v1/auth/logout", csrf))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            Assert.Contains("path=/api/player", SetCookie(logout, SessionCookie)!.ToLowerInvariant());
        }
        using var raw = RawClient(host);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/player/v1/me");
        request.Headers.Add("Cookie", $"{SessionCookie}={secret}");
        using var after = await raw.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        Assert.Equal("unauthenticated", await CodeAsync(after));
        Assert.Equal(["0"], await host.ReadAsync("SELECT COUNT(*) FROM player_portal_sessions;"));
    }

    /// <summary>AC-002: a closed portal refuses the right password as closed; only the status route answers.</summary>
    [Fact]
    public async Task A_closed_portal_refuses_login_as_closed()
    {
        await using var host = await GmTestHost.StartAsync();
        await SeedAccountAsync(host, 1, "alice");
        using var player = new PlayerClient(host);
        using (var status = await player.Client.GetAsync("/api/player/v1/auth/status"))
        {
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            Assert.False((await JsonAsync(status)).GetProperty("enabled").GetBoolean());
        }
        using (var csrf = await player.Client.GetAsync("/api/player/v1/auth/csrf"))
        { Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode); Assert.Equal("portal_closed", await CodeAsync(csrf)); }
        using (var login = await player.PostAsync("/api/player/v1/auth/login", null, new { loginName = "alice", password = Password }))
        { Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode); Assert.Equal("portal_closed", await CodeAsync(login)); }
        using (var me = await player.Client.GetAsync("/api/player/v1/me"))
        { Assert.Equal(HttpStatusCode.Forbidden, me.StatusCode); Assert.Equal("portal_closed", await CodeAsync(me)); }
        Assert.Equal(["0"], await host.ReadAsync("SELECT COUNT(*) FROM player_portal_sessions;"));
    }

    /// <summary>AC-004: the account is the session's; another account's identifier in the request changes nothing.</summary>
    [Fact]
    public async Task A_player_reads_only_the_own_account_and_live_characters()
    {
        await using var host = await GmTestHost.StartAsync();
        await SeedAccountAsync(host, 1, "alice", cera: 1000, tokenCera: 2000); await SeedAccountAsync(host, 2, "bob", cera: 9, tokenCera: 9);
        await SeedCharacterAsync(host, 11, 1, "甲"); await SeedCharacterAsync(host, 12, 1, "乙", deleted: true); await SeedCharacterAsync(host, 21, 2, "丙");
        await SetPortalAsync(host);
        using var player = new PlayerClient(host); await player.SignInAsync("alice");
        foreach (var path in new[] { "/api/player/v1/me", "/api/player/v1/me?accountId=2", "/api/player/v1/me?account=bob" })
        {
            using var me = await player.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            var body = await JsonAsync(me);
            Assert.Equal(["accountId", "loginName", "balances", "characters", "portal"], body.EnumerateObject().Select(p => p.Name));
            Assert.Equal(1, body.GetProperty("accountId").GetInt32());
            Assert.Equal("alice", body.GetProperty("loginName").GetString());
            Assert.Equal(["cera", "tokenCera"], body.GetProperty("balances").EnumerateObject().Select(p => p.Name));
            Assert.Equal(1000, body.GetProperty("balances").GetProperty("cera").GetInt64());
            Assert.Equal(2000, body.GetProperty("balances").GetProperty("tokenCera").GetInt64());
            var character = Assert.Single(body.GetProperty("characters").EnumerateArray());
            Assert.Equal(["characterId", "name", "job", "level", "isOnline"], character.EnumerateObject().Select(p => p.Name));
            Assert.Equal(11, character.GetProperty("characterId").GetInt32());
            Assert.Equal("甲", character.GetProperty("name").GetString());
            Assert.False(character.GetProperty("isOnline").GetBoolean());
            var portal = body.GetProperty("portal");
            Assert.True(portal.GetProperty("ceraEnabled").GetBoolean());
            Assert.Equal(0, portal.GetProperty("today").GetProperty("cera").GetInt64());
        }
    }

    /// <summary>AC-006 and RQ-005: eight hours, never extended by use.</summary>
    [Fact]
    public async Task A_session_expires_eight_hours_after_login_however_it_is_used()
    {
        var clock = new GmTestClock();
        await using var host = await GmTestHost.StartAsync(clock: clock);
        await SeedAccountAsync(host, 1, "alice"); await SetPortalAsync(host);
        using var player = new PlayerClient(host); await player.SignInAsync("alice");
        clock.Advance(TimeSpan.FromHours(7));
        using (var used = await player.Client.GetAsync("/api/player/v1/me")) Assert.Equal(HttpStatusCode.OK, used.StatusCode);
        clock.Advance(TimeSpan.FromMinutes(61));
        using var expired = await player.Client.GetAsync("/api/player/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal("unauthenticated", await CodeAsync(expired));
    }

    /// <summary>AC-008(b): the portal closed after the password check and before the login CMD leaves no session.</summary>
    [Fact]
    public async Task Closing_between_the_password_check_and_the_login_cmd_leaves_no_session()
    {
        var gate = new CredentialGate();
        await using var host = await GmTestHost.StartAsync(configureServices: services => gate.Wrap(services));
        await SeedAccountAsync(host, 1, "alice"); await SetPortalAsync(host);
        var gmCsrf = await GmCeraHttpTests.Login(host);
        using var player = new PlayerClient(host);
        var csrf = await player.CsrfAsync();
        gate.Armed = true;
        var pending = player.LoginAsync("alice", csrf: csrf);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using (var close = await host.PostAsync("/api/gm/v1/player-portal/settings", gmCsrf, GmPlayerPortalHttpTests.Settings(enabled: false, revision: 1)))
            Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        gate.Release.SetResult();
        using var login = await pending;
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Equal("portal_closed", await CodeAsync(login));
        Assert.Null(SetCookie(login, SessionCookie));
        Assert.Equal(["0"], await host.ReadAsync("SELECT COUNT(*) FROM player_portal_sessions;"));
        Assert.Equal(["0"], await host.ReadAsync("SELECT COUNT(*) FROM gm_audit WHERE action='PlayerLogin';"));
    }

    /// <summary>A record changed after the check is checked once more (as the launcher does); still wrong, one refusal is audited.</summary>
    [Fact]
    public async Task A_record_changed_after_the_check_is_checked_again_and_then_refused()
    {
        var gate = new CredentialGate();
        await using var host = await GmTestHost.StartAsync(configureServices: services => gate.Wrap(services));
        await SeedAccountAsync(host, 1, "alice"); await SetPortalAsync(host);
        var replacement = host.Services.GetRequiredService<IPasswordCredentialRuleService>().Hash("another-password");
        using var player = new PlayerClient(host);
        var csrf = await player.CsrfAsync();
        gate.Armed = true;
        var pending = player.LoginAsync("alice", csrf: csrf);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await host.ExecuteAsync($"UPDATE accounts SET password_hash='{replacement}' WHERE account_id=1;");
        gate.Release.SetResult();
        using var login = await pending;
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal(2, gate.Calls);
        Assert.Equal(["Rejected:1"], await host.ReadAsync("SELECT outcome||':'||account_id FROM gm_audit WHERE action='PlayerLogin';"));
        Assert.Equal(["0"], await host.ReadAsync("SELECT COUNT(*) FROM player_portal_sessions;"));
    }

    /// <summary>The launcher login owns rehashing and legacy migration; a player login only reads the record.</summary>
    [Fact]
    public async Task A_player_login_never_rewrites_the_password_record()
    {
        await using var host = await GmTestHost.StartAsync(configuration: new Dictionary<string, string?> { ["LauncherAuth:LegacyMd5Migration:AllowedAccountIds:0"] = "1" });
        var legacy = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(Password))).ToLowerInvariant();
        await host.ExecuteAsync($"INSERT INTO accounts(account_id,m_id,password_hash) VALUES(1,'legacy','{legacy}');");
        await SetPortalAsync(host);
        using var player = new PlayerClient(host);
        using var login = await player.LoginAsync("legacy");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal([legacy], await host.ReadAsync("SELECT password_hash FROM accounts WHERE account_id=1;"));
    }

    internal sealed class CredentialGate
    {
        internal bool Armed { get; set; }
        internal int Calls;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Wrap(IServiceCollection services)
        {
            var original = services.Last(descriptor => descriptor.ServiceType == typeof(ILauncherCredentialReader));
            services.AddSingleton<ILauncherCredentialReader>(provider =>
                new Gated((ILauncherCredentialReader)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!), this));
        }
        private sealed class Gated(ILauncherCredentialReader inner, CredentialGate gate) : ILauncherCredentialReader
        {
            public ValueTask<RegistrationPreparation> PrepareRegistrationAsync(string loginId, string password, CancellationToken token) =>
                inner.PrepareRegistrationAsync(loginId, password, token);
            public async ValueTask<LoginVerification> VerifyLoginAsync(string loginId, string password, CancellationToken token)
            {
                var result = await inner.VerifyLoginAsync(loginId, password, token);
                if (gate.Armed && Interlocked.Increment(ref gate.Calls) == 1) { gate.Entered.SetResult(); await gate.Release.Task; }
                return result;
            }
        }
    }
}
