using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Host.Tests.PlayerPortalTestSupport;

namespace Dfo.Server.Host.Tests;

/// <summary>#329 TASK-05: a player mails a character of the own account, once per request id, by the GM mail's rules.</summary>
public sealed class PlayerPortalMailHttpTests
{
    private const int A1 = 11, A2Deleted = 12, A3 = 13, B1 = 21, Missing = 9999;
    private static readonly object[] StackTwo = [new { itemId = 3, count = 2 }];

    private static async Task<(GmTestHost Host, PlayerClient Player, string Csrf)> StartAsync(TimeProvider? clock = null,
        Action<IServiceCollection>? configureServices = null, string alice = "alice")
    {
        var host = await GmTestHost.StartAsync(clock: clock, configureServices: configureServices);
        await SeedAccountAsync(host, 1, alice); await SeedAccountAsync(host, 2, "bob");
        await SeedCharacterAsync(host, A1, 1, "小明"); await SeedCharacterAsync(host, A2Deleted, 1, "旧角色", deleted: true);
        await SeedCharacterAsync(host, A3, 1, "小红"); await SeedCharacterAsync(host, B1, 2, "鲍勃");
        await SetPortalAsync(host);
        var player = new PlayerClient(host);
        return (host, player, await player.SignInAsync(alice));
    }

