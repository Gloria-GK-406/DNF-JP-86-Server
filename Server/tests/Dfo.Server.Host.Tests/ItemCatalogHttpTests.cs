using System.Net;
using System.Net.Http.Json;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Initialization;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

public sealed class ItemCatalogHttpTests
{
    private sealed class Catalog : IItemCatalogSource
    {
        private static readonly ItemCatalogEntry[] Entries = [new(1,"虚拟资源","consumable",null,1,1),
            new(3,"普通消耗品","consumable",null,1,1),new(42,"限时物品","consumable",null,1,1),
            new(3033,"材料","material",null,1,1),new(27312,"武器","equipment","weapon",80,2),
            new(39032,"时装","equipment","coat avatar",1,1)];
        public ValueTask<ItemCatalogEntry?> FindAsync(int id,CancellationToken ct) => ValueTask.FromResult(Entries.SingleOrDefault(e=>e.ItemId==id));
        public ValueTask<IReadOnlyList<ItemCatalogEntry>> SearchAsync(ItemCatalogFilter filter,CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<ItemCatalogEntry>>(Entries.Where(e=>filter.ItemId is null || e.ItemId==filter.ItemId).ToArray());
    }

    private sealed class RefusingMail : IGmMailRuleService
    {
        public string SelectionKey => "mail";
        public bool Allows(ItemCatalogDefinition definition) => false;
        public GmMailInput Normalize(GmMailInput input,bool sending) => throw new InvalidOperationException("Mail must not be invoked.");
        public string DisplayText(GmMailInput input) => throw new InvalidOperationException("Mail must not be invoked.");
        public string PayloadHash(GmMailInput input) => throw new InvalidOperationException("Mail must not be invoked.");
        public ValueTask<IReadOnlyList<GmMailPreparedItem>> PrepareItemsAsync(GmMailInput input,DateTimeOffset at,CancellationToken ct) =>
            throw new InvalidOperationException("Catalog searches must not prepare mail.");
    }

    [Fact]
    public async Task Mail_restrictions_do_not_change_initialization_refill_or_unrestricted_catalog()
    {
        await using var host=await GmTestHost.StartAsync(configureServices:s=> {
            s.AddSingleton<IItemCatalogSource>(new Catalog());s.AddSingleton<IGmMailRuleService>(new RefusingMail());
        });
        var csrf=await GmCeraHttpTests.Login(host);
        var catalog=host.Services.GetRequiredService<IItemCatalogDomainService>();
        var raw=await catalog.SearchAsync(new(PageSize:50),null,default);
        Assert.Contains(raw.Items,e=>e.ItemId==42);
        Assert.Contains(raw.Items,e=>e.ItemId==39032);
        Assert.Equal(42,(await catalog.ItemAsync(42,null,default)).Item.ItemId);
        var avatar=await catalog.ItemAsync(39032,null,default);
        Assert.Equal("avatar",avatar.Kind);Assert.Null(avatar.Equipment);
        Assert.Empty((await host.Client.GetFromJsonAsync<ItemCatalogPage>("/api/gm/v1/mail/items"))!.Items);
        foreach(var purpose in new[]{"initialization","daily-refill"})
        {
            var page=(await host.Client.GetFromJsonAsync<ItemCatalogPage>($"/api/gm/v1/{purpose}/items?pageSize=1"))!;
            Assert.Equal(3,Assert.Single(page.Items).ItemId);
            Assert.Equal(HttpStatusCode.OK,(await host.Client.GetAsync($"/api/gm/v1/{purpose}/items/3")).StatusCode);
        }
        Assert.Equal(3,(await host.Services.GetRequiredService<IInitializationRuleService>().ItemAsync(3,default)).Item.ItemId);
        Assert.Equal(HttpStatusCode.OK,(await host.PostAsync("/api/gm/v1/daily-refill",csrf,new DailyRefillChange(0,"add",3,5,1))).StatusCode);
    }

    [Fact]
    public async Task Purpose_admission_precedes_pagination_and_search_matches_detail_admission()
    {
        await using var host=await GmTestHost.StartAsync(configureServices:s=>s.AddSingleton<IItemCatalogSource>(new Catalog()));
        await GmCeraHttpTests.Login(host);
        foreach(var purpose in new[]{"mail","initialization","daily-refill"})
        {
            var first=(await host.Client.GetFromJsonAsync<ItemCatalogPage>($"/api/gm/v1/{purpose}/items?pageSize=1"))!;
            Assert.Equal(3,Assert.Single(first.Items).ItemId);
            Assert.Equal(purpose!="daily-refill",first.HasMore);
            var second=(await host.Client.GetFromJsonAsync<ItemCatalogPage>($"/api/gm/v1/{purpose}/items?pageSize=1&page=2"))!;
            if (purpose=="daily-refill") Assert.Empty(second.Items);
            else Assert.Equal(purpose=="mail" ? 3033 : 27312,Assert.Single(second.Items).ItemId);
            Assert.Equal(purpose=="mail",second.HasMore);
            Assert.Equal(HttpStatusCode.BadRequest,(await host.Client.GetAsync($"/api/gm/v1/{purpose}/items/42")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await host.Client.GetAsync($"/api/gm/v1/{purpose}/items/999999")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest,(await host.Client.GetAsync("/api/gm/v1/daily-refill/items/27312")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await host.Client.GetAsync("/api/gm/v1/initialization/items/27312")).StatusCode);
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Pvf_metadata_keeps_avatars_while_current_grant_purposes_exclude_them()
    {
        await using var host = await GmTestHost.StartAsync(realContent: true);
        await GmCeraHttpTests.Login(host);
        var catalog = host.Services.GetRequiredService<IItemCatalogDomainService>();
        var avatars = await catalog.SearchAsync(new(EquipmentType: "coat avatar"), null, default);
        Assert.NotEmpty(avatars.Items);
        var id = avatars.Items[0].ItemId;
        Assert.Equal("coat avatar", (await host.Services.GetRequiredService<IItemCatalogSource>().FindAsync(id, default))!.EquipmentType);
        foreach (var purpose in new[] { "mail", "initialization", "daily-refill" })
        {
            var page = (await host.Client.GetFromJsonAsync<ItemCatalogPage>($"/api/gm/v1/{purpose}/items?itemId={id}"))!;
            Assert.Empty(page.Items);
        }
    }
}
