using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Hosting;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Login;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

public sealed class GmPlayerDirectoryHttpTests
{
    private static readonly string[] PrivatePaths = ["overview", "accounts", "accounts/45", "characters", "characters/1"];

    [Fact]
    public async Task EveryQueryUsesCurrentPermissionAndRejectsPlayerTicketsExpiredSessionsAndRemotePlaintext()
    {
        var clock = new GmTestClock();
        await using var host = await GmTestHost.StartAsync(clock: clock);
        await SeedAsync(host);
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", "player-ticket-secret");
        foreach (var path in PrivatePaths) Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/gm/v1/" + path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.LoginAsync(await host.CsrfAsync())).StatusCode);
        foreach (var path in PrivatePaths) Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/gm/v1/" + path)).StatusCode);
        await host.ExecuteAsync("UPDATE gm_administrators SET permissions=8;");
        foreach (var path in PrivatePaths) Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/api/gm/v1/" + path)).StatusCode);
        await host.ExecuteAsync("UPDATE gm_administrators SET permissions=1;");
        clock.Advance(TimeSpan.FromHours(9));
        foreach (var path in PrivatePaths) Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/gm/v1/" + path)).StatusCode);
        await using var remote = await GmTestHost.StartAsync(remoteConnection: true);
        foreach (var path in PrivatePaths) Assert.Equal(HttpStatusCode.Forbidden, (await remote.Client.GetAsync("/api/gm/v1/" + path)).StatusCode);
    }

