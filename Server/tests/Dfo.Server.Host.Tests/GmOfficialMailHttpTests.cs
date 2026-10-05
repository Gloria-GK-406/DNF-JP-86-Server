using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Content;
using System.Collections.Immutable;
using Dfo.Server.Application.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Protocol.Game.Mailbox;

namespace Dfo.Server.Host.Tests;

public sealed partial class GmOfficialMailHttpTests
{
    private static object Payload(Guid id, int[]? recipients = null, string body = "测试金币", string title = "官方补偿", int gold = 100, object[]? items = null) =>
        new { requestId = id, title, body, recipientIds = recipients ?? [1], items = items ?? [], gold };
    private static async Task<(string Csrf, string Secret)> Login(GmTestHost host, string name = "operator")
    {
        using var response = await host.LoginAsync(await host.CsrfAsync(), name);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("Dfo.Gm.Session=", StringComparison.Ordinal)).Split(';')[0];
        return (await host.CsrfAsync(), cookie["Dfo.Gm.Session=".Length..]);
    }
    [Fact]
    public async Task SendMailOnlyAdministratorCanSendAndReplayOneDurableGoldMail()
    {
        await using var host = await GmTestHost.StartAsync(permissions: "SendMail");
        await GmPlayerDirectoryHttpTests.SeedAsync(host);
        Assert.Equal(HttpStatusCode.OK, (await host.LoginAsync(await host.CsrfAsync())).StatusCode);
        var csrf = await host.CsrfAsync();
        var requestId = Guid.NewGuid();
        var payload = new { requestId, title = "官方补偿", body = "测试金币", recipientIds = new[] { 1 }, items = Array.Empty<object>(), gold = 100 };
        using var first = await host.PostAsync("/api/gm/v1/mail/send", csrf, payload);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var repeated = await host.PostAsync("/api/gm/v1/mail/send", csrf, payload);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        var receipt = await first.Content.ReadFromJsonAsync<JsonElement>();
        var replay = await repeated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(receipt.GetProperty("receipt").GetProperty("recipients")[0].GetProperty("mailId").GetInt64(),
            replay.GetProperty("receipt").GetProperty("recipients")[0].GetProperty("mailId").GetInt64());
        Assert.True(replay.GetProperty("replayed").GetBoolean());
        Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages WHERE receiver_character_id=1;"));
        Assert.Equal(new[] { "100" }, await host.ReadAsync("SELECT gold_amount FROM mailbox_attachments WHERE asset_kind='gold';"));
        Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM gm_audit WHERE action='SendOfficialMail' AND outcome='Succeeded';"));
    }

    [Fact]
    public async Task BatchIsNormalizedDurableAndDifferentPayloadConflictsAcrossRestart()
    {
        var directory = GmTestHost.NewDirectory(); var id = Guid.NewGuid(); long mail;
        await using (var host = await GmTestHost.StartAsync(directory: directory))
        {
            await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host);
            var tasks = Enumerable.Range(0, 5).Select(_ => host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(id, [2, 1, 2])));
            var responses = await Task.WhenAll(tasks); Assert.All(responses, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
            mail = (await responses[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receipt").GetProperty("recipients")[0].GetProperty("mailId").GetInt64();
            foreach (var response in responses) response.Dispose();
            Assert.Equal(new[] { "2" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
            Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM gm_mail_operations;"));
            Assert.Equal(HttpStatusCode.Conflict, (await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(id, [1, 2], gold: 101))).StatusCode);
            await host.ExecuteAsync("UPDATE characters SET delete_flag=1 WHERE character_id=1;");
        }
        await using (var reopened = await GmTestHost.StartAsync(directory: directory))
        {
            var login = await Login(reopened); using var replay = await reopened.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(id, [1, 2]));
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(mail, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receipt").GetProperty("recipients")[0].GetProperty("mailId").GetInt64());
            Assert.Equal(new[] { "2" }, await reopened.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
            Assert.Equal(HttpStatusCode.OK, (await reopened.Client.GetAsync("/api/gm/v1/mail/operations/" + id)).StatusCode);
        }
    }

    [Fact]
    public async Task DraftAndTemplatePersistWithOptimisticVersionsAndPrivateOwnership()
    {
        var directory = GmTestHost.NewDirectory(); Guid template;
        await using (var host = await GmTestHost.StartAsync(directory: directory))
        {
            await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host);
            var input = new { title = "稿件", body = "保存后刷新", recipientIds = new[] { 1, 2 }, items = Array.Empty<object>(), gold = 0, expectedRevision = 0, name = "模板甲" };
            Assert.Equal(HttpStatusCode.OK, (await Write(host, "PUT", "/draft", login.Csrf, input)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Write(host, "PUT", "/draft", login.Csrf, input)).StatusCode);
            using var saved = await host.PostAsync("/api/gm/v1/mail/templates", login.Csrf, input); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var value = await saved.Content.ReadFromJsonAsync<JsonElement>(); template = value.GetProperty("id").GetGuid();
            Assert.Empty(value.GetProperty("input").GetProperty("recipientIds").EnumerateArray());
            Assert.Equal(HttpStatusCode.Conflict, (await host.PostAsync("/api/gm/v1/mail/templates", login.Csrf, input)).StatusCode);
            await host.ExecuteAsync("INSERT INTO gm_administrators(login_name,password_record,enabled,permissions) SELECT 'other',password_record,1,2 FROM gm_administrators LIMIT 1;");
            var other = await Login(host, "other");
            Assert.Equal(0, (await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/mail/draft")).GetProperty("revision").GetInt64());
            Assert.Equal(HttpStatusCode.NotFound, (await Write(host, "DELETE", "/templates/" + template, other.Csrf, input)).StatusCode);
        }
        await using (var reopened = await GmTestHost.StartAsync(directory: directory))
        {
            var login = await Login(reopened);
            Assert.Equal("保存后刷新", (await reopened.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/mail/draft")).GetProperty("input").GetProperty("body").GetString());
            Assert.Equal(template, (await reopened.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/mail/templates"))[0].GetProperty("id").GetGuid());
            var delete = new { title = "", body = "", recipientIds = Array.Empty<int>(), items = Array.Empty<object>(), gold = 0, expectedRevision = 1 };
            Assert.Equal(HttpStatusCode.OK, (await Write(reopened, "DELETE", "/templates/" + template, login.Csrf, delete)).StatusCode);
            Assert.Empty((await reopened.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/mail/templates")).EnumerateArray());
        }
    }

    [Theory]
    [InlineData(511, true)]
    [InlineData(512, false)]
    public async Task Utf8DisplayBoundsAreEnforcedBeforeMailIsCreated(int bytes, bool allowed)
    {
        await using var host = await GmTestHost.StartAsync(); await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host);
        var body = string.Concat(Enumerable.Repeat("中", 170)) + new string('x', bytes - 510);
        using var result = await host.PostAsync("/api/gm/v1/mail/preview", login.Csrf, Payload(Guid.NewGuid(), body: body, title: ""));
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result.StatusCode);
        if (allowed) Assert.Equal(bytes, (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("utf8Bytes").GetInt32());
        using var sent = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(Guid.NewGuid(), body: body, title: ""));
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.BadRequest, sent.StatusCode);
        Assert.Equal(new[] { allowed ? "1" : "0" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
    }

    [Fact]
    public async Task PreviewMatchesStoredNormalizationAndInvalidInputDoesNotCreateMail()
    {
        await using var host = await GmTestHost.StartAsync(); await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host);
        using var preview = await host.PostAsync("/api/gm/v1/mail/preview", login.Csrf, Payload(Guid.NewGuid(), body: "正文\r\n二", title: "标题"));
        Assert.Equal("标题\n正文\n二", (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("displayText").GetString());
        foreach (var input in new[] { Payload(Guid.NewGuid(), [1, 46]), Payload(Guid.NewGuid(), [1, int.MaxValue]), Payload(Guid.NewGuid(), body: "bad\0"), Payload(Guid.NewGuid(), Enumerable.Repeat(1, 101).ToArray()), Payload(Guid.NewGuid(), gold: -1), Payload(Guid.Empty) })
            Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, input)).StatusCode);
        Assert.Equal(new[] { "0" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
        var page = await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/mail/recipients?characterId=48");
        Assert.Equal("invalid", page.GetProperty("items")[0].GetProperty("nameDecodeStatus").GetString());
        Assert.DoesNotContain("balances", page.GetRawText()); Assert.DoesNotContain("loginName", page.GetRawText());
        Assert.Equal(0, (await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/mail/recipients?characterId=46")).GetProperty("total").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/gm/v1/mail/recipients?page=1&page=2")).StatusCode);
    }

    [Theory]
    [InlineData("", "此为官方邮件，无需回复。")]
    [InlineData("\t \r\n", "此为官方邮件，无需回复。")]
    [InlineData(" 自定义正文\r\n二 ", " 自定义正文\n二 ")]
    public async Task OptionalBodyDefaultsOnlyWhenSendingAndPreviewMatchesDurableMail(string body, string expected)
    {
        await using var host = await GmTestHost.StartAsync(); await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host);
        var editor = new { title = "官方补偿", body, recipientIds = new[] { 1 }, items = Array.Empty<object>(), gold = 100, expectedRevision = 0, name = "可选正文" };
        using var draft = await Write(host, "PUT", "/draft", login.Csrf, editor);
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        var storedBody = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(storedBody, (await draft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("input").GetProperty("body").GetString());
        using var template = await host.PostAsync("/api/gm/v1/mail/templates", login.Csrf, editor);
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        Assert.Equal(storedBody, (await template.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("input").GetProperty("body").GetString());
        var id = Guid.NewGuid(); var payload = Payload(id, body: body);
        using var preview = await host.PostAsync("/api/gm/v1/mail/preview", login.Csrf, payload);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var shown = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected, shown.GetProperty("input").GetProperty("body").GetString());
        Assert.Equal("官方补偿\n" + expected, shown.GetProperty("displayText").GetString());
        using var sent = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, payload);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.Equal(expected, (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receipt").GetProperty("input").GetProperty("body").GetString());
        using var replay = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(id, body: expected));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
        var mailbox = host.Services.GetRequiredService<IMailboxDomainService>();
        Assert.Equal(expected, Assert.Single(await mailbox.InboxAsync(new CharacterId(1))).Body);
        Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
    }

    [Theory]
    [InlineData("CREATE TRIGGER fail_target BEFORE INSERT ON mailbox_messages WHEN NEW.receiver_character_id=2 BEGIN SELECT RAISE(ABORT,'isolated target fault'); END")]
    [InlineData("CREATE TRIGGER fail_attachment BEFORE INSERT ON mailbox_attachments BEGIN SELECT RAISE(ABORT,'isolated attachment fault'); END")]
    [InlineData("CREATE TRIGGER fail_equipment BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT,'isolated equipment fault'); END")]
    [InlineData("CREATE TRIGGER fail_receipt BEFORE INSERT ON gm_mail_operations BEGIN SELECT RAISE(ABORT,'isolated receipt fault'); END")]
    [InlineData("CREATE TRIGGER fail_success_audit BEFORE INSERT ON gm_audit WHEN NEW.action='SendOfficialMail' AND NEW.outcome='Succeeded' BEGIN SELECT RAISE(ABORT,'isolated audit fault'); END")]
    public async Task MidBatchStorageFailureRollsBackEntireBatchAndSeparatelyRecordsFailure(string trigger)
    {
        await using var host = await GmTestHost.StartAsync(); await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host);
        using var preview = await host.PostAsync("/api/gm/v1/mail/preview", login.Csrf, Payload(Guid.NewGuid(), [1, 2], items: [new { itemId = 27312, count = 1 }]));
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        await host.ExecuteAsync(trigger);
        using var sent = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(Guid.NewGuid(), [1, 2], items: [new { itemId = 27312, count = 1 }]));
        Assert.Equal(HttpStatusCode.InternalServerError, sent.StatusCode);
        Assert.Equal("rolledBack", (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("outcome").GetString());
        foreach (var table in new[] { "mailbox_messages", "mailbox_attachments", "gm_mail_operations", "items" })
            Assert.Equal(new[] { "0" }, await host.ReadAsync("SELECT COUNT(*) FROM " + table + ";"));
        Assert.Equal(new[] { "RolledBack" }, await host.ReadAsync("SELECT outcome FROM gm_audit WHERE action='SendOfficialMail';"));
    }

    [Fact]
    public async Task AuthCsrfIndependentPermissionsAndQueuedSessionRevalidationProtectWrites()
    {
        await using var host = await GmTestHost.StartAsync(permissions: "SendMail"); await GmPlayerDirectoryHttpTests.SeedAsync(host);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.PostAsync("/api/gm/v1/mail/send", null, Payload(Guid.NewGuid()))).StatusCode);
        var login = await Login(host);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync("/api/gm/v1/mail/send", null, Payload(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/api/gm/v1/characters")).StatusCode);
        var bus = host.Services.GetRequiredService<ICommandBus>();
        var input = new GmMailInput("", "测试", [1], [], 0);
        var preparation = (GmMailPreparation)(await bus.SendAsync(new QueryGmMailCommand(login.Secret, GmMailQuery.Prepare, id: Guid.NewGuid(), input: input))).Value;
        await host.ExecuteAsync("UPDATE gm_administrators SET permissions=1;");
        Assert.Equal(GmMailRefusal.Forbidden, (await Assert.ThrowsAsync<GmMailException>(() => bus.SendAsync(new SendGmMailCommand(login.Secret, preparation)).AsTask())).Reason);
        await host.ExecuteAsync("UPDATE gm_administrators SET permissions=2; DELETE FROM gm_sessions;");
        Assert.Equal(GmMailRefusal.Unauthorized, (await Assert.ThrowsAsync<GmMailException>(() => bus.SendAsync(new SendGmMailCommand(login.Secret, preparation)).AsTask())).Reason);
        Assert.Equal(new[] { "0" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
    }

    [Fact]
    public async Task RecipientOwnerChangeAfterPrepareDoesNotUseWrongAccountLease()
    {
        await using var host = await GmTestHost.StartAsync(); await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host);
        var bus = host.Services.GetRequiredService<ICommandBus>();
        var preparation = (GmMailPreparation)(await bus.SendAsync(new QueryGmMailCommand(login.Secret, GmMailQuery.Prepare, id: Guid.NewGuid(), input: new GmMailInput("", "测试", [1], [], 0)))).Value;
        await host.ExecuteAsync("UPDATE characters SET account_id=2 WHERE character_id=1;");
        Assert.Equal(GmMailRefusal.Changed, (await Assert.ThrowsAsync<GmMailException>(() => bus.SendAsync(new SendGmMailCommand(login.Secret, preparation)).AsTask())).Reason);
        Assert.Equal(new[] { "0" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
    }

    [Fact]
    public async Task ReminderExceptionAfterCommitNeverRegrantsAndSuccessRemainsQueryable()
    {
        var notices = new ThrowingNotifications();
        await using var host = await GmTestHost.StartAsync(configureServices: services => services.AddSingleton<INotificationBus>(notices));
        await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host); var id = Guid.NewGuid();
        notices.AssertCommitted = async () => Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM gm_mail_operations;"));
        for (var i = 0; i < 2; i++)
        {
            using var sent = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(id)); Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
            Assert.Equal("unconfirmed", (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reminder").GetString());
        }
        Assert.Equal(2, notices.Count); Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/gm/v1/mail/operations/" + id)).StatusCode);
    }

    [Fact]
    public Task EquipmentStackAndAccountMaterialsAreDetachedThenClaimedThroughExistingMailbox() => VerifyMixedAttachmentClaims(false);

    [Dfo.Server.Testing.PvfFact]
    public Task ActualPvfEquipmentStackAndAccountMaterialsAreDetachedThenClaimedThroughExistingMailbox() => VerifyMixedAttachmentClaims(true);

    private static async Task VerifyMixedAttachmentClaims(bool realContent)
    {
        await using var host = await GmTestHost.StartAsync(realContent: realContent); await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host);
        await host.ExecuteAsync("INSERT INTO character_stats(character_id,carried_gold) VALUES(1,0),(2,0);");
        object[] items = [new { itemId = 27312, count = 2 }, new { itemId = 3, count = 3 }, new { itemId = 3037, count = 4 }];
        using var preview = await host.PostAsync("/api/gm/v1/mail/preview", login.Csrf, Payload(Guid.NewGuid(), items: items));
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        var requestId = Guid.NewGuid();
        using var sent = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(requestId, [1, 2], items: items));
        Assert.True(sent.IsSuccessStatusCode, await sent.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "4" }, await host.ReadAsync("SELECT COUNT(*) FROM items;"));
        Assert.Equal(new[] { "0" }, await host.ReadAsync("SELECT COUNT(*) FROM item_slots;"));
        var storedCores = await host.ReadAsync("SELECT item_uid || ':' || hex(item_core) FROM items ORDER BY item_uid;");
        var cores = storedCores.Select(x => x.Split(':')).ToDictionary(x => int.Parse(x[0], System.Globalization.CultureInfo.InvariantCulture), x => Convert.FromHexString(x[1]));
        var instanceValues = cores.Values.Select(x => BinaryPrimitives.ReadInt32LittleEndian(x.AsSpan(5, 4))).ToArray();
        Assert.All(instanceValues, value => Assert.InRange(value, 1, int.MaxValue - 1));
        Assert.Equal(4, instanceValues.Distinct().Count());
        using var replay = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(requestId, [2, 1], items: items));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
        Assert.Equal(storedCores, await host.ReadAsync("SELECT item_uid || ':' || hex(item_core) FROM items ORDER BY item_uid;"));
        var mailbox = host.Services.GetRequiredService<IMailboxDomainService>();
        var commands = host.Services.GetRequiredService<ICommandBus>();
        var completion = host.Services.GetRequiredService<INotificationHandler<MailboxCompleted>>();
        foreach (var character in new[] { new CharacterId(1), new CharacterId(2) })
        {
            // Synthetic selected sessions exercise the actual mailbox and inventory projections.
            var user = await GmPlayerDirectoryHttpTests.ConnectAsync(host, character.Value, 21011);
            var session = GmPlayerDirectoryHttpTests.Publish(host, user, character.Value, character.Value, 21011);
            var recipient = new NotificationRecipient(character, session.ConnectionId);
            var opened = await commands.SendAsync(new MailboxCommand(user.OnlineUserId, session, MailboxOperation.Open));
            var projected = Assert.Single(opened.Inbox!.Messages);
            foreach (var attachment in projected.Attachments.Where(x => x.ItemTemplateId == 27312))
            {
                var core = cores[checked((int)attachment.SourceItemUid)];
                Assert.Equal(core, attachment.ItemCore);
                Assert.Equal(BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(5, 4)), attachment.InstanceValue);
                var single = opened.Inbox with { Messages = [projected with { Attachments = [attachment], Gold = 0, GoldAttachments = [] }] };
                var messages = await completion.ComposeAsync(recipient, new MailboxCompleted(session, new MailboxOpenResponse(0), new(Inbox: single)), default);
                var body = EncodeBody(host, messages, 0x0061);
                using var reader = new BinaryReader(new MemoryStream(body));
                Assert.Equal(1, reader.ReadByte()); reader.ReadByte(); reader.ReadUInt32(); Assert.Equal(0, reader.ReadInt32());
                Assert.Empty(reader.ReadBytes(reader.ReadInt32())); Assert.Equal(0, reader.ReadInt32()); Assert.Equal(27312, reader.ReadInt32()); Assert.Equal(1, reader.ReadByte());
                Assert.Equal(attachment.InstanceValue, reader.ReadInt32());
                Assert.Equal((ushort)attachment.Durability, reader.ReadUInt16());
            }
            var mail = Assert.Single(await mailbox.InboxAsync(character));
            Assert.Equal(0, mail.SenderCharacterId); Assert.Empty(mail.SenderName);
            Assert.Equal(5, mail.Attachments.Length);
            foreach (var attachment in mail.Attachments)
            {
                var claimed = await commands.SendAsync(new MailboxCommand(user.OnlineUserId, session, MailboxOperation.Claim, attachment.Id));
                if (attachment.Asset is not MailAsset.Equipment equipment) continue;
                var update = Assert.Single(claimed.Assets!.Updates);
                Assert.Equal(equipment.Identity, update.Identity);
                var core = cores[equipment.Identity.Value];
                Assert.Equal(BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(5, 4)), update.Core.Value);
                Assert.Equal(BinaryPrimitives.ReadUInt16LittleEndian(core.AsSpan(10, 2)), update.Core.Durability);
                Assert.Equal(core[12], update.Core.SealFlag);
                var messages = await completion.ComposeAsync(recipient, new MailboxCompleted(session, new MailboxClaimResponse(0), claimed), default);
                var body = EncodeBody(host, messages, 0x000E);
                Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(1, 2)));
                Assert.Equal(27312, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(5, 4)));
                Assert.Equal(update.Core.Value, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(9, 4)));
            }
            Assert.All(Assert.Single(await mailbox.InboxAsync(character)).Attachments, x => Assert.True(x.Claimed));
            GmPlayerDirectoryHttpTests.Cleanup(host, session);
        }
        var afterClaims = await host.ReadAsync("SELECT item_uid || ':' || hex(item_core) FROM items ORDER BY item_uid;");
        using var claimedReplay = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(requestId, [1, 2], items: items));
        Assert.Equal(HttpStatusCode.OK, claimedReplay.StatusCode);
        Assert.Equal(afterClaims, await host.ReadAsync("SELECT item_uid || ':' || hex(item_core) FROM items ORDER BY item_uid;"));
        Assert.Equal(storedCores, await host.ReadAsync("SELECT item_uid || ':' || hex(item_core) FROM items WHERE item_uid IN (" + string.Join(',', cores.Keys) + ") ORDER BY item_uid;"));
        Assert.Equal(new[] { "2" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
        Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM gm_audit WHERE action='SendOfficialMail' AND outcome='Succeeded';"));
        Assert.Equal(new[] { "4" }, await host.ReadAsync("SELECT COUNT(*) FROM item_slots WHERE kind='world';"));
        Assert.Equal(new[] { "4", "4" }, await host.ReadAsync("SELECT count FROM item_slots WHERE owner_scope='account' AND owner_id IN(1,2) AND list_type=240 AND slot_index=4 AND kind='template' AND id=3037 ORDER BY owner_id;"));
        Assert.Equal(new[] { "0", "0" }, await host.ReadAsync("SELECT cube_clear FROM accounts WHERE account_id IN(1,2) ORDER BY account_id;"));
        Assert.Equal(new[] { "100", "100" }, await host.ReadAsync("SELECT carried_gold FROM character_stats WHERE character_id IN(1,2) ORDER BY character_id;"));
    }

    private static byte[] EncodeBody(GmTestHost host, IReadOnlyList<Dfo.Server.Data.Networking.IServerNetworkMessage> messages, ushort type)
    {
        var frames = messages.SelectMany(message =>
        {
            var registration = host.Services.GetServices<ServerMessageEncoderRegistration>().Single(x => x.MessageType == message.GetType());
            return ((IServerMessageEncoder)host.Services.GetRequiredService(registration.EncoderType)).Encode(message);
        });
        return Assert.Single(frames, frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)) == type)[15..];
    }

    [Fact]
    public async Task FullEquipmentBagAndGoldLimitLeaveAttachmentsPendingThenCanBeRecovered()
    {
        await using var host = await GmTestHost.StartAsync(); await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host);
        await host.ExecuteAsync("INSERT INTO character_stats(character_id,carried_gold) VALUES(1,2147483647);");
        using var sent = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(Guid.NewGuid(), items: [new { itemId = 27312, count = 1 }]));
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var mailbox = host.Services.GetRequiredService<IMailboxDomainService>(); var character = new CharacterId(1);
        var mail = Assert.Single(await mailbox.InboxAsync(character)); var gear = mail.Attachments.Single(x => x.Asset is MailAsset.Equipment); var gold = mail.Attachments.Single(x => x.Asset is MailAsset.Gold);
        var uid = ((MailAsset.Equipment)gear.Asset).Identity.Value;
        var placement = await host.Services.GetRequiredService<IItemPlacementRuleService>().PlacementForAsync(27312, default); Assert.NotNull(placement);
        for (var index = placement.Destination.FirstIndex; index <= placement.Destination.LastIndex; index++)
        {
            var coordinate = LegacyInventoryStorageCoordinates.ToStorage(new BusinessSlot(placement.Destination.Space, index));
            await host.ExecuteAsync($"INSERT INTO items(item_core) SELECT item_core FROM items WHERE item_uid={uid}; INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count) VALUES('character',1,{(int)coordinate.List},{coordinate.Index},'world',last_insert_rowid(),last_insert_rowid(),1);");
        }
        Assert.Equal(MailFailure.NoCapacity, (await Assert.ThrowsAsync<MailException>(() => mailbox.ClaimAsync(character, gear.Id).AsTask())).Reason);
        Assert.Equal(MailFailure.GoldLimit, (await Assert.ThrowsAsync<MailException>(() => mailbox.ClaimAsync(character, gold.Id).AsTask())).Reason);
        Assert.All(Assert.Single(await mailbox.InboxAsync(character)).Attachments, x => Assert.False(x.Claimed));
        await host.ExecuteAsync("DELETE FROM item_slots WHERE owner_scope='character' AND owner_id=1; UPDATE character_stats SET carried_gold=0 WHERE character_id=1;");
        await mailbox.ClaimAsync(character, gear.Id); await mailbox.ClaimAsync(character, gold.Id);
        Assert.Equal(new[] { uid.ToString(System.Globalization.CultureInfo.InvariantCulture) }, await host.ReadAsync("SELECT id FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND kind='world';"));
        Assert.Equal(new[] { "100" }, await host.ReadAsync("SELECT carried_gold FROM character_stats WHERE character_id=1;"));
    }

    [Theory]
    [InlineData("UPDATE sqlite_sequence SET seq=2147483647 WHERE name='mailbox_messages';")]
    [InlineData("UPDATE sqlite_sequence SET seq=1073741823 WHERE name='mailbox_attachments';")]
    public async Task ExhaustedWireIdentifiersRollBackWithoutCreatingUnrepresentableMail(string change)
    {
        await using var host = await GmTestHost.StartAsync(); await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host);
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(Guid.NewGuid()))).StatusCode);
        await host.ExecuteAsync(change);
        using var rejected = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.InternalServerError, rejected.StatusCode);
        Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
        Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_attachments;"));
    }

    [Fact]
    public async Task UnknownPostDurableCommitFailureReturnsUnknownAndOriginalKeyRecoversCommittedReceipt()
    {
        var poison = new PoisonCommit();
        await using var host = await GmTestHost.StartAsync(configureServices: services => services.AddSingleton<IGmAuditDomainService>(provider => new CommitFaultAudit(
            provider.GetRequiredService<IGmAdministrationPersistence>(), provider.GetRequiredService<IDomainTransactionAccessor>(), poison)));
        await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host); var id = Guid.NewGuid();
        using var unknown = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(id));
        Assert.Equal(HttpStatusCode.InternalServerError, unknown.StatusCode);
        Assert.Equal("unknown", (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("outcome").GetString());
        Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/gm/v1/mail/operations/" + id)).StatusCode);
        using var restored = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(id)); Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.True((await restored.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
        Assert.Equal(new[] { "Succeeded", "Unknown" }, await host.ReadAsync("SELECT outcome FROM gm_audit WHERE action='SendOfficialMail' ORDER BY rowid;"));
        Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
    }

    [Fact]
    public async Task SameRequestIdBelongsToEachAdministratorAndOtherReceiptIsNotExposed()
    {
        await using var host = await GmTestHost.StartAsync(); await GmPlayerDirectoryHttpTests.SeedAsync(host); var first = await Login(host); var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("/api/gm/v1/mail/send", first.Csrf, Payload(id))).StatusCode);
        await host.ExecuteAsync("INSERT INTO gm_administrators(login_name,password_record,enabled,permissions) SELECT 'other',password_record,1,2 FROM gm_administrators LIMIT 1;");
        var second = await Login(host, "other");
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/api/gm/v1/mail/operations/" + id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("/api/gm/v1/mail/send", second.Csrf, Payload(id, gold: 200))).StatusCode);
        Assert.Equal(new[] { "2" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
        Assert.Equal("other", (await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/mail/operations/" + id)).GetProperty("administratorName").GetString());
    }

    [Fact]
    public async Task DuplicateItemsMissingFieldsOversizeAndUntrustedCoreCannotBypassValidation()
    {
        await using var host = await GmTestHost.StartAsync(); await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host);
        object[] bad = [Payload(Guid.NewGuid(), items: [new { itemId = 3, count = 1 }, new { itemId = 3, count = 2 }]),
            new { requestId = Guid.NewGuid(), title = "", body = "测试", recipientIds = new[] { 1 }, items = Array.Empty<object>() },
            Payload(Guid.NewGuid(), items: [new { itemId = 27312, count = 1, uid = 7 }]), Payload(Guid.NewGuid(), body: new string('x', 33000))];
        foreach (var value in bad) Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, value)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Write(host, "PUT", "/draft", login.Csrf, Payload(Guid.NewGuid()))).StatusCode);
        Assert.Equal(new[] { "0" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 123456)]
    public async Task PeriodOrAbsoluteExpirationDeclarationIsRejectedEvenForOtherwiseValidStackable(int days, int expiration)
    {
        await using var host = await GmTestHost.StartAsync(configureServices: services => services.AddSingleton<IPackageOpeningDefinitionSource>(new ExpiringDefinitions(days, expiration)));
        await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(Guid.NewGuid(), items: [new { itemId = 3, count = 1 }]))).StatusCode);
        Assert.Equal(new[] { "0" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
    }

    [Theory]
    [InlineData(33, 1)]
    [InlineData(1, 1)]
    [InlineData(42, 1)]
    [InlineData(39032, 1)]
    [InlineData(63022, 1)]
    [InlineData(2747175, 1)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(27312, 11)]
    [InlineData(3, 0)]
    public async Task UnsupportedAttachmentsAreRejectedBeforeAnyAssetIsCreated(int itemId, int count)
    {
        await using var host = await GmTestHost.StartAsync(); await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host);
        using var sent = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(Guid.NewGuid(), items: [new { itemId, count }]));
        Assert.Equal(HttpStatusCode.BadRequest, sent.StatusCode);
        Assert.Equal(new[] { "0" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
    }

    [Fact]
    public async Task TemplateQuotaAndConcurrentRevisionDoNotOverwriteDurableContent()
    {
        await using var host = await GmTestHost.StartAsync(); var login = await Login(host);
        object Editor(string name, long revision = 0, string body = "模板正文") => new { title = "", body, recipientIds = Array.Empty<int>(), items = Array.Empty<object>(), gold = 0, name, expectedRevision = revision };
        Guid first = Guid.Empty;
        for (var index = 0; index < 20; index++)
        {
            using var created = await host.PostAsync("/api/gm/v1/mail/templates", login.Csrf, Editor("模板" + index));
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            if (index == 0) first = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await host.PostAsync("/api/gm/v1/mail/templates", login.Csrf, Editor("第21份"))).StatusCode);
        foreach (var name in new[] { " ", new string('中', 65) }) Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync("/api/gm/v1/mail/templates", login.Csrf, Editor(name))).StatusCode);
        var versions = await Task.WhenAll(Write(host, "PUT", "/templates/" + first, login.Csrf, Editor("模板0", 1, "编辑甲")), Write(host, "PUT", "/templates/" + first, login.Csrf, Editor("模板0", 1, "编辑乙")));
        Assert.Single(versions, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(versions, x => x.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in versions) response.Dispose();
        Assert.Equal(HttpStatusCode.Conflict, (await Write(host, "DELETE", "/templates/" + first, login.Csrf, Editor("模板0", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Write(host, "DELETE", "/templates/" + first, login.Csrf, Editor("模板0", 2))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("/api/gm/v1/mail/templates", login.Csrf, Editor("第21份"))).StatusCode);
        Assert.Equal(new[] { "20" }, await host.ReadAsync("SELECT COUNT(*) FROM gm_mail_templates;"));
    }

    [Fact]
    public async Task SameAccountCharactersEachReceiveTextOnlyMailAndNormalizedBodyReplays()
    {
        await using var host = await GmTestHost.StartAsync(); await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host); var id = Guid.NewGuid();
        using var sent = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(id, [47, 1, 47], body: "同文\r\n换行", title: "同文\n换行", gold: 0));
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var first = (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receipt");
        using var replay = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(id, [1, 47], body: "同文\n换行", title: "同文\r换行", gold: 0));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
        Assert.Equal(new[] { "1", "47" }, await host.ReadAsync("SELECT receiver_character_id FROM mailbox_messages ORDER BY receiver_character_id;"));
        Assert.Equal(new[] { "0" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_attachments;"));
        var mailbox = host.Services.GetRequiredService<IMailboxDomainService>();
        foreach (var character in new[] { 1, 47 }) { var mail = Assert.Single(await mailbox.InboxAsync(new CharacterId(character))); Assert.Equal("同文\n换行", mail.Body); Assert.Equal(mail.Body, mail.Title); Assert.Empty(mail.Attachments); }
        Assert.Equal(first.GetProperty("expiresAt").GetString(), (await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/mail/operations/" + id)).GetProperty("expiresAt").GetString());
        var bus = host.Services.GetRequiredService<ICommandBus>();
        var preparation = (GmMailPreparation)(await bus.SendAsync(new QueryGmMailCommand(login.Secret, GmMailQuery.Prepare, id: Guid.NewGuid(), input: new GmMailInput("", "预检", [1, 2], [], 0)))).Value;
        await host.ExecuteAsync("UPDATE characters SET delete_flag=1 WHERE character_id=2;");
        Assert.Equal(GmMailRefusal.Invalid, (await Assert.ThrowsAsync<GmMailException>(() => bus.SendAsync(new SendGmMailCommand(login.Secret, preparation)).AsTask())).Reason);
        Assert.Equal(new[] { "2" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
    }

    [Fact]
    public async Task ChangedItemOrderQuantityOrBodyConflictsWithoutAllocatingNewEquipment()
    {
        await using var host = await GmTestHost.StartAsync(); await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host); var id = Guid.NewGuid();
        object[] items = [new { itemId = 27312, count = 1 }, new { itemId = 3, count = 1 }];
        using var first = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(id, items: items)); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        foreach (var changed in new[] { Payload(id, items: items.Reverse().ToArray()), Payload(id, items: [new { itemId = 27312, count = 2 }, new { itemId = 3, count = 1 }]), Payload(id, body: "修改正文", items: items) })
            Assert.Equal(HttpStatusCode.Conflict, (await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, changed)).StatusCode);
        Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM items;"));
        Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
    }

    [Fact]
    public async Task MailEndpointsInheritActualRemoteOriginAndPlayerCredentialBoundaries()
    {
        await using (var remote = await GmTestHost.StartAsync(remoteConnection: true))
        {
            remote.Client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
            Assert.Equal(HttpStatusCode.Forbidden, (await remote.Client.GetAsync("/api/gm/v1/mail/draft")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await remote.PostAsync("/api/gm/v1/mail/send", "spoofed", Payload(Guid.NewGuid()))).StatusCode);
        }
        await using var host = await GmTestHost.StartAsync(permissions: "QueryPlayers"); await GmPlayerDirectoryHttpTests.SeedAsync(host);
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", "player-ticket");
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/gm/v1/mail/draft")).StatusCode);
        var login = await Login(host);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/api/gm/v1/mail/draft")).StatusCode);
        await host.ExecuteAsync("UPDATE gm_administrators SET permissions=2;");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync("/api/gm/v1/mail/send", "wrong-token", Payload(Guid.NewGuid()))).StatusCode);
        host.Client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(Guid.NewGuid()))).StatusCode);
        host.Client.DefaultRequestHeaders.Remove("Origin");
        using var malformed = new HttpRequestMessage(HttpMethod.Post, "/api/gm/v1/mail/send") { Content = new StringContent("{\"requestId\":\"" + Guid.NewGuid() + "\",\"title\":\"\",\"body\":\"\\uD800\",\"recipientIds\":[1],\"items\":[],\"gold\":0}", System.Text.Encoding.UTF8, "application/json") };
        malformed.Headers.Add("X-GM-CSRF", login.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.SendAsync(malformed)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(Guid.NewGuid(), body: "控制\u0001"))).StatusCode);
        Assert.Equal(new[] { "0" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
    }

    private static Task<HttpResponseMessage> Write(GmTestHost host, string method, string path, string csrf, object payload)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "/api/gm/v1/mail" + path) { Content = JsonContent.Create(payload) }; request.Headers.Add("X-GM-CSRF", csrf);
        return host.Client.SendAsync(request);
    }
    private sealed class ThrowingNotifications : INotificationBus
    {
        internal int Count { get; private set; }
        internal Func<Task>? AssertCommitted { get; set; }
        public async ValueTask<NotificationDelivery> PublishAsync<T>(NotificationAudience audience, T notification, CancellationToken token) where T : class, INotification<T>
        { Count++; if (AssertCommitted is not null) await AssertCommitted(); throw new InvalidOperationException("Isolated postcommit delivery fault"); }
    }
    private sealed class PoisonCommit : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken token) => throw new InvalidOperationException("Isolated nonfallible commit violation after SQLite commit");
        public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
    }
    private sealed class CommitFaultAudit(IGmAdministrationPersistence persistence, IDomainTransactionAccessor transactions, PoisonCommit poison) : IGmAuditDomainService
    {
        public async ValueTask AppendAsync(GmAuditRecord value, CancellationToken token)
        {
            await persistence.AppendAuditAsync(value, token);
            if (value.Action == "SendOfficialMail" && value.Outcome == "Succeeded") transactions.Current!.Enlist(poison, () => poison);
        }
        public ValueTask<GmAuditPage> QueryAsync(int page, int size, CancellationToken token) => persistence.QueryAuditAsync(page, size, token);
    }
    private sealed class ExpiringDefinitions(int days, int expiration) : IPackageOpeningDefinitionSource
    {
        public ValueTask<OpeningPackageDefinition?> GetAsync(int itemId, CancellationToken token) => ValueTask.FromResult<OpeningPackageDefinition?>(null);
        public ValueTask<OpeningRewardItemDefinition?> GetRewardAsync(int itemId, CancellationToken token) => ValueTask.FromResult<OpeningRewardItemDefinition?>(new(itemId,
            new ItemPlacement(2, 1000, new BusinessSlotRange(BusinessInventorySpace.Consumable, 0, 47)), 0, false, ImmutableArray<int>.Empty, null, true, days, expiration));
    }
}
