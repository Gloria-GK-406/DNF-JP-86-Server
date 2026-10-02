using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Mail;
using Dfo.Server.Protocol.Game.Mailbox;
using Microsoft.Extensions.DependencyInjection;
using System.Buffers.Binary;

namespace Dfo.Server.Host.Tests;

public sealed partial class GmOfficialMailHttpTests
{
    [Fact]
    public async Task SameEquipmentWithDifferentConfigurationsKeepsEachStateAndReplaysWithoutNewItems()
    {
        await using var host = await GmTestHost.StartAsync();
        await GmPlayerDirectoryHttpTests.SeedAsync(host);
        var login = await Login(host);
        object[] items = [
            new { itemId = 27312, count = 1, equipment = new { seal = "unsealed", growth = "reinforcement", level = 7, amplifyType = 0 } },
            new { itemId = 27312, count = 2, equipment = new { seal = "default", growth = "reinforcement", level = 10, amplifyType = 0 } }];
        var id = Guid.NewGuid();
        using var preview = await host.PostAsync("/api/gm/v1/mail/preview", login.Csrf, Payload(id, [1, 2], items: items));
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        using var sent = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(id, [1, 2], items: items));
        Assert.True(sent.IsSuccessStatusCode, await sent.Content.ReadAsStringAsync());
        var cores = await host.ReadAsync("SELECT hex(item_core) FROM items ORDER BY item_uid;");
        Assert.Equal(6, cores.Count);
        Assert.Equal(new byte[] { 7, 10, 10, 7, 10, 10 }, cores.Select(x => (byte)(Convert.FromHexString(x)[9] & 31)));
        Assert.Equal(0, Convert.FromHexString(cores[0])[12]);
        using var replay = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(id, [1, 2], items: items));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
        Assert.Equal(cores, await host.ReadAsync("SELECT hex(item_core) FROM items ORDER BY item_uid;"));
        object[] changed = [new { itemId = 27312, count = 1, equipment = new { seal = "unsealed", growth = "reinforcement", level = 8, amplifyType = 0 } }, items[1]];
        Assert.Equal(HttpStatusCode.Conflict, (await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(id, [1, 2], items: changed))).StatusCode);
        Assert.Equal(new[] { "6" }, await host.ReadAsync("SELECT COUNT(DISTINCT item_uid) FROM items;"));
    }

    [Fact]
    public Task ConfiguredJewelryWithoutDurabilitySurvivesDraftTemplatePreviewClaimAndRestart() => VerifyConfiguredJewelry(false);

    [Dfo.Server.Testing.PvfFact]
    public Task ActualPvfConfiguredJewelrySurvivesClaimAndRestart() => VerifyConfiguredJewelry(true);

    private static async Task VerifyConfiguredJewelry(bool realContent)
    {
        var directory = GmTestHost.NewDirectory();
        var requestId = Guid.NewGuid();
        object[] items = [new { itemId = 100310498, count = 1, equipment = new { seal = "unsealed", growth = "amplification", level = 10, amplifyType = 3 } }];
        string core;
        await using (var host = await GmTestHost.StartAsync(directory: directory, realContent: realContent))
        {
            await GmPlayerDirectoryHttpTests.SeedAsync(host);
            var login = await Login(host);
            var input = new { title = "装备", body = "领取并保存", recipientIds = new[] { 1 }, items, gold = 0, expectedRevision = 0, name = "增幅模板" };
            Assert.Equal(HttpStatusCode.OK, (await Write(host, "PUT", "/draft", login.Csrf, input)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("/api/gm/v1/mail/templates", login.Csrf, input)).StatusCode);
            using var preview = await host.PostAsync("/api/gm/v1/mail/preview", login.Csrf, input);
            Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
            var item = (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
            Assert.Equal(10, item.GetProperty("upgradeLevel").GetInt32());
            Assert.Equal(3, item.GetProperty("amplifyType").GetInt32());
            Assert.False(item.GetProperty("sealed").GetBoolean());
            var definition = await host.Services.GetRequiredService<IEquipmentDefinitionSource>().GetEquipmentAsync(100310498, default);
            Assert.Null(definition!.MaxDurability);
            var table = await host.Services.GetRequiredService<IEquipmentOutworldContentSource>().GetAsync(default);
            var expectedValue = table!.InitialValues[definition.Rarity][3];
            Assert.Equal(expectedValue, item.GetProperty("amplifyValue").GetUInt16());
            using var sent = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf,
                Payload(requestId, body: "领取并保存", title: "装备", gold: 0, items: items));
            Assert.True(sent.IsSuccessStatusCode, await sent.Content.ReadAsStringAsync());
            var receipt = (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receipt");
            Assert.Equal(item.GetProperty("name").GetString(), receipt.GetProperty("items")[0].GetProperty("name").GetString());
            Assert.Equal(expectedValue, receipt.GetProperty("items")[0].GetProperty("amplifyValue").GetUInt16());
            core = Assert.Single(await host.ReadAsync("SELECT hex(item_core) FROM items;"));
            var stored = Convert.FromHexString(core);
            Assert.Equal(10, stored[9] & 31); Assert.Equal(0, stored[12]);
            Assert.Equal(3, stored[18]); Assert.Equal(expectedValue, BinaryPrimitives.ReadUInt16LittleEndian(stored.AsSpan(19, 2)));
            var user = await GmPlayerDirectoryHttpTests.ConnectAsync(host, 1, 21011);
            var session = GmPlayerDirectoryHttpTests.Publish(host, user, 1, 1, 21011);
            var mailbox = host.Services.GetRequiredService<IMailboxDomainService>();
            var message = Assert.Single(await mailbox.InboxAsync(new CharacterId(1)));
            var attachment = Assert.Single(message.Attachments);
            var claimed = await host.Services.GetRequiredService<ICommandBus>().SendAsync(new MailboxCommand(user.OnlineUserId, session, MailboxOperation.Claim, attachment.Id));
            var update = Assert.Single(claimed.Assets!.Updates);
            Assert.Equal((byte)10, update.Core.ReinforcementLevel);
            Assert.Equal((byte)3, update.Core.AmplifyType); Assert.Equal(expectedValue, update.Core.AmplifyValue);
            Assert.Equal(core, Assert.Single(await host.ReadAsync("SELECT hex(item_core) FROM items;")));
            GmPlayerDirectoryHttpTests.Cleanup(host, session);
        }
        await using (var host = await GmTestHost.StartAsync(directory: directory, realContent: realContent))
        {
            var login = await Login(host);
            var draft = await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/mail/draft");
            var templates = await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/mail/templates");
            foreach (var saved in new[] { draft, templates[0] })
                Assert.Equal(10, saved.GetProperty("input").GetProperty("items")[0].GetProperty("equipment").GetProperty("level").GetInt32());
            Assert.Equal(core, Assert.Single(await host.ReadAsync("SELECT hex(item_core) FROM items;")));
            Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM item_slots WHERE kind='world';"));
            using var replay = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(requestId, body: "领取并保存", title: "装备", gold: 0, items: items));
            Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
            Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
        }
    }

    [Fact]
    public async Task OriginalMailJsonHashAndSuccessfulRequestRemainCompatible()
    {
        var directory = GmTestHost.NewDirectory(); var id = Guid.NewGuid();
        var oldInput = new { Title = "官方补偿", Body = "测试金币", RecipientIds = new[] { 1 }, Items = new[] { new { ItemId = 27312, Count = 1 } }, Gold = 100 };
        var oldHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(oldInput)));
        await using (var host = await GmTestHost.StartAsync(directory: directory))
        {
            await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host);
            using var sent = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(id, items: [new { itemId = 27312, count = 1 }]));
            Assert.True(sent.IsSuccessStatusCode, await sent.Content.ReadAsStringAsync());
            Assert.Equal(oldHash, Assert.Single(await host.ReadAsync("SELECT payload_hash FROM gm_mail_operations;")));
            Assert.DoesNotContain("Equipment", Assert.Single(await host.ReadAsync("SELECT json_extract(receipt,'$.Input') FROM gm_mail_operations;")));
            await host.ExecuteAsync("UPDATE gm_mail_operations SET receipt=json_remove(receipt,'$.Items');");
        }
        await using (var host = await GmTestHost.StartAsync(directory: directory))
        {
            var login = await Login(host);
            // The extension's explicit default state has exactly the same meaning as legacy omission.
            using var replay = await host.PostAsync("/api/gm/v1/mail/send", login.Csrf, Payload(id,
                items: [new { itemId = 27312, count = 1, equipment = new { seal = "default", growth = "none", level = 0, amplifyType = 0 } }]));
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
            Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM items;"));
        }
    }

    [Fact]
    public async Task InvalidEquipmentCombinationsAndExpandedLimitCannotCreateAssets()
    {
        await using var host = await GmTestHost.StartAsync(); await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host);
        object[] options = [
            new { seal = "sealed", growth = "none", level = 0, amplifyType = 0 },
            new { seal = "default", growth = "reinforcement", level = 32, amplifyType = 0 },
            new { seal = "default", growth = "amplification", level = 10, amplifyType = 0 },
            new { seal = "default", growth = "none", level = 10, amplifyType = 0 },
            new { seal = "default", growth = "reinforcement", level = 10, amplifyType = 3 },
            new { seal = "default", growth = "amplification", level = 10, amplifyType = 3, amplifyValue = 65535 }];
        foreach (var equipment in options)
            Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync("/api/gm/v1/mail/send", login.Csrf,
                Payload(Guid.NewGuid(), items: [new { itemId = 100310498, count = 1, equipment }]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync("/api/gm/v1/mail/send", login.Csrf,
            Payload(Guid.NewGuid(), items: [new { itemId = 3, count = 1, equipment = new { seal = "unsealed" } }]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync("/api/gm/v1/mail/send", login.Csrf,
            Payload(Guid.NewGuid(), items: [new { itemId = 27312, count = 10 }, new { itemId = 27312, count = 1, equipment = new { seal = "unsealed" } }]))).StatusCode);
        Assert.Equal(new[] { "0" }, await host.ReadAsync("SELECT COUNT(*) FROM items;"));
        Assert.Equal(new[] { "0" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
    }

    [Fact]
    public Task ItemCatalogFiltersAndPagesOnlyMailableItemsAndChecksPermission() => VerifyItemCatalog(false);

    [Dfo.Server.Testing.PvfFact]
    public Task ActualPvfItemCatalogFiltersAndPagesOnlyMailableItems() => VerifyItemCatalog(true);

    private static async Task VerifyItemCatalog(bool realContent)
    {
        await using var host = await GmTestHost.StartAsync(permissions: "SendMail", realContent: realContent);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/gm/v1/mail/items?itemId=3")).StatusCode);
        await Login(host);
        var single = await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/mail/items?itemId=3037&category=material");
        Assert.Single(single.GetProperty("items").EnumerateArray());
        Assert.Equal(3037, single.GetProperty("items")[0].GetProperty("itemId").GetInt32());
        var details = await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/mail/items/100310498");
        Assert.True(details.GetProperty("equipment").GetProperty("canAmplify").GetBoolean());
        Assert.True(details.GetProperty("equipment").GetProperty("maxAmplificationLevel").GetInt32() >= 10);
        Assert.False(string.IsNullOrWhiteSpace(details.GetProperty("item").GetProperty("name").GetString()));
        foreach (var suffix in new[] { "itemId=0", "page=0", "category=invalid", "minimumLevel=10&maximumLevel=2", "rarity=7", "q=a&q=b" })
            Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/gm/v1/mail/items?" + suffix)).StatusCode);
        Assert.Empty((await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/mail/items?itemId=2147483647")).GetProperty("items").EnumerateArray());
        var first = await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/mail/items?category=material&pageSize=2");
        var second = await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/mail/items?category=material&pageSize=2&page=2");
        Assert.True(first.GetProperty("hasMore").GetBoolean());
        var ids = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).Select(x => x.GetProperty("itemId").GetInt32()).ToArray();
        Assert.Equal(4, ids.Length); Assert.Equal(4, ids.Distinct().Count());
        var name = first.GetProperty("items")[0].GetProperty("name").GetString()!;
        var searched = await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/mail/items?category=material&q=" + Uri.EscapeDataString(name));
        Assert.NotEmpty(searched.GetProperty("items").EnumerateArray());
        Assert.All(searched.GetProperty("items").EnumerateArray(), x => Assert.Contains(name, x.GetProperty("name").GetString()!));
        await host.ExecuteAsync("UPDATE gm_administrators SET permissions=1;");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/api/gm/v1/mail/items?itemId=3")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/api/gm/v1/mail/items/3")).StatusCode);
    }

    [Fact]
    public async Task UnreadableGrowthRulesDisableConfigurationWithoutBlockingDefaultEquipment()
    {
        await using var host = await GmTestHost.StartAsync(configureServices: services => services.AddSingleton<IEquipmentDefinitionSource>(new UnreadableGrowthDefinitions()));
        await GmPlayerDirectoryHttpTests.SeedAsync(host); var login = await Login(host);
        var details = await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/mail/items/27312");
        Assert.False(details.GetProperty("equipment").GetProperty("canAmplify").GetBoolean());
        Assert.Equal(0, details.GetProperty("equipment").GetProperty("maxReinforcementLevel").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("/api/gm/v1/mail/send", login.Csrf,
            Payload(Guid.NewGuid(), items: [new { itemId = 27312, count = 1, equipment = new { seal = "unsealed" } }]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync("/api/gm/v1/mail/send", login.Csrf,
            Payload(Guid.NewGuid(), items: [new { itemId = 27312, count = 1, equipment = new { growth = "reinforcement", level = 7 } }]))).StatusCode);
        Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM items;"));
    }

    private sealed class UnreadableGrowthDefinitions : IEquipmentDefinitionSource
    {
        public ValueTask<int> InitializeAsync(CancellationToken token) => ValueTask.FromResult(1);
        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int itemId, CancellationToken token) => throw new InvalidDataException("Isolated unsupported growth metadata.");
    }
}