    [Fact]
    public async Task DirectoryPagesAllPlayersAndShowsAccountBalancesWithoutSecrets()
    {
        await using var host = await GmTestHost.StartAsync();
        await SeedAsync(host);
        Assert.Equal(HttpStatusCode.OK, (await host.LoginAsync(await host.CsrfAsync())).StatusCode);
        using var response = await host.Client.GetAsync("/api/gm/v1/accounts?page=2&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var text = await response.Content.ReadAsStringAsync();
        var page = JsonSerializer.Deserialize<JsonElement>(text);
        Assert.Equal(45, page.GetProperty("total").GetInt32());
        Assert.Equal(21, page.GetProperty("items")[0].GetProperty("accountId").GetInt32());
        var account = await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/accounts/45");
        Assert.Equal(0, account.GetProperty("characterCount").GetInt32());
        Assert.Equal(int.MaxValue, account.GetProperty("balances").GetProperty("cera").GetInt32());
        foreach (var forbidden in new[] { "password", "connection", "tokenHash", "lastLoginIp", "secret-player" })
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NamesAreStrictAuthoritativeLiteralAndDetailsRemainReadOnly()
    {
        await using var host = await GmTestHost.StartAsync();
        await SeedAsync(host);
        Assert.Equal(HttpStatusCode.OK, (await host.LoginAsync(await host.CsrfAsync())).StatusCode);
        var before = await PlayerFactsAsync(host);
        foreach (var term in new[] { "中文", "%", "_", "'", "<b>" })
        {
            var page = await GetAsync(host, "characters?q=" + Uri.EscapeDataString(term));
            Assert.Equal(1, page.GetProperty("total").GetInt32());
            Assert.Equal(46, page.GetProperty("items")[0].GetProperty("characterId").GetInt32());
        }
        Assert.Equal(0, (await GetAsync(host, "characters?q=wrong-name")).GetProperty("total").GetInt32());
        Assert.Equal("角色01", (await GetAsync(host, "characters/1")).GetProperty("name").GetString());
        Assert.Equal("missing", (await GetAsync(host, "characters/47")).GetProperty("nameDecodeStatus").GetString());
        var invalid = await GetAsync(host, "characters/48");
        Assert.Equal("invalid", invalid.GetProperty("nameDecodeStatus").GetString());
        Assert.Equal("FFFE", invalid.GetProperty("nameBytesHex").GetString());
        Assert.True((await GetAsync(host, "characters/46")).GetProperty("isDeleted").GetBoolean());
        Assert.Equal(86, (await GetAsync(host, "characters/1")).GetProperty("level").GetInt32());
        Assert.Equal(4, (await GetAsync(host, "accounts/1")).GetProperty("characterCount").GetInt32());
        var all = await GetAsync(host, "characters?pageSize=100");
        Assert.Equal(47, all.GetProperty("total").GetInt32());
        Assert.False(all.GetProperty("items")[0].GetProperty("isOnline").GetBoolean());
        Assert.Equal("saved", all.GetProperty("items")[0].GetProperty("position").GetProperty("source").GetString());
        foreach (var key in new[] { "passwordHash", "connectionId", "lastLoginIp", "sessionSecret", "tokenHash", "remoteAddress", "nameBytes", "savedPosition" })
            Assert.DoesNotContain('"' + key + '"', all.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/api/gm/v1/accounts/2147483647")).StatusCode);
        Assert.Equal(before, await PlayerFactsAsync(host));
    }

    [Fact]
    public async Task FiltersPagesBoundariesAndFailureDoNotFabricateFacts()
    {
        await using var host = await GmTestHost.StartAsync();
        Assert.Equal(HttpStatusCode.OK, (await host.LoginAsync(await host.CsrfAsync())).StatusCode);
        Assert.Equal(0, (await GetAsync(host, "accounts")).GetProperty("total").GetInt32());
        await SeedAsync(host);
        var before = await PlayerFactsAsync(host);
        foreach (var query in new[] { "page=0", "page=-1", "page=1000001", "page=x", "pageSize=0", "pageSize=101", "pageSize=2147483648", "accountId=0", "accountId=2147483648", "onlineOnly=1", "onlineOnly=TRUE", "page=1&page=2", "q=a&q=b", "onlineOnly=true&onlineOnly=false", "q=" + new string('a', 129) })
            Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/gm/v1/accounts?" + query)).StatusCode);
        foreach (var path in new[] { "accounts/0", "accounts/-1", "accounts/x", "characters/2147483648", "characters?characterId=x", "characters?characterId=1&characterId=2" })
            Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/gm/v1/" + path)).StatusCode);
        foreach (var path in new[] { "accounts/999", "characters/999" })
            Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/api/gm/v1/" + path)).StatusCode);
        var last = await GetAsync(host, "accounts?page=1000000&pageSize=100&q=%20%20&ignored=anything");
        Assert.Equal(45, last.GetProperty("total").GetInt32()); Assert.Equal(0, last.GetProperty("items").GetArrayLength());
        var first = await GetAsync(host, "characters?pageSize=20"); var second = await GetAsync(host, "characters?page=2&pageSize=20");
        var repeated = await GetAsync(host, "characters?pageSize=20");
        // Projection timestamps differ between requests; compare stable identities rather than incidental sampling time.
        var firstIds = first.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("characterId").GetInt32()).ToArray();
        var secondIds = second.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("characterId").GetInt32()).ToArray();
        Assert.Equal(firstIds, repeated.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("characterId").GetInt32()));
        Assert.Equal(Enumerable.Range(1, 20), firstIds); Assert.Equal(Enumerable.Range(21, 20), secondIds);
        Assert.Equal(0, (await GetAsync(host, "characters?accountId=2&characterId=1&q=角色")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await GetAsync(host, "accounts?accountId=2&q=player02")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await GetAsync(host, "accounts?q=PLAYER")).GetProperty("total").GetInt32());
        Assert.Equal(before, await PlayerFactsAsync(host));
        await host.ExecuteAsync("UPDATE accounts SET cera=-1 WHERE account_id=1;");
        var failedBefore = await PlayerFactsAsync(host);
        using var failure = await host.Client.GetAsync("/api/gm/v1/accounts/1");
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        var error = await failure.Content.ReadAsStringAsync(); Assert.Contains("message", error); Assert.DoesNotContain("SELECT", error); Assert.DoesNotContain("Sqlite", error);
        Assert.Equal(failedBefore, await PlayerFactsAsync(host));
    }