    private static Task<List<string>> AuditAsync(GmTestHost host) =>
        host.ReadAsync("SELECT outcome||':'||actor_kind||':'||IFNULL(account_id,'-')||':'||administrator_name||':'||IFNULL(administrator_id,'-')||':'||target FROM gm_audit WHERE action='PlayerSendMail' ORDER BY rowid;");
    private static Task<List<string>> OperationCountAsync(GmTestHost host) => host.ReadAsync("SELECT COUNT(*) FROM player_portal_operations;");
    private static async Task<long> RevisionAsync(GmTestHost host) => long.Parse(Assert.Single(await host.ReadAsync("SELECT revision FROM player_portal_settings;")), System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<JsonElement> AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code, string outcome = "rejected")
    {
        Assert.Equal(status, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal(outcome, body.GetProperty("outcome").GetString());
        return body;
    }

    private static async Task<JsonElement> SentAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static async Task<string> GmMessageAsync(GmTestHost host, string gmCsrf, object payload)
    {
        using var sent = await host.PostAsync("/api/gm/v1/mail/send", gmCsrf, payload);
        Assert.Equal(HttpStatusCode.BadRequest, sent.StatusCode);
        return (await JsonAsync(sent)).GetProperty("message").GetString()!;
    }

    /// <summary>AC-011: items and gold reach the own character as the GM mail would send them; anything beyond {itemId,count} is refused.</summary>
    [Fact]
    public async Task Items_and_gold_reach_the_own_character_by_the_gm_mail_rules()
    {
        var (host, player, csrf) = await StartAsync();
        await using var _ = host; using var __ = player;
        var id = Guid.NewGuid();
        using (var sent = await player.SendMailAsync(csrf, A1, "补给", "给你", StackTwo, 500, id))
        {
            var body = await SentAsync(sent);
            Assert.False(body.GetProperty("replayed").GetBoolean());
            Assert.False(body.TryGetProperty("reminder", out JsonElement _));
            var receipt = body.GetProperty("receipt");
            Assert.Equal(["requestId", "kind", "committedAt", "summary", "result", "characterId", "characterName", "title", "body", "items", "gold", "expiresAt"],
                receipt.EnumerateObject().Select(property => property.Name));
            Assert.Equal(id, receipt.GetProperty("requestId").GetGuid());
            Assert.Equal("mail", receipt.GetProperty("kind").GetString());
            Assert.Equal("成功", receipt.GetProperty("result").GetString());
            Assert.Equal("发给 小明：物品 1 种、金币 500", receipt.GetProperty("summary").GetString());
            Assert.Equal(A1, receipt.GetProperty("characterId").GetInt32());
            Assert.Equal("小明", receipt.GetProperty("characterName").GetString());
            Assert.Equal("给你", receipt.GetProperty("body").GetString());
            var item = Assert.Single(receipt.GetProperty("items").EnumerateArray());
            Assert.Equal(3, item.GetProperty("itemId").GetInt32()); Assert.Equal(2, item.GetProperty("count").GetInt32());
            Assert.Equal("stackable", item.GetProperty("kind").GetString());
            Assert.Equal(500, receipt.GetProperty("gold").GetInt32());
            Assert.Equal(TimeSpan.FromDays(30), receipt.GetProperty("expiresAt").GetDateTimeOffset() - receipt.GetProperty("committedAt").GetDateTimeOffset());
        }
        Assert.Equal([$"{A1}:补给:给你"], await host.ReadAsync("SELECT receiver_character_id||':'||title||':'||body FROM mailbox_messages;"));
        Assert.Equal(["stackable:3:2:0", "gold:0:1:500"],
            await host.ReadAsync("SELECT asset_kind||':'||item_template_id||':'||item_count||':'||gold_amount FROM mailbox_attachments ORDER BY attachment_id;"));

        // Equipment is created exactly as the GM mail creates it: the fixed 最上级 attribute seed.
        Assert.Equal(HttpStatusCode.OK, (await player.SendMailAsync(csrf, A1, items: [new { itemId = 27312, count = 1 }])).StatusCode);
        var core = Convert.FromHexString(Assert.Single(await host.ReadAsync("SELECT hex(item_core) FROM items ORDER BY item_uid DESC LIMIT 1;")));
        Assert.Equal(999999998, BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(5, 4)));
        var mailbox = await MailboxAsync(host);

        // Equipment options, any undeclared field, or an account id are not part of a player's mail (RQ-011, RQ-003).
        foreach (var payload in new object[]
        {
            new { requestId = Guid.NewGuid(), characterId = A1, title = "", body = "x", items = new object[] { new { itemId = 27312, count = 1, equipment = new { seal = "unsealed" } } }, gold = 0 },
            new { requestId = Guid.NewGuid(), characterId = A1, title = "", body = "x", items = new object[] { new { itemId = 27312, count = 1, upgrade = 10 } }, gold = 0 },
            new { requestId = Guid.NewGuid(), characterId = A1, title = "", body = "x", items = Array.Empty<object>(), gold = 0, accountId = 2 },
            new { requestId = Guid.NewGuid(), characterId = A1, title = (string?)null, body = "x", items = Array.Empty<object>(), gold = 0 },
            new { requestId = Guid.NewGuid(), characterId = A1, body = "x", items = Array.Empty<object>(), gold = 0 },
        })
        {
            using var refused = await player.PostAsync("/api/player/v1/mail/send", csrf, payload);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("invalid", await CodeAsync(refused));
        }
        Assert.Equal(mailbox, await MailboxAsync(host));
        Assert.Equal(2, (await AuditAsync(host)).Count);   // the two successes; a request that could not be read is not audited

        // The GM rules refuse the same content with the same words.
        var gmCsrf = await GmCeraHttpTests.Login(host);
        foreach (var (title, body, items) in new (string, string, object[])[]
        {
            ("", "x", [new { itemId = 42, count = 1 }]),
            (new string('a', 512), "x", []),
            ("", "x", Enumerable.Range(3, 11).Select(itemId => (object)new { itemId, count = 1 }).ToArray()),
        })
        {
            var gm = await GmMessageAsync(host, gmCsrf, new { requestId = Guid.NewGuid(), title, body, recipientIds = new[] { A1 }, items, gold = 0 });
            var refused = await AssertRefusedAsync(await player.SendMailAsync(csrf, A1, title, body, items), HttpStatusCode.BadRequest, "invalid");
            Assert.Equal(gm, refused.GetProperty("message").GetString());
        }
        Assert.Equal(mailbox, await MailboxAsync(host));
        Assert.Equal(3, (await AuditAsync(host)).Count(row => row.StartsWith("Rejected:player:1:alice:-:角色 11", StringComparison.Ordinal)));
    }

