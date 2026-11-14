using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dfo.Server.Abstractions.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

/// <summary>Accounts with real launcher passwords, the portal switch, and a player's own browser (cookie jar).</summary>
internal static class PlayerPortalTestSupport
{
    internal const string Password = "player-test-password";
    internal const string SessionCookie = "Dfo.Player.Session";

    internal static Task SeedAccountAsync(GmTestHost host, int id, string name, string password = Password, int cera = 10, int tokenCera = 20)
    {
        var record = host.Services.GetRequiredService<IPasswordCredentialRuleService>().Hash(password);
        return host.ExecuteAsync($"INSERT INTO accounts(account_id,m_id,password_hash,cera,token_cera,happy_token_cera) VALUES({id},'{name}','{record}',{cera},{tokenCera},30);");
    }

    internal static Task SeedCharacterAsync(GmTestHost host, int id, int accountId, string name, bool deleted = false, int level = 50) =>
        host.ExecuteAsync($"INSERT INTO characters(character_id,account_id,name,name_bytes,job,grow_type,level,delete_flag,slot_index) VALUES({id},{accountId},'n',X'{Convert.ToHexString(Encoding.UTF8.GetBytes(name))}',3,1,{level},{(deleted ? 1 : 0)},{id % 8});");

    /// <summary>Writes the settings row directly: the boundary and every CMD read it per request.</summary>
    internal static Task SetPortalAsync(GmTestHost host, bool enabled = true, bool cera = true, bool mail = true) =>
        host.ExecuteAsync($"INSERT INTO player_portal_settings(id,enabled,cera_enabled,mail_enabled,cera_per_grant,cera_per_day,token_cera_per_grant,token_cera_per_day,mail_gold_per_mail,mails_per_day,revision) VALUES(1,{B(enabled)},{B(cera)},{B(mail)},0,0,0,0,0,0,1) ON CONFLICT(id) DO UPDATE SET enabled=excluded.enabled,cera_enabled=excluded.cera_enabled,mail_enabled=excluded.mail_enabled,revision=revision+1;");
    private static int B(bool value) => value ? 1 : 0;

    /// <summary>Sets the cera limits on the settings row <see cref="SetPortalAsync"/> wrote.</summary>
    internal static Task SetCeraLimitsAsync(GmTestHost host, long ceraPerGrant = 0, long ceraPerDay = 0, long tokenCeraPerGrant = 0, long tokenCeraPerDay = 0) =>
        host.ExecuteAsync($"UPDATE player_portal_settings SET cera_per_grant={ceraPerGrant},cera_per_day={ceraPerDay},token_cera_per_grant={tokenCeraPerGrant},token_cera_per_day={tokenCeraPerDay},revision=revision+1;");

    /// <summary>Sets the mail limits on the settings row <see cref="SetPortalAsync"/> wrote.</summary>
    internal static Task SetMailLimitsAsync(GmTestHost host, long mailGoldPerMail = 0, long mailsPerDay = 0) =>
        host.ExecuteAsync($"UPDATE player_portal_settings SET mail_gold_per_mail={mailGoldPerMail},mails_per_day={mailsPerDay},revision=revision+1;");

    /// <summary>Mailbox messages, mail attachments and created items, as <c>messages:attachments:items</c>.</summary>
    internal static async Task<string> MailboxAsync(GmTestHost host) => Assert.Single(await host.ReadAsync(
        "SELECT (SELECT COUNT(*) FROM mailbox_messages)||':'||(SELECT COUNT(*) FROM mailbox_attachments)||':'||(SELECT COUNT(*) FROM items);"));

    /// <summary>The account's cera and token cera, as <c>cera:token</c>.</summary>
    internal static async Task<string> BalanceAsync(GmTestHost host, int accountId) =>
        Assert.Single(await host.ReadAsync($"SELECT cera||':'||token_cera FROM accounts WHERE account_id={accountId};"));

    internal static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();
    internal static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;

    internal static string? SetCookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.FirstOrDefault(value => value.StartsWith(name + "=", StringComparison.Ordinal)) : null;
    internal static string CookieValue(string setCookie) => setCookie[(setCookie.IndexOf('=') + 1)..setCookie.IndexOf(';')];

    /// <summary>A browser of its own: the player's cookies never mix with the administrator's client.</summary>
    internal sealed class PlayerClient : IDisposable
    {
        internal PlayerClient(GmTestHost host)
        {
            Cookies = new CookieContainer();
            Client = new HttpClient(new HttpClientHandler { CookieContainer = Cookies }) { BaseAddress = host.Address };
        }
        internal CookieContainer Cookies { get; }
        internal HttpClient Client { get; }

        internal async Task<string> CsrfAsync()
        {
            using var response = await Client.GetAsync("/api/player/v1/auth/csrf");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await JsonAsync(response)).GetProperty("token").GetString()!;
        }
        internal async Task<HttpResponseMessage> LoginAsync(string name, string password = Password, string? csrf = null) =>
            await PostAsync("/api/player/v1/auth/login", csrf ?? await CsrfAsync(), new { loginName = name, password });
        /// <summary>Signs in and returns a token bound to the new identity.</summary>
        internal async Task<string> SignInAsync(string name, string password = Password)
        {
            using var response = await LoginAsync(name, password);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await CsrfAsync();
        }
        internal Task<HttpResponseMessage> PostAsync(string path, string? csrf, object? payload = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload ?? new { }) };
            if (csrf is not null) request.Headers.Add("X-GM-CSRF", csrf);
            return Client.SendAsync(request);
        }
        internal Task<HttpResponseMessage> GrantAsync(string csrf, string currency, int amount, Guid? id = null) =>
            PostAsync("/api/player/v1/cera/grant", csrf, new { requestId = id ?? Guid.NewGuid(), currency, amount });
        /// <summary>A player mail; <paramref name="items"/> are sent as given, so a test can add undeclared fields.</summary>
        internal Task<HttpResponseMessage> SendMailAsync(string csrf, int characterId, string title = "", string body = "测试",
            object[]? items = null, int gold = 0, Guid? id = null) =>
            PostAsync("/api/player/v1/mail/send", csrf, new { requestId = id ?? Guid.NewGuid(), characterId, title, body, items = items ?? [], gold });
        internal string? Secret => Cookies.GetCookies(new Uri(Client.BaseAddress!, "/api/player/")).FirstOrDefault(c => c.Name == SessionCookie)?.Value;
        public void Dispose() => Client.Dispose();
    }

    /// <summary>A client that sends exactly the cookies a test writes, for credentials placed under the other prefix's name.</summary>
    internal static HttpClient RawClient(GmTestHost host) => new(new HttpClientHandler { UseCookies = false }) { BaseAddress = host.Address };
}