    [Fact]
    public async Task RealRuntimeLifecycleCountsBeforePaginationAndUsesCurrentPositionChannelAndPublication()
    {
        var readiness = new DirectoryReadiness();
        await using var host = await GmTestHost.StartAsync(configuration: ChannelConfig,
            configureServices: services =>
            {
                services.AddSingleton(new ChannelListenerOptions([21011, 21012], false)); services.AddSingleton<IGameServerReadiness>(readiness);
                // Pause automatic supersession cleanup only in this deterministic lifecycle fixture.
                foreach (var descriptor in services.Where(item => item.ImplementationType?.Name == "LoginSessionMaintenanceService").ToArray()) services.Remove(descriptor);
            });
        await SeedAsync(host); Assert.Equal(HttpStatusCode.OK, (await host.LoginAsync(await host.CsrfAsync())).StatusCode);
        var online = host.Services.GetRequiredService<IOnlineUserDomainService>();
        var anonymous = online.RegisterConnection(new(new("anonymous-secret"), NetworkEndpointKind.Game, 21011, IPAddress.Parse("192.0.2.22")));
        Assert.Equal(0, (await GetAsync(host, "overview")).GetProperty("onlineAccounts").GetInt32());
        var user40 = await ConnectAsync(host, 40, 21011);
        Assert.Equal(1, (await GetAsync(host, "accounts?onlineOnly=true&pageSize=1")).GetProperty("total").GetInt32());
        Assert.True((await GetAsync(host, "accounts/40")).GetProperty("isOnline").GetBoolean());
        Assert.Equal(0, (await GetAsync(host, "overview")).GetProperty("onlineCharacters").GetInt32());
        var prepared = SelectedSessionIdentity.Create(new(40), new(40), user40.Connection.ConnectionId, user40.Generation, 21011);
        var selected = host.Services.GetRequiredService<ISelectedSessionDomainService>(); selected.Select(prepared);
        Assert.False((await GetAsync(host, "characters/40")).GetProperty("isOnline").GetBoolean());
        host.Services.GetRequiredService<IActiveCharacterDomainService>().Publish(prepared);
        host.Services.GetRequiredService<ISelectedSessionRuntimeDirectory>().Publish(prepared, true);
        Assert.True(online.GetRequired(user40.OnlineUserId).Generation > prepared.ConnectionGeneration);
        var unplaced = await GetAsync(host, "characters/40"); Assert.True(unplaced.GetProperty("isOnline").GetBoolean());
        Assert.Equal("unavailable", unplaced.GetProperty("position").GetProperty("source").GetString());
        var positions = host.Services.GetRequiredService<ICharacterPositionDomainService>(); positions.Place(prepared, new(3, 4, 100, 200, 2));
        positions.StepTo(prepared, 123, 234, 1); positions.CrossInto(prepared, 5, 6, 321, 432);
        var moved = await GetAsync(host, "characters/40");
        Assert.Equal("current", moved.GetProperty("position").GetProperty("source").GetString());
        Assert.Equal(321, moved.GetProperty("position").GetProperty("x").GetInt32()); Assert.Equal(6, moved.GetProperty("position").GetProperty("areaId").GetInt32());
        Assert.Equal(11, moved.GetProperty("channel").GetProperty("channelId").GetInt32());
        var duplicate = await ConnectAsync(host, 40, 21011);
        Assert.True(online.TryGetByConnection(user40.Connection.ConnectionId, out _));
        Assert.True(online.TryGetByConnection(duplicate.Connection.ConnectionId, out _));
        var user41 = await ConnectAsync(host, 41, 21012); var session41 = Publish(host, user41, 41, 41, 21012);
        var user42 = await ConnectAsync(host, 42, 21999); var unknown = Publish(host, user42, 42, 42, 21999);
        var overview = await GetAsync(host, "overview");
        Assert.Equal(3, overview.GetProperty("onlineAccounts").GetInt32()); Assert.Equal(3, overview.GetProperty("onlineCharacters").GetInt32());
        Assert.Equal(1, overview.GetProperty("channels")[0].GetProperty("onlineCharacters").GetInt32());
        Assert.Equal(1, overview.GetProperty("channels")[1].GetProperty("onlineCharacters").GetInt32());
        Assert.Equal(1, overview.GetProperty("unmappedOnlineCharacters").GetInt32());
        var page1 = await GetAsync(host, "characters?onlineOnly=true&pageSize=1"); var page2 = await GetAsync(host, "characters?onlineOnly=true&pageSize=1&page=2");
        Assert.Equal(3, page1.GetProperty("total").GetInt32()); Assert.Equal(40, page1.GetProperty("items")[0].GetProperty("characterId").GetInt32());
        Assert.Equal(41, page2.GetProperty("items")[0].GetProperty("characterId").GetInt32());
        Assert.Equal(1, (await GetAsync(host, "characters?onlineOnly=true&accountId=41&q=角色41")).GetProperty("total").GetInt32());
        var replacementUser = await ConnectAsync(host, 40, 21012);
        Cleanup(host, prepared); var replacement = Publish(host, replacementUser, 40, 40, 21012); positions.Place(replacement, new(8, 9, 888, 999, 3));
        Cleanup(host, prepared); online.TryDisconnectConnection(user40.Connection.ConnectionId);
        var replaced = await GetAsync(host, "characters/40"); Assert.Equal(12, replaced.GetProperty("channel").GetProperty("channelId").GetInt32());
        Assert.Equal(888, replaced.GetProperty("position").GetProperty("x").GetInt32());
        Cleanup(host, replacement); online.TryDisconnectConnection(replacementUser.Connection.ConnectionId); online.TryDisconnectConnection(duplicate.Connection.ConnectionId);
        var exited = await GetAsync(host, "characters/40"); Assert.False(exited.GetProperty("isOnline").GetBoolean());
        Assert.Equal("saved", exited.GetProperty("position").GetProperty("source").GetString());
        Assert.Equal(10, exited.GetProperty("position").GetProperty("x").GetInt32());
        Assert.False((await GetAsync(host, "accounts/40")).GetProperty("isOnline").GetBoolean());
        var before = await PlayerFactsAsync(host); var snapshotBefore = online.GetRequired(user41.OnlineUserId);
        await GetAsync(host, "overview"); await GetAsync(host, "characters"); await GetAsync(host, "accounts/41");
        Assert.Equal(before, await PlayerFactsAsync(host)); Assert.Equal(snapshotBefore, online.GetRequired(user41.OnlineUserId));
        readiness.NetworkEnabled = true; readiness.IsReady = false; overview = await GetAsync(host, "overview");
        Assert.True(overview.GetProperty("networkEnabled").GetBoolean()); Assert.False(overview.GetProperty("gameReady").GetBoolean());
        readiness.IsReady = true; Assert.True((await GetAsync(host, "overview")).GetProperty("gameReady").GetBoolean());
        Cleanup(host, session41); Cleanup(host, unknown); online.TryDisconnectConnection(user41.Connection.ConnectionId);
        online.TryDisconnectConnection(user42.Connection.ConnectionId); online.TryDisconnectConnection(anonymous.Connection.ConnectionId);
        Assert.Equal(0, (await GetAsync(host, "overview")).GetProperty("onlineAccounts").GetInt32());
    }

