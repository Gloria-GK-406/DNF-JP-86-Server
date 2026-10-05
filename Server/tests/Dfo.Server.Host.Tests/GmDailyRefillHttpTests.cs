using System.Net;
using System.Net.Http.Json;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Host.Tests;

public sealed class GmDailyRefillHttpTests
{
    private const string Prefix = "/api/gm/v1/daily-refill";
    private static void Content(IServiceCollection services) => services.AddSingleton<IEntryLifecycleContentSource>(new PvfRules());
    private sealed class PvfRules : IEntryLifecycleContentSource
    {
        private static DailyRefillRule[] Rules => [new(3,2,DateTimeOffset.MaxValue,DailyRefillMode.RefillToTarget,100,2,new(BusinessInventorySpace.Consumable,0,55)),
            new(3033,3,DateTimeOffset.Parse("2000-01-01T00:00:00Z"),DailyRefillMode.Add,100,3,new(BusinessInventorySpace.Material,0,55))];
        public ValueTask<IReadOnlyList<DailyRefillRule>> GetDailyRefillRulesAsync(DateTimeOffset at, CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<DailyRefillRule>>(Rules.Where(r => r.ExpiresAt > at).ToArray());
        public ValueTask<IReadOnlyList<DailyRefillRule>> GetDeclaredDailyRefillRulesAsync(DateTimeOffset at, CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<DailyRefillRule>>(Rules);
    }
    [Fact]
    public async Task Authentication_permission_csrf_and_strict_fields_protect_management()
    {
        await using var denied = await GmTestHost.StartAsync(permissions: "QueryPlayers,SendMail,GrantCera", configureServices: Content);
        var csrf = await GmCeraHttpTests.Login(denied);
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.Client.GetAsync(Prefix)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.PostAsync(Prefix, csrf, new DailyRefillChange(0, "add", 3, 5, 1))).StatusCode);
        await using var host = await GmTestHost.StartAsync(configureServices: Content);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync(Prefix)).StatusCode);
        csrf = await GmCeraHttpTests.Login(host);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync(Prefix, null, new DailyRefillChange(0, "add", 3, 5, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync(Prefix, csrf, new { action = "add", itemId = 3, quantity = 5, mode = 1 })).StatusCode);
        foreach (var change in new[] { new DailyRefillChange(0, "add", 3, 0, 1), new(0, "add", 3, 5, 8), new(0, "add", 27312, 5, 1), new(0, "add", 999999, 5, 1), new(0, "pvf", 999999, Enabled: false), new(0, "add", 3, 5, 1, DateTimeOffset.UtcNow.AddDays(-1)) })
            Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync(Prefix, csrf, change)).StatusCode);
        Assert.Empty(await host.ReadAsync("SELECT id FROM daily_refill_settings"));
        var page = (await host.Client.GetFromJsonAsync<Dfo.Server.Data.Inventory.ItemCatalogPage>(Prefix + "/items?pageSize=50"))!;
        Assert.NotEmpty(page.Items);
        Assert.DoesNotContain(page.Items, e => e.Category == "equipment");

    }
    [Fact]
    public async Task Overrides_disable_restore_delete_and_expiry_have_one_effective_rule_and_survive_restart()
    {
        var directory = GmTestHost.NewDirectory();
        await using (var host = await GmTestHost.StartAsync(directory: directory, configureServices: Content))
        {
            var csrf = await GmCeraHttpTests.Login(host); var list = (await host.Client.GetFromJsonAsync<DailyRefillList>(Prefix))!;
            Assert.Equal(0, list.Revision); Assert.Equal("expired", list.Entries.Single(e => e.ItemId == 3033).Status);
            Assert.Equal("测试消耗品", list.Entries.Single(e => e.ItemId == 3).Name);
            Assert.Equal("测试黑色材料", list.Entries.Single(e => e.ItemId == 3033).Name);
            await host.ExecuteAsync("INSERT INTO character_daily_counters(character_id,counter_key,period,value) VALUES(90,'pvf_daily_refill_item_3','2026-10-05',1);");
            using var added = await host.PostAsync(Prefix, csrf, new DailyRefillChange(0, "add", 3, 250, 1)); Assert.Equal(HttpStatusCode.OK, added.StatusCode);
            list = (await added.Content.ReadFromJsonAsync<DailyRefillList>())!;
            Assert.Equal("overridden", list.Entries.Single(e => e.ItemId == 3 && e.Source == "pvf").Status);
            Assert.Equal("active", list.Entries.Single(e => e.Source == "custom").Status);
            var config = host.Services.GetRequiredService<IDailyRefillConfigurationDomainService>();
            var effective = Assert.Single(await config.EffectiveAsync(DateTimeOffset.UtcNow, default)); Assert.Equal(250, effective.Quantity); Assert.Equal(DailyRefillMode.Add, effective.Mode);
            Assert.Equal(HttpStatusCode.Conflict, (await host.PostAsync(Prefix, csrf, new DailyRefillChange(0, "delete", 3))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync(Prefix, csrf, new DailyRefillChange(1, "add", 3, 5, 0))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await host.PostAsync(Prefix, csrf, new DailyRefillChange(1, "update", 3, 7, 0, DateTimeOffset.UtcNow.AddHours(1)))).StatusCode);
            Assert.Empty(await config.EffectiveAsync(DateTimeOffset.UtcNow.AddHours(2), default));
            Assert.Equal(HttpStatusCode.OK, (await host.PostAsync(Prefix, csrf, new DailyRefillChange(2, "pvf", 3, Enabled: false))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await host.PostAsync(Prefix, csrf, new DailyRefillChange(3, "delete", 3))).StatusCode);
            Assert.Empty(await config.EffectiveAsync(DateTimeOffset.UtcNow, default));
            Assert.Equal(HttpStatusCode.OK, (await host.PostAsync(Prefix, csrf, new DailyRefillChange(4, "pvf", 3, Enabled: true))).StatusCode);
            Assert.Equal(2, Assert.Single(await config.EffectiveAsync(DateTimeOffset.UtcNow, default)).Quantity);
            Assert.Equal(new[] { "2026-10-05:1" }, await host.ReadAsync("SELECT period||':'||value FROM character_daily_counters WHERE character_id=90"));
            Assert.Equal(new[] { "5" }, await host.ReadAsync("SELECT COUNT(*) FROM gm_audit WHERE action='ChangeDailyRefill'"));
        }
        await using var restarted = await GmTestHost.StartAsync(directory: directory, configureServices: Content); await GmCeraHttpTests.Login(restarted);
        Assert.Equal(5, (await restarted.Client.GetFromJsonAsync<DailyRefillList>(Prefix))!.Revision);
    }
    [Fact]
    public async Task Audit_failure_cannot_leave_a_saved_rule_without_its_audit()
    {
        var fault = new GmAuditFault(); await using var host = await GmTestHost.StartAsync(fault: fault, configureServices: Content);
        var csrf = await GmCeraHttpTests.Login(host); fault.Throw = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await host.PostAsync(Prefix, csrf, new DailyRefillChange(0, "add", 3, 5, 1))).StatusCode);
        Assert.Equal(0, (await host.Client.GetFromJsonAsync<DailyRefillList>(Prefix))!.Revision);
        Assert.Empty(await host.ReadAsync("SELECT id FROM daily_refill_settings"));
    }
    private sealed class ChangedItems : IItemPlacementDefinitionSource
    {
        public bool Missing { get; set; }
        private readonly IItemPlacementDefinitionSource original = new GmMailTestContent();
        public ValueTask<ItemPlacement?> GetAsync(int id, CancellationToken ct) =>
            Missing && id == 3 ? ValueTask.FromResult<ItemPlacement?>(null) : original.GetAsync(id, ct);
    }
    private sealed class SurvivingPvf : IEntryLifecycleContentSource
    {
        public ValueTask<IReadOnlyList<DailyRefillRule>> GetDailyRefillRulesAsync(DateTimeOffset at,CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<DailyRefillRule>>([new(42,2,DateTimeOffset.MaxValue,DailyRefillMode.Add,100,2,new(BusinessInventorySpace.Consumable,0,55))]);
    }
    [Fact]
    public async Task A_changed_archive_marks_invalid_custom_items_and_keeps_other_rules_available()
    {
        var items = new ChangedItems();
        await using var host = await GmTestHost.StartAsync(configureServices:services => {
            services.AddSingleton<IEntryLifecycleContentSource>(new SurvivingPvf()); services.AddSingleton<IItemPlacementDefinitionSource>(items);
        });
        var csrf = await GmCeraHttpTests.Login(host);
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync(Prefix,csrf,new DailyRefillChange(0,"add",3,5,1))).StatusCode);
        items.Missing = true;
        var list = (await host.Client.GetFromJsonAsync<DailyRefillList>(Prefix))!;
        Assert.Equal("unsupported",list.Entries.Single(e=>e.Source=="custom" && e.ItemId==3).Status);
        Assert.Equal("物品 42",list.Entries.Single(e=>e.Source=="pvf" && e.ItemId==42).Name);
        var config = host.Services.GetRequiredService<IDailyRefillConfigurationDomainService>();
        Assert.Equal(42,Assert.Single(await config.EffectiveAsync(DateTimeOffset.UtcNow,default)).ItemId);
        Assert.Equal(1,list.Revision);
    }
    [PvfFact]
    public async Task Real_archive_lists_live_expired_and_zero_quantity_rules_and_filters_item_catalog()
    {
        await using var host = await GmTestHost.StartAsync(realContent: true, configuration: new Dictionary<string, string?> { ["Pvf:ArchivePath"] = Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH") });
        await GmCeraHttpTests.Login(host); var list = (await host.Client.GetFromJsonAsync<DailyRefillList>(Prefix))!;
        Assert.Contains(list.Entries, e => e.ItemId == 2680738 && e.Source == "pvf" && e.Status == "active" && e.Quantity == 2);
        Assert.Contains(list.Entries, e => e.ItemId == 10002520 && e.Quantity == 3);
        Assert.Contains(list.Entries, e => e.ItemId == 690001556 && e.Mode == 1);
        Assert.Contains(list.Entries, e => e.Status == "expired"); Assert.Contains(list.Entries, e => e.Status == "inactive");
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(Prefix + "/items/2680738")).StatusCode);
    }
}
