using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using static Dfo.Server.Host.Tests.PlayerPortalTestSupport;

namespace Dfo.Server.Host.Tests;

/// <summary>#329: the administrator's switch, limits and session controls, and their audit.</summary>
public sealed class GmPlayerPortalHttpTests
{
    private const string SettingsPath = "/api/gm/v1/player-portal/settings";

    internal static object Settings(bool enabled = true, bool cera = true, bool mail = true, long revision = 0, long limit = 0) => new
    {
        enabled, ceraEnabled = cera, mailEnabled = mail, revision,
        limits = new { ceraPerGrant = limit, ceraPerDay = limit, tokenCeraPerGrant = limit, tokenCeraPerDay = limit, mailGoldPerMail = limit, mailsPerDay = limit },
    };

    private static async Task<HttpResponseMessage> SaveAsync(GmTestHost host, string csrf, object settings) => await host.PostAsync(SettingsPath, csrf, settings);

    /// <summary>AC-020: defaults, full permission only, and a stale revision is a conflict (R-1: 409, not 500).</summary>
    [Fact]
    public async Task Settings_default_closed_need_full_permission_and_refuse_a_stale_revision()
    {
        await using (var denied = await GmTestHost.StartAsync(permissions: "QueryPlayers,SendMail,GrantCera"))
        {
            var deniedCsrf = await GmCeraHttpTests.Login(denied);
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.Client.GetAsync(SettingsPath)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SaveAsync(denied, deniedCsrf, Settings())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.Client.GetAsync("/api/gm/v1/player-portal/sessions")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.PostAsync("/api/gm/v1/player-portal/sessions/kick-all", deniedCsrf)).StatusCode);
        }
        await using var host = await GmTestHost.StartAsync();
        var csrf = await GmCeraHttpTests.Login(host);
        var initial = await JsonAsync(await host.Client.GetAsync(SettingsPath));
        Assert.False(initial.GetProperty("enabled").GetBoolean());
        Assert.True(initial.GetProperty("ceraEnabled").GetBoolean());
        Assert.True(initial.GetProperty("mailEnabled").GetBoolean());
        Assert.All(initial.GetProperty("limits").EnumerateObject(), limit => Assert.Equal(0, limit.Value.GetInt64()));
        Assert.Equal(0, initial.GetProperty("revision").GetInt64());

        using (var first = await SaveAsync(host, csrf, Settings(limit: 1000)))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var saved = await JsonAsync(first);
            Assert.Equal(1, saved.GetProperty("revision").GetInt64());
            Assert.True(saved.GetProperty("enabled").GetBoolean());
            Assert.Equal(1000, saved.GetProperty("limits").GetProperty("mailsPerDay").GetInt64());
        }
        using (var stale = await SaveAsync(host, csrf, Settings(enabled: false)))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("设置已被其他管理员修改，请刷新后重试。", (await JsonAsync(stale)).GetProperty("message").GetString());
        }
        Assert.True((await JsonAsync(await host.Client.GetAsync(SettingsPath))).GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(host, csrf, Settings(revision: 1))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(host, csrf, Settings(revision: 2, limit: -1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(host, csrf, Settings(revision: 2, limit: 2147483648))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(host, csrf, Settings(revision: 2, limit: 2147483647))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(host, csrf, new { enabled = true, ceraEnabled = true, mailEnabled = true, revision = 3 })).StatusCode);
        var extra = new HttpRequestMessage(HttpMethod.Post, SettingsPath) { Content = new StringContent(
            "{\"enabled\":true,\"ceraEnabled\":true,\"mailEnabled\":true,\"revision\":3,\"limits\":{\"ceraPerGrant\":0,\"ceraPerDay\":0,\"tokenCeraPerGrant\":0,\"tokenCeraPerDay\":0,\"mailGoldPerMail\":0,\"mailsPerDay\":0},\"extra\":1}",
            Encoding.UTF8, "application/json") };
        extra.Headers.Add("X-GM-CSRF", csrf);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.SendAsync(extra)).StatusCode);
        Assert.Equal(["3"], await host.ReadAsync("SELECT revision FROM player_portal_settings;"));
    }

    /// <summary>AC-021: closing ends every player session at once; reopening lets the player sign in again; a feature switch keeps the login.</summary>
    [Fact]
    public async Task Closing_ends_sessions_at_once_and_feature_switches_keep_the_player_signed_in()
    {
        await using var host = await GmTestHost.StartAsync();
        await SeedAccountAsync(host, 1, "alice");
        var csrf = await GmCeraHttpTests.Login(host);
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(host, csrf, Settings())).StatusCode);
        using var player = new PlayerClient(host); await player.SignInAsync("alice");
        Assert.Equal(HttpStatusCode.OK, (await player.Client.GetAsync("/api/player/v1/me")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(host, csrf, Settings(enabled: false, revision: 1))).StatusCode);
        using (var closed = await player.Client.GetAsync("/api/player/v1/me"))
        { Assert.Equal(HttpStatusCode.Forbidden, closed.StatusCode); Assert.Equal("portal_closed", await CodeAsync(closed)); }
        Assert.Equal(["0"], await host.ReadAsync("SELECT COUNT(*) FROM player_portal_sessions;"));

        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(host, csrf, Settings(cera: false, revision: 2))).StatusCode);
        using (var forgotten = await player.Client.GetAsync("/api/player/v1/me"))
        { Assert.Equal(HttpStatusCode.Unauthorized, forgotten.StatusCode); Assert.Equal("unauthenticated", await CodeAsync(forgotten)); }
        await player.SignInAsync("alice");
        using (var me = await player.Client.GetAsync("/api/player/v1/me"))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            var portal = (await JsonAsync(me)).GetProperty("portal");
            Assert.False(portal.GetProperty("ceraEnabled").GetBoolean());
            Assert.True(portal.GetProperty("mailEnabled").GetBoolean());
        }
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(host, csrf, Settings(mail: false, revision: 3))).StatusCode);
        using (var me = await player.Client.GetAsync("/api/player/v1/me"))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            Assert.False((await JsonAsync(me)).GetProperty("portal").GetProperty("mailEnabled").GetBoolean());
        }
    }

    /// <summary>AC-023: live sessions only, kicking one account leaves the others, kicking all ends every session.</summary>
    [Fact]
    public async Task Administrators_list_and_kick_player_sessions()
    {
        await using var host = await GmTestHost.StartAsync();
        await SeedAccountAsync(host, 1, "alice"); await SeedAccountAsync(host, 2, "bob"); await SetPortalAsync(host);
        var csrf = await GmCeraHttpTests.Login(host);
        using var alice = new PlayerClient(host); await alice.SignInAsync("alice");
        using var bob = new PlayerClient(host); await bob.SignInAsync("bob");
        await host.ExecuteAsync("INSERT INTO player_portal_sessions(token_hash,account_id,created_at,expires_at) VALUES('EXPIRED',2,0,1);");

        var sessions = (await JsonAsync(await host.Client.GetAsync("/api/gm/v1/player-portal/sessions"))).EnumerateArray().ToArray();
        Assert.Equal(["alice", "bob"], sessions.Select(s => s.GetProperty("loginName").GetString()).Order());
        Assert.All(sessions, s => Assert.True(s.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow));
        Assert.Equal(["accountId", "loginName", "createdAt", "expiresAt"], sessions[0].EnumerateObject().Select(p => p.Name));

        using (var kick = await host.PostAsync("/api/gm/v1/player-portal/sessions/kick", csrf, new { accountId = 1 }))
        { Assert.Equal(HttpStatusCode.OK, kick.StatusCode); Assert.Equal(1, (await JsonAsync(kick)).GetProperty("kicked").GetInt32()); }
        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.Client.GetAsync("/api/player/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.Client.GetAsync("/api/player/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync("/api/gm/v1/player-portal/sessions/kick", csrf, new { accountId = 0 })).StatusCode);

        using (var all = await host.PostAsync("/api/gm/v1/player-portal/sessions/kick-all", csrf))
        { Assert.Equal(HttpStatusCode.OK, all.StatusCode); Assert.Equal(2, (await JsonAsync(all)).GetProperty("kicked").GetInt32()); }
        Assert.Equal(HttpStatusCode.Unauthorized, (await bob.Client.GetAsync("/api/player/v1/me")).StatusCode);
        Assert.Equal(["0"], await host.ReadAsync("SELECT COUNT(*) FROM player_portal_sessions;"));
    }

    /// <summary>AC-024: every player and administrator act is audited, filterable by actor, and a refused login records no stray name.</summary>
    [Fact]
    public async Task Audit_marks_player_acts_filters_by_actor_and_never_records_a_stray_name()
    {
        await using var host = await GmTestHost.StartAsync();
        await SeedAccountAsync(host, 1, "alice");
        // A record written before the migration, with the old columns only.
        await host.ExecuteAsync("INSERT INTO gm_audit(operation_id,occurred_at,action,administrator_id,administrator_name,target,outcome,reason) VALUES('legacy-record',1767225600000,'GrantCera',1,'operator','账号 1','Succeeded','迁移前');");
        var csrf = await GmCeraHttpTests.Login(host);
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(host, csrf, Settings())).StatusCode);
        const string strayPassword = "Hunter2-secret";
        using (var player = new PlayerClient(host))
        {
            await player.SignInAsync("alice");
            Assert.Equal(HttpStatusCode.NoContent, (await player.PostAsync("/api/player/v1/auth/logout", await player.CsrfAsync())).StatusCode);
        }
        foreach (var (name, password) in new[] { ("alice", strayPassword), ("Pa ss?word", strayPassword), ("no-such-account", strayPassword) })
        {
            using var stranger = new PlayerClient(host);
            Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.LoginAsync(name, password)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("/api/gm/v1/player-portal/sessions/kick", csrf, new { accountId = 1 })).StatusCode);

        var players = await JsonAsync(await host.Client.GetAsync("/api/gm/v1/audit?actor=player&pageSize=100"));
        var items = players.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(5, players.GetProperty("total").GetInt64());
        Assert.All(items, item => Assert.Equal("player", item.GetProperty("actorKind").GetString()));
        Assert.All(items, item => Assert.Equal(JsonValueKind.Null, item.GetProperty("administratorId").ValueKind));
        Assert.Equal(["PlayerLogin:Rejected:-", "PlayerLogin:Rejected:-", "PlayerLogin:Rejected:1", "PlayerLogin:Succeeded:1", "PlayerLogout:Succeeded:1"],
            items.Select(i => $"{i.GetProperty("action").GetString()}:{i.GetProperty("outcome").GetString()}:{(i.GetProperty("accountId").ValueKind == JsonValueKind.Null ? "-" : i.GetProperty("accountId").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture))}").Order(StringComparer.Ordinal));
        Assert.All(items.Where(i => i.GetProperty("accountId").ValueKind != JsonValueKind.Null), i => Assert.Equal("alice", i.GetProperty("administratorName").GetString()));
        Assert.All(items.Where(i => i.GetProperty("accountId").ValueKind == JsonValueKind.Null), i => Assert.Equal("（未记录的登录名）", i.GetProperty("administratorName").GetString()));

        var gm = await JsonAsync(await host.Client.GetAsync("/api/gm/v1/audit?actor=gm&pageSize=100"));
        var gmItems = gm.GetProperty("items").EnumerateArray().ToArray();
        Assert.All(gmItems, item => { Assert.Equal("gm", item.GetProperty("actorKind").GetString()); Assert.Equal(JsonValueKind.Null, item.GetProperty("accountId").ValueKind); });
        Assert.Contains(gmItems, item => item.GetProperty("operationId").GetString() == "legacy-record");
        Assert.Contains(gmItems, item => item.GetProperty("action").GetString() == "ChangePlayerPortal");
        Assert.Contains(gmItems, item => item.GetProperty("action").GetString() == "KickPlayerSessions" && item.GetProperty("target").GetString() == "账号 1");
        Assert.Equal(4, (await JsonAsync(await host.Client.GetAsync("/api/gm/v1/audit?action=PlayerLogin"))).GetProperty("total").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/gm/v1/audit?actor=robot")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/gm/v1/audit?actor=gm&actor=player")).StatusCode);

        var stored = string.Join("\n", await host.ReadAsync("SELECT operation_id||'|'||occurred_at||'|'||action||'|'||COALESCE(administrator_id,'')||'|'||COALESCE(administrator_name,'')||'|'||target||'|'||outcome||'|'||reason||'|'||actor_kind||'|'||COALESCE(account_id,'') FROM gm_audit;"));
        Assert.DoesNotContain("Pa ss?word", stored);
        Assert.DoesNotContain("no-such-account", stored);
        Assert.DoesNotContain(strayPassword, stored);
        Assert.DoesNotContain(Password, stored);
    }
}