    [Fact]
    public async Task GmOnlyOverviewIsAvailableAndReportsGameDisabled()
    {
        await using var host = await GmTestHost.StartAsync();
        Assert.Equal(HttpStatusCode.OK, (await host.LoginAsync(await host.CsrfAsync())).StatusCode);
        var value = await GetAsync(host, "overview");
        Assert.False(value.GetProperty("networkEnabled").GetBoolean()); Assert.False(value.GetProperty("gameReady").GetBoolean());
        Assert.Equal(0, value.GetProperty("onlineAccounts").GetInt32()); Assert.Equal(0, value.GetProperty("totalCharacters").GetInt32());
    }

    internal static readonly IReadOnlyDictionary<string, string?> ChannelConfig = new Dictionary<string, string?>
    {
        ["Channel:Channels:0:Id"] = "11", ["Channel:Channels:0:Name"] = "频道甲", ["Channel:Channels:0:Port"] = "31011", ["Channel:Channels:0:CurrentUsers"] = "99",
        ["Channel:Channels:1:Id"] = "12", ["Channel:Channels:1:Name"] = "频道乙", ["Channel:Channels:1:Port"] = "31012", ["Channel:Channels:1:CurrentUsers"] = "88",
    };
    internal static async Task<JsonElement> GetAsync(GmTestHost host, string path) => await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/" + path);
    private static Task<List<string>> PlayerFactsAsync(GmTestHost host) => host.ReadAsync("SELECT 'A:'||account_id||':'||m_id||':'||password_hash||':'||cera||':'||token_cera||':'||happy_token_cera FROM accounts UNION ALL SELECT 'C:'||character_id||':'||account_id||':'||name||':'||COALESCE(hex(name_bytes),'NULL')||':'||town_id||':'||area_id||':'||pos_x||':'||pos_y FROM characters ORDER BY 1;");
    internal static async Task<OnlineUserSnapshot> ConnectAsync(GmTestHost host, int accountId, int port)
    {
        const string password = "isolated-player-password";
        var credentials = host.Services.GetRequiredService<IPasswordCredentialRuleService>();
        await host.ExecuteAsync($"UPDATE accounts SET password_hash='{credentials.Hash(password)}' WHERE account_id={accountId};");
        var online = host.Services.GetRequiredService<IOnlineUserDomainService>();
        var user = online.RegisterConnection(new(new("fixture-connection-" + Guid.NewGuid().ToString("N")), NetworkEndpointKind.Game, port));
        using var scope = host.Services.CreateScope(); var commands = scope.ServiceProvider.GetRequiredService<ICommandBus>();
        var ticket = await commands.SendAsync(new AuthenticateAndIssueTicketCommand($"player{accountId:D2}", password), default);
        Assert.NotNull(ticket.Ticket);
        var result = await commands.SendAsync(new RedeemGameLoginCommand(user.OnlineUserId, ticket.MId!, ticket.Ticket!), default);
        Assert.Equal(new AccountId(accountId), result.AccountId);
        return online.GetRequired(user.OnlineUserId);
    }
    internal static SelectedSessionIdentity Publish(GmTestHost host, OnlineUserSnapshot user, int account, int character, int port)
    {
        var session = SelectedSessionIdentity.Create(new(account), new(character), user.Connection.ConnectionId, user.Generation, port);
        host.Services.GetRequiredService<ISelectedSessionDomainService>().Select(session);
        host.Services.GetRequiredService<IActiveCharacterDomainService>().Publish(session);
        host.Services.GetRequiredService<ISelectedSessionRuntimeDirectory>().Publish(session, true);
        host.Services.GetRequiredService<ICharacterPositionDomainService>().Place(session, new(3, 4, 100, 200, 2));
        return session;
    }
    internal static void Cleanup(GmTestHost host, SelectedSessionIdentity session)
    {
        host.Services.GetRequiredService<ISelectedSessionRuntimeDirectory>().RemoveExact(session);
        host.Services.GetRequiredService<IActiveCharacterDomainService>().ClearExact(session);
        host.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(session);
        host.Services.GetRequiredService<ICharacterPositionDomainService>().ForgetExact(session);
    }

