using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

public sealed class GmItemTooltipHttpTests
{
    [Fact]
    public async Task TooltipReturnsTheStaticContractToAnyAdministratorAndUnknownItemsAre404()
    {
        await using var host = await GmTestHost.StartAsync(permissions: "QueryPlayers",
            configureServices: services => services.AddSingleton<IItemTooltipSource>(new Tooltips()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/gm/v1/items/27650/tooltip")).StatusCode);
        using var login = await host.LoginAsync(await host.CsrfAsync());
        var tooltip = await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/items/27650/tooltip");
        Assert.Equal(["itemId", "name", "rarity", "category", "typeName", "minimumLevel", "maximumLevel", "jobs", "attach", "periodDays",
            "dailyDelete", "stats", "explain", "flavor", "durability", "weight", "price", "stackLimit"], tooltip.EnumerateObject().Select(p => p.Name));
        Assert.Equal("太刀", tooltip.GetProperty("typeName").GetString()); Assert.Equal(2.8, tooltip.GetProperty("weight").GetDouble());
        Assert.Equal(JsonValueKind.Null, tooltip.GetProperty("maximumLevel").ValueKind);
        var stat = tooltip.GetProperty("stats")[0];
        Assert.Equal(("物理攻击力", "186 ~ 201"), (stat.GetProperty("label").GetString(), stat.GetProperty("value").GetString()));
        using var missing = await host.Client.GetAsync("/api/gm/v1/items/1/tooltip");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.False(string.IsNullOrEmpty((await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()));
    }

    private sealed class Tooltips : IItemTooltipSource
    {
        public ValueTask<ItemTooltip?> FindAsync(int itemId, CancellationToken cancellationToken) => ValueTask.FromResult(itemId == 27650
            ? new ItemTooltip(27650, "复仇灵魂之刀", 2, "equipment", "太刀", 20, null, ["鬼剑士（男）"], "sealing", null, false,
                [new("物理攻击力", "186 ~ 201")], null, "用复仇灵魂石铸造的刀。", 45, 2.8, 24640, null) : null);
    }
}
