using System.Net;
using static Dfo.Server.Host.Tests.PlayerPortalTestSupport;

namespace Dfo.Server.Host.Tests;

/// <summary>#329 TASK-04 / TASK-05, AC-013: a player pages through the own succeeded operations only.</summary>
public sealed class PlayerPortalRecordsHttpTests
{
    private const string RecordsPath = "/api/player/v1/operations";

    [Fact]
    public async Task Records_list_only_the_own_succeeded_operations_newest_first()
    {
        var clock = new GmTestClock();
        await using var host = await GmTestHost.StartAsync(clock: clock);
        await SeedAccountAsync(host, 1, "alice"); await SeedAccountAsync(host, 2, "bob");
        await SetPortalAsync(host);
        using var alice = new PlayerClient(host); var csrf = await alice.SignInAsync("alice");
        using var bob = new PlayerClient(host); var bobCsrf = await bob.SignInAsync("bob");

        var receipts = new List<string>();
        foreach (var amount in new[] { 1, 2, 3 })
        {
            using var granted = await alice.GrantAsync(csrf, "Cera", amount);
            Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
            receipts.Insert(0, (await JsonAsync(granted)).GetProperty("receipt").GetRawText());
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GrantAsync(csrf, "Cera", 0)).StatusCode);
        string bobRequest;
        using (var other = await bob.GrantAsync(bobCsrf, "TokenCera", 7))
            bobRequest = (await JsonAsync(other)).GetProperty("receipt").GetProperty("requestId").GetString()!;

        var all = await JsonAsync(await alice.Client.GetAsync(RecordsPath + "?accountId=2"));
        Assert.Equal(3, all.GetProperty("total").GetInt64());
        Assert.Equal(1, all.GetProperty("page").GetInt32());
        Assert.Equal(20, all.GetProperty("pageSize").GetInt32());
        var items = all.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(receipts, items.Select(item => item.GetRawText()));
        Assert.All(items, item =>
        {
            Assert.Equal("cera", item.GetProperty("kind").GetString());
            Assert.Equal("成功", item.GetProperty("result").GetString());
            Assert.True(item.TryGetProperty("committedAt", out _));
            Assert.True(item.TryGetProperty("summary", out _));
            Assert.NotEqual(bobRequest, item.GetProperty("requestId").GetString());
        });
        Assert.Equal(["点券 +3", "点券 +2", "点券 +1"], items.Select(item => item.GetProperty("summary").GetString()));

        var first = await JsonAsync(await alice.Client.GetAsync(RecordsPath + "?page=1&pageSize=2"));
        var second = await JsonAsync(await alice.Client.GetAsync(RecordsPath + "?page=2&pageSize=2"));
        Assert.Equal(3, second.GetProperty("total").GetInt64());
        Assert.Equal(receipts, first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).Select(item => item.GetRawText()));
        Assert.Equal(3, (await JsonAsync(await alice.Client.GetAsync(RecordsPath + "?kind=cera"))).GetProperty("total").GetInt64());
        var mail = await JsonAsync(await alice.Client.GetAsync(RecordsPath + "?kind=mail"));
        Assert.Equal(0, mail.GetProperty("total").GetInt64());
        Assert.Empty(mail.GetProperty("items").EnumerateArray());
        foreach (var query in new[] { "?kind=x", "?kind=cera&kind=cera", "?page=0", "?pageSize=101", "?page=abc", "?pageSize=-1", "?page=1&page=2" })
        {
            using var refused = await alice.Client.GetAsync(RecordsPath + query);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("invalid", await CodeAsync(refused));
        }

        var bobs = await JsonAsync(await bob.Client.GetAsync(RecordsPath));
        Assert.Equal(1, bobs.GetProperty("total").GetInt64());
        Assert.Equal("代币券 +7", Assert.Single(bobs.GetProperty("items").EnumerateArray()).GetProperty("summary").GetString());

        // Viewing is gated by the portal and the session only, never by a feature switch (RQ-021).
        await SetPortalAsync(host, cera: false, mail: false);
        Assert.Equal(HttpStatusCode.OK, (await alice.Client.GetAsync(RecordsPath)).StatusCode);
        await SetPortalAsync(host, enabled: false);
        using var closed = await alice.Client.GetAsync(RecordsPath);
        Assert.Equal(HttpStatusCode.Forbidden, closed.StatusCode);
        Assert.Equal("portal_closed", await CodeAsync(closed));
    }

    /// <summary>AC-013 (mail part): mails are records of the own account, filtered by kind, each as the send answered it.</summary>
    [Fact]
    public async Task Mail_records_are_the_own_sent_mails_as_they_were_answered()
    {
        var clock = new GmTestClock();
        await using var host = await GmTestHost.StartAsync(clock: clock);
        await SeedAccountAsync(host, 1, "alice"); await SeedAccountAsync(host, 2, "bob");
        await SeedCharacterAsync(host, 11, 1, "小明"); await SeedCharacterAsync(host, 21, 2, "鲍勃");
        await SetPortalAsync(host);
        using var alice = new PlayerClient(host); var csrf = await alice.SignInAsync("alice");
        using var bob = new PlayerClient(host); var bobCsrf = await bob.SignInAsync("bob");

        Assert.Equal(HttpStatusCode.OK, (await alice.GrantAsync(csrf, "Cera", 1)).StatusCode);
        clock.Advance(TimeSpan.FromSeconds(1));
        var mails = new List<string>();
        foreach (var gold in new[] { 10, 20 })
        {
            using var sent = await alice.SendMailAsync(csrf, 11, gold: gold);
            Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
            mails.Insert(0, (await JsonAsync(sent)).GetProperty("receipt").GetRawText());
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.Equal(HttpStatusCode.NotFound, (await alice.SendMailAsync(csrf, 21)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.SendMailAsync(bobCsrf, 21, gold: 30)).StatusCode);

        var all = await JsonAsync(await alice.Client.GetAsync(RecordsPath));
        Assert.Equal(3, all.GetProperty("total").GetInt64());
        Assert.Equal(["mail", "mail", "cera"], all.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("kind").GetString()));
        var mail = await JsonAsync(await alice.Client.GetAsync(RecordsPath + "?kind=mail"));
        Assert.Equal(2, mail.GetProperty("total").GetInt64());
        var items = mail.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(mails, items.Select(item => item.GetRawText()));
        Assert.All(items, item =>
        {
            foreach (var field in new[] { "requestId", "kind", "committedAt", "summary", "result" }) Assert.True(item.TryGetProperty(field, out _), field);
            Assert.Equal(11, item.GetProperty("characterId").GetInt32());
        });
        Assert.Equal(["发给 小明：金币 20", "发给 小明：金币 10"], items.Select(item => item.GetProperty("summary").GetString()));
        var cera = await JsonAsync(await alice.Client.GetAsync(RecordsPath + "?kind=cera"));
        Assert.Equal("cera", Assert.Single(cera.GetProperty("items").EnumerateArray()).GetProperty("kind").GetString());
        Assert.Equal("发给 鲍勃：金币 30", Assert.Single((await JsonAsync(await bob.Client.GetAsync(RecordsPath))).GetProperty("items").EnumerateArray()).GetProperty("summary").GetString());
    }
}