    /// <summary>AC-011 online path: the recipient is told of the new mail after the commit, by the mailbox's own event, once.</summary>
    [Fact]
    public async Task An_online_recipient_is_told_of_the_new_mail_once()
    {
        var notices = new RecordingNotifications();
        var (host, player, csrf) = await StartAsync(configureServices: services => services.AddSingleton<INotificationBus>(notices), alice: "player01");
        await using var _ = host; using var __ = player;
        var user = await GmPlayerDirectoryHttpTests.ConnectAsync(host, 1, 21011);
        var session = GmPlayerDirectoryHttpTests.Publish(host, user, 1, A1, 21011);
        var me = await JsonAsync(await player.Client.GetAsync("/api/player/v1/me"));
        Assert.Contains(me.GetProperty("characters").EnumerateArray(), character =>
            character.GetProperty("characterId").GetInt32() == A1 && character.GetProperty("isOnline").GetBoolean());

        var id = Guid.NewGuid();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.Equal(attempt == 1, (await SentAsync(await player.SendMailAsync(csrf, A1, items: StackTwo, id: id))).GetProperty("replayed").GetBoolean());
            await host.Services.GetRequiredService<DomainEventDispatcher>().DrainOnceAsync();
            await SettledAsync(host);
            var notice = Assert.Single(notices.Delivered);   // a replay delivers nothing, so it announces nothing
            Assert.Equal(new CharacterId(A1), Assert.Single(Assert.IsType<NotificationAudience.TheseCharacters>(notice.Audience).CharacterIds));
            Assert.Equal(new CharacterId(A1), notice.Mail.Character);
        }
        GmPlayerDirectoryHttpTests.Cleanup(host, session);
    }

    /// <summary>R-3, RQ-011: an empty mail is refused before the GM rules fill in their default body; a blank body with gold is sent with it.</summary>
    [Fact]
    public async Task A_mail_needs_a_body_or_an_attachment_judged_before_the_default_body()
    {
        var (host, player, csrf) = await StartAsync();
        await using var _ = host; using var __ = player;
        foreach (var (title, body) in new[] { ("", ""), ("", "   "), ("只有标题", "") })
        {
            var refused = await AssertRefusedAsync(await player.SendMailAsync(csrf, A1, title, body), HttpStatusCode.BadRequest, "invalid");
            Assert.Equal("邮件至少要有正文或附件。", refused.GetProperty("message").GetString());
        }
        Assert.Equal("0:0:0", await MailboxAsync(host));
        Assert.Equal(3, (await AuditAsync(host)).Count(row => row.StartsWith("Rejected:", StringComparison.Ordinal)));

        var receipt = (await SentAsync(await player.SendMailAsync(csrf, A1, body: "", gold: 100))).GetProperty("receipt");
        Assert.Equal("此为官方邮件，无需回复。", receipt.GetProperty("body").GetString());
        Assert.Equal("发给 小明：金币 100", receipt.GetProperty("summary").GetString());
        Assert.Equal(["此为官方邮件，无需回复。"], await host.ReadAsync("SELECT body FROM mailbox_messages;"));
        Assert.Equal("发给 小明：正文", (await SentAsync(await player.SendMailAsync(csrf, A1, body: "只有正文"))).GetProperty("receipt").GetProperty("summary").GetString());
    }

    /// <summary>AC-005: someone else's, a deleted and a missing character are refused alike, and nothing anywhere changes.</summary>
    [Fact]
    public async Task Another_accounts_a_deleted_and_a_missing_character_are_refused_alike()
    {
        var (host, player, csrf) = await StartAsync();
        await using var _ = host; using var __ = player;
        var answers = new List<string>();
        foreach (var character in new[] { B1, A2Deleted, Missing })
        {
            using var refused = await player.SendMailAsync(csrf, character, items: [new { itemId = 27312, count = 1 }], gold: 5);
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
            answers.Add(await refused.Content.ReadAsStringAsync());
        }
        Assert.Single(answers.Distinct());
        Assert.Equal("not_found", JsonDocument.Parse(answers[0]).RootElement.GetProperty("code").GetString());
        Assert.Equal("收件角色不存在。", JsonDocument.Parse(answers[0]).RootElement.GetProperty("message").GetString());
        Assert.Equal("0:0:0", await MailboxAsync(host));
        Assert.Equal(["0"], await OperationCountAsync(host));
        Assert.Equal(["Rejected:player:1:alice:-:角色 21", "Rejected:player:1:alice:-:角色 12", "Rejected:player:1:alice:-:角色 9999"], await AuditAsync(host));
        await AssertRefusedAsync(await player.SendMailAsync(csrf, 0), HttpStatusCode.BadRequest, "invalid");
    }

    /// <summary>AC-012: one delivery per request id, replayed even after the recipient is deleted, the switch closed or the day's mails used up.</summary>
    [Fact]
    public async Task A_request_id_delivers_once_and_replays_even_after_the_recipient_is_deleted()
    {
        var (host, player, csrf) = await StartAsync();
        await using var _ = host; using var __ = player;
        await SetMailLimitsAsync(host, mailsPerDay: 1);
        var id = Guid.NewGuid();
        object[] items = [new { itemId = 3, count = 2 }, new { itemId = 27312, count = 1 }];
        var first = (await SentAsync(await player.SendMailAsync(csrf, A1, "t", "b", items, 10, id))).GetProperty("receipt").GetRawText();
        var mailbox = await MailboxAsync(host);
        void Same(JsonElement replay)
        {
            Assert.True(replay.GetProperty("replayed").GetBoolean());
            Assert.Equal(first, replay.GetProperty("receipt").GetRawText());
        }
        Same(await SentAsync(await player.SendMailAsync(csrf, A1, "t", "b", items, 10, id)));          // the day's one mail is used
        await SetPortalAsync(host, mail: false);
        Same(await SentAsync(await player.SendMailAsync(csrf, A1, "t", "b", items, 10, id)));          // the mail switch is off
        await SetPortalAsync(host);
        await host.ExecuteAsync($"UPDATE characters SET delete_flag=1 WHERE character_id={A1};");
        Same(await SentAsync(await player.SendMailAsync(csrf, A1, "t", "b", items, 10, id)));          // the recipient is deleted
        await AssertRefusedAsync(await player.SendMailAsync(csrf, A1, "t", "b", items, 10), HttpStatusCode.NotFound, "not_found");
        await host.ExecuteAsync($"UPDATE characters SET delete_flag=0 WHERE character_id={A1};");
        await SetMailLimitsAsync(host);

        foreach (var changed in new Func<Task<HttpResponseMessage>>[]
        {
            () => player.SendMailAsync(csrf, A1, "t", "b", items, 11, id),
            () => player.SendMailAsync(csrf, A1, "t", "b", [items[1], items[0]], 10, id),
            () => player.SendMailAsync(csrf, A3, "t", "b", items, 10, id),
        })
            await AssertRefusedAsync(await changed(), HttpStatusCode.Conflict, "request_conflict");
        var ceraId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await player.GrantAsync(csrf, "Cera", 1, ceraId)).StatusCode);
        await AssertRefusedAsync(await player.SendMailAsync(csrf, A1, id: ceraId), HttpStatusCode.Conflict, "request_conflict");
        Assert.Equal(mailbox, await MailboxAsync(host));
        Assert.Equal(["Succeeded", "Rejected", "Rejected", "Rejected", "Rejected", "Rejected"],
            (await AuditAsync(host)).Select(row => row[..row.IndexOf(':')]));

        using var bob = new PlayerClient(host);
        Assert.Equal(HttpStatusCode.OK, (await bob.SendMailAsync(await bob.SignInAsync("bob"), B1, "t", "b", items, 10, id)).StatusCode);
    }

    /// <summary>AC-021 (mail): the mail switch refuses only mail; a closed portal answers portal_closed, never 500.</summary>
    [Fact]
    public async Task Closing_the_mail_switch_refuses_only_mail_and_a_closed_portal_answers_portal_closed()
    {
        var (host, player, csrf) = await StartAsync();
        await using var _ = host; using var __ = player;
        await SetPortalAsync(host, mail: false);
        var refused = await AssertRefusedAsync(await player.SendMailAsync(csrf, A1), HttpStatusCode.Forbidden, "feature_disabled");
        Assert.Equal("发邮件已关闭。", refused.GetProperty("message").GetString());
        var me = await JsonAsync(await player.Client.GetAsync("/api/player/v1/me"));
        Assert.False(me.GetProperty("portal").GetProperty("mailEnabled").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await player.Client.GetAsync("/api/player/v1/operations")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await player.GrantAsync(csrf, "Cera", 1)).StatusCode);
        Assert.Equal(["Rejected:player:1:alice:-:角色 11"], await AuditAsync(host));

        await SetPortalAsync(host, enabled: false);
        using var closed = await player.SendMailAsync(csrf, A1);
        Assert.Equal(HttpStatusCode.Forbidden, closed.StatusCode);
        Assert.Equal("portal_closed", await CodeAsync(closed));
        Assert.Equal("0:0:0", await MailboxAsync(host));
        Assert.Single(await AuditAsync(host));
    }

    /// <summary>AC-008(a): the portal or the mail switch closed after a mail passed the boundary refuses it inside the CMD.</summary>
    [Theory]
    [InlineData(false, true, "portal_closed")]
    [InlineData(true, false, "feature_disabled")]
    public async Task Closing_after_a_mail_passed_the_boundary_refuses_it_inside_the_cmd(bool enabled, bool mail, string code)
    {
        var gate = new PlayerPortalCeraHttpTests.BoundaryGate();
        var (host, player, csrf) = await StartAsync(configureServices: gate.Wrap);
        await using var _ = host; using var __ = player;
        var gmCsrf = await GmCeraHttpTests.Login(host);
        var revision = await RevisionAsync(host);
        gate.Arm(1);
        var pending = player.SendMailAsync(csrf, A1, items: StackTwo, gold: 5);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using (var close = await host.PostAsync("/api/gm/v1/player-portal/settings", gmCsrf, GmPlayerPortalHttpTests.Settings(enabled: enabled, mail: mail, revision: revision)))
            Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        gate.Release.SetResult();
        // An outcome is only answered by the CMD: the boundary's own refusal has none.
        await AssertRefusedAsync(await pending, HttpStatusCode.Forbidden, code);
        Assert.Equal("0:0:0", await MailboxAsync(host));
        Assert.Equal(["0"], await OperationCountAsync(host));
        // A closed portal ended the session: nobody to audit under. A closed mail switch is the player's audited refusal.
        if (enabled) Assert.Equal(["Rejected:player:1:alice:-:角色 11"], await AuditAsync(host));
        else Assert.Empty(await AuditAsync(host));
    }

    /// <summary>AC-008(c): with one of two daily mails sent, two concurrent mails: exactly one is delivered.</summary>
    [Fact]
    public async Task Two_concurrent_mails_never_pass_the_daily_count_together()
    {
        var gate = new PlayerPortalCeraHttpTests.BoundaryGate();
        var (host, player, csrf) = await StartAsync(configureServices: gate.Wrap);
        await using var _ = host; using var __ = player;
        await SetMailLimitsAsync(host, mailsPerDay: 2);
        Assert.Equal(HttpStatusCode.OK, (await player.SendMailAsync(csrf, A1)).StatusCode);
        gate.Arm(2);
        var both = new[] { player.SendMailAsync(csrf, A1, gold: 1), player.SendMailAsync(csrf, A3, gold: 2) };
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        gate.Release.SetResult();
        var responses = await Task.WhenAll(both);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        await AssertRefusedAsync(Assert.Single(responses, response => response.StatusCode != HttpStatusCode.OK), HttpStatusCode.Conflict, "limit_exceeded");
        foreach (var response in responses) response.Dispose();
        Assert.Equal(["2"], await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
        Assert.Equal(["2"], await OperationCountAsync(host));
        Assert.Single(await AuditAsync(host), row => row.StartsWith("Rejected", StringComparison.Ordinal));
    }

    /// <summary>AC-022 (mail): gold per mail and mails per day, refusals not counted, cera not counted, and a new game day starts again.</summary>
    [Fact]
    public async Task Mail_limits_count_only_delivered_mails_and_a_new_game_day_starts_again()
    {
        var clock = new GmTestClock();
        var (host, player, csrf) = await StartAsync(clock);
        await using var _ = host; using var __ = player;
        await SetMailLimitsAsync(host, mailGoldPerMail: 500, mailsPerDay: 2);
        var refused = await AssertRefusedAsync(await player.SendMailAsync(csrf, A1, gold: 501), HttpStatusCode.Conflict, "limit_exceeded");
        Assert.Equal("单封金币最多 500。", refused.GetProperty("message").GetString());
        await AssertRefusedAsync(await player.SendMailAsync(csrf, A1, items: [new { itemId = 42, count = 1 }]), HttpStatusCode.BadRequest, "invalid");
        Assert.Equal(HttpStatusCode.OK, (await player.GrantAsync(csrf, "Cera", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await player.SendMailAsync(csrf, A1, gold: 500)).StatusCode);   // the refusals and the cera counted nothing
        Assert.Equal(HttpStatusCode.OK, (await player.SendMailAsync(csrf, A3)).StatusCode);              // exactly the daily count
        refused = await AssertRefusedAsync(await player.SendMailAsync(csrf, A1), HttpStatusCode.Conflict, "limit_exceeded");
        Assert.Equal("今日邮件已达上限（2 封）。", refused.GetProperty("message").GetString());
        Assert.Equal(2, (await JsonAsync(await player.Client.GetAsync("/api/player/v1/me"))).GetProperty("portal").GetProperty("today").GetProperty("mails").GetInt64());

        var days = host.Services.GetRequiredService<IDailyResetRuleService>();
        var yesterday = days.PeriodDay(clock.GetUtcNow());
        clock.Advance(days.PeriodStart(clock.GetUtcNow()).AddDays(1) - clock.GetUtcNow() + TimeSpan.FromSeconds(1));
        var today = days.PeriodDay(clock.GetUtcNow());
        csrf = await player.SignInAsync("alice");
        Assert.Equal(HttpStatusCode.OK, (await player.SendMailAsync(csrf, A1)).StatusCode);
        Assert.Equal([$"{yesterday:yyyy-MM-dd}:2", $"{today:yyyy-MM-dd}:1"],
            await host.ReadAsync("SELECT period_day||':'||COUNT(*) FROM player_portal_operations WHERE kind='mail' GROUP BY period_day ORDER BY period_day;"));
        Assert.Equal(1, (await JsonAsync(await player.Client.GetAsync("/api/player/v1/me"))).GetProperty("portal").GetProperty("today").GetProperty("mails").GetInt64());

        await SetMailLimitsAsync(host);
        Assert.Equal(HttpStatusCode.OK, (await player.SendMailAsync(csrf, A1, gold: 2000000000)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await player.SendMailAsync(csrf, A1)).StatusCode);
    }

    /// <summary>AC-024 (mail): success and refusal are the player's records with the account; the GM audit filters them.</summary>
    [Fact]
    public async Task Player_mails_are_audited_as_the_player_and_filtered_by_actor()
    {
        var (host, player, csrf) = await StartAsync();
        await using var _ = host; using var __ = player;
        await SetMailLimitsAsync(host, mailGoldPerMail: 10);
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await player.SendMailAsync(csrf, A1, gold: 10, id: id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await player.SendMailAsync(csrf, A1, gold: 10, id: id)).StatusCode);   // a replay writes nothing
        Assert.Equal(HttpStatusCode.Conflict, (await player.SendMailAsync(csrf, A1, gold: 11)).StatusCode);
        using (var anonymous = new PlayerClient(host))
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendMailAsync(await anonymous.CsrfAsync(), A1)).StatusCode);
        using (var unread = await player.PostAsync("/api/player/v1/mail/send", csrf, new { requestId = Guid.NewGuid() }))
            Assert.Equal(HttpStatusCode.BadRequest, unread.StatusCode);
        Assert.Equal(["Succeeded:player:1:alice:-:角色 11", "Rejected:player:1:alice:-:角色 11"], await AuditAsync(host));
        var reasons = await host.ReadAsync("SELECT reason FROM gm_audit WHERE action='PlayerSendMail' ORDER BY rowid;");
        Assert.StartsWith("发给 小明：金币 10，邮件 ", reasons[0], StringComparison.Ordinal);
        Assert.EndsWith($"编号 {id:N}", reasons[0], StringComparison.Ordinal);
        Assert.StartsWith("未提交：单封金币最多 10。，编号 ", reasons[1], StringComparison.Ordinal);

        await SetPortalAsync(host, enabled: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.SendMailAsync(csrf, A1)).StatusCode);
        Assert.Equal(2, (await AuditAsync(host)).Count);

        await GmCeraHttpTests.Login(host);
        var page = await JsonAsync(await host.Client.GetAsync("/api/gm/v1/audit?actor=player&action=PlayerSendMail"));
        Assert.Equal(2, page.GetProperty("total").GetInt64());
        Assert.All(page.GetProperty("items").EnumerateArray(), item =>
        {
            Assert.Equal("player", item.GetProperty("actorKind").GetString());
            Assert.Equal(1, item.GetProperty("accountId").GetInt32());
        });
    }

    /// <summary>A commit that cannot be confirmed is audited as unknown under the player; the same id then replays, delivered once.</summary>
    [Fact]
    public async Task An_unconfirmed_commit_is_audited_under_the_player_and_the_same_id_replays()
    {
        var poison = new PoisonCommit();
        var (host, player, csrf) = await StartAsync(configureServices: services => services.AddSingleton<IGmAuditDomainService>(provider =>
            new CommitFaultAudit(provider.GetRequiredService<IGmAdministrationPersistence>(), provider.GetRequiredService<IDomainTransactionAccessor>(), poison)));
        await using var _ = host; using var __ = player;
        var id = Guid.NewGuid();
        using (var unconfirmed = await player.SendMailAsync(csrf, A1, items: StackTwo, id: id))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, unconfirmed.StatusCode);
            var body = await JsonAsync(unconfirmed);
            Assert.Equal("unknown", body.GetProperty("code").GetString());
            Assert.Equal("unknown", body.GetProperty("outcome").GetString());
        }
        Assert.Equal(["Succeeded:player:1:alice:-:角色 11", "Unknown:player:1:alice:-:角色 11"], await AuditAsync(host));
        Assert.True((await SentAsync(await player.SendMailAsync(csrf, A1, items: StackTwo, id: id))).GetProperty("replayed").GetBoolean());
        Assert.Equal(["1"], await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
    }

    /// <summary>A storage fault inside the delivery rolls everything back and is audited apart as rolled back.</summary>
    [Fact]
    public async Task A_storage_fault_rolls_the_whole_mail_back_and_is_audited_apart()
    {
        var (host, player, csrf) = await StartAsync();
        await using var _ = host; using var __ = player;
        await host.ExecuteAsync("CREATE TRIGGER fail_attachment BEFORE INSERT ON mailbox_attachments BEGIN SELECT RAISE(ABORT,'isolated attachment fault'); END;");
        await AssertRefusedAsync(await player.SendMailAsync(csrf, A1, items: [new { itemId = 27312, count = 1 }], gold: 5),
            HttpStatusCode.InternalServerError, "unknown", "rolledBack");
        Assert.Equal("0:0:0", await MailboxAsync(host));
        Assert.Equal(["0"], await OperationCountAsync(host));
        Assert.Equal(["RolledBack:player:1:alice:-:角色 11"], await AuditAsync(host));
    }

    /// <summary>F-104 of the GM mail tests: a delivery the dispatch loop took is on the executor, so a later marker job waits for it.</summary>
    private static async Task SettledAsync(GmTestHost host) =>
        await host.Services.GetRequiredService<IGameplayQueue>().RunAsync(new GameplayWork(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Gm), "test:settled", _ => ValueTask.CompletedTask), default);

    private sealed class RecordingNotifications : INotificationBus
    {
        internal ConcurrentQueue<(NotificationAudience Audience, MailDelivered Mail)> Delivered { get; } = new();
        public ValueTask<NotificationDelivery> PublishAsync<T>(NotificationAudience audience, T notification, CancellationToken token) where T : class, INotification<T>
        {
            if (notification is MailDelivered mail) Delivered.Enqueue((audience, mail));
            return ValueTask.FromResult(new NotificationDelivery([]));
        }
    }

    private sealed class PoisonCommit : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken token) => throw new IOException("post-SQLite commit fault");
        public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
    }

    private sealed class CommitFaultAudit(IGmAdministrationPersistence persistence, IDomainTransactionAccessor transactions, PoisonCommit poison) : IGmAuditDomainService
    {
        public async ValueTask AppendAsync(GmAuditRecord record, CancellationToken token)
        {
            await persistence.AppendAuditAsync(record, token);
            if (record is { Action: "PlayerSendMail", Outcome: "Succeeded" }) transactions.Current!.Enlist(poison, () => poison);
        }
        public ValueTask<GmAuditPage> QueryAsync(int page, int pageSize, GmAuditFilter filter, CancellationToken token) =>
            persistence.QueryAuditAsync(page, pageSize, filter, token);
    }
}