    private sealed class DirectoryReadiness : IGameServerReadiness { public bool NetworkEnabled { get; set; } public bool IsReady { get; set; } }

    internal static async Task SeedAsync(GmTestHost host)
    {
        var sql = new StringBuilder();
        for (var id = 1; id <= 45; id++)
        {
            sql.Append($"INSERT INTO accounts(account_id,m_id,password_hash,cera,token_cera,happy_token_cera,last_login_ip) VALUES({id},'player{id:D2}','secret-player',{(id == 45 ? int.MaxValue : id)},0,12,'192.0.2.1');");
            if (id < 45)
                sql.Append($"INSERT INTO characters(character_id,account_id,name,name_bytes,job,grow_type,level,town_id,area_id,pos_x,pos_y,direction) VALUES({id},{id},'wrong-name',X'{Convert.ToHexString(Encoding.UTF8.GetBytes($"角色{id:D2}"))}',1,2,86,1,2,10,20,5);");
        }
        sql.Append("INSERT INTO characters(character_id,account_id,name,name_bytes,delete_flag) VALUES(46,1,'wrong-name',X'E4B8ADE69687255F27203C623E',1),(47,1,'fallback-forbidden',NULL,0),(48,1,'fallback-forbidden',X'FFFE',0);");
        await host.ExecuteAsync(sql.ToString());
    }
}
