using System.Net;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Host.Tests.PlayerPortalTestSupport;

namespace Dfo.Server.Host.Tests;

/// <summary>
/// #329 TASK-05, SD-8a: the player's mail page picks from the GM mail catalog, icons and tooltips, under the player's
/// session and the mail switch. The answers are the GM routes' own; no GM route is opened to a player.
/// </summary>
public sealed class PlayerPortalMailCatalogHttpTests
{
    private static async Task<(GmTestHost Host, PlayerClient Player)> StartAsync(string? iconPack = null)
    {
        var host = await GmTestHost.StartAsync(directory: iconPack is null ? null : Path.GetDirectoryName(iconPack),
            configuration: iconPack is null ? null : new Dictionary<string, string?> { ["GmWeb:IconPack"] = iconPack },
            configureServices: services => services.AddSingleton<IItemTooltipSource>(new Tooltips()));
        await SeedAccountAsync(host, 1, "alice"); await SetPortalAsync(host);
        await GmCeraHttpTests.Login(host);
        var player = new PlayerClient(host);
        await player.SignInAsync("alice");
        return (host, player);
    }

    /// <summary>R-3: the same search, details and refusals as the GM mail catalog, so what a player finds can be sent.</summary>
    [Fact]
    public async Task The_catalog_answers_as_the_gm_mail_catalog_does()
    {
        var (host, player) = await StartAsync();
        await using var _ = host; using var __ = player;
        foreach (var path in new[]
        {
            "mail/items", "mail/items?itemId=27312", "mail/items?itemId=3", "mail/items?itemId=42", "mail/items?itemId=1",
            "mail/items?category=material&pageSize=2", "mail/items?category=material&pageSize=2&page=2",
            "mail/items/27312", "mail/items/3", "mail/items/3037", "mail/items/42", "mail/items/1", "mail/items/2147483647",
            "mail/items?itemId=0", "mail/items?page=0", "mail/items?category=invalid", "mail/items?q=a&q=b", "mail/items?page=x",
        })
        {
            using var gm = await host.Client.GetAsync("/api/gm/v1/" + path);
            using var own = await player.Client.GetAsync("/api/player/v1/" + path);
            Assert.True(gm.StatusCode == own.StatusCode, $"{path}: GM {(int)gm.StatusCode}, player {(int)own.StatusCode}");
            if (gm.StatusCode == HttpStatusCode.OK)
                Assert.Equal(await gm.Content.ReadAsStringAsync(), await own.Content.ReadAsStringAsync());
            else
                Assert.Equal(own.StatusCode == HttpStatusCode.NotFound ? "not_found" : "invalid", await CodeAsync(own));
        }
        Assert.NotEmpty((await JsonAsync(await player.Client.GetAsync("/api/player/v1/mail/items?itemId=27312"))).GetProperty("items").EnumerateArray());
    }

    /// <summary>The icon pack's state, its icons and the tooltips are the GM ones, with the GM caching.</summary>
    [Fact]
    public async Task Icons_and_tooltips_are_the_gm_ones()
    {
        var pack = Path.Combine(GmTestHost.NewDirectory(), "gm-icons.zip");
        var (host, player) = await StartAsync(pack);
        await using var _ = host; using var __ = player;
        async Task SameAsync(string path)
        {
            using var gm = await host.Client.GetAsync("/api/gm/v1/" + path);
            using var own = await player.Client.GetAsync("/api/player/v1/" + path);
            Assert.Equal(gm.StatusCode, own.StatusCode);
            if (gm.StatusCode == HttpStatusCode.OK)
                Assert.Equal(await gm.Content.ReadAsByteArrayAsync(), await own.Content.ReadAsByteArrayAsync());
            else
                Assert.Equal("not_found", await CodeAsync(own));
        }

        await SameAsync("items/icon-pack");
        Assert.False((await JsonAsync(await player.Client.GetAsync("/api/player/v1/items/icon-pack"))).GetProperty("available").GetBoolean());
        await SameAsync("items/27312/icon");
        GmItemIconHttpTests.WritePack(pack, 1);
        await SameAsync("items/icon-pack");
        Assert.True((await JsonAsync(await player.Client.GetAsync("/api/player/v1/items/icon-pack"))).GetProperty("available").GetBoolean());
        await SameAsync("items/27312/icon");
        await SameAsync("items/3/icon");
        using (var icon = await player.Client.GetAsync("/api/player/v1/items/27312/icon"))
        {
            Assert.Equal(HttpStatusCode.OK, icon.StatusCode);
            Assert.Equal("image/png", icon.Content.Headers.ContentType?.MediaType);
            Assert.True(icon.Headers.CacheControl!.Private); Assert.Equal(TimeSpan.FromDays(1), icon.Headers.CacheControl.MaxAge);
            Assert.False(icon.Headers.CacheControl.NoStore);
        }
        await SameAsync("items/27650/tooltip");
        await SameAsync("items/1/tooltip");
    }

    /// <summary>The catalog needs the mail switch as well as the portal and the session; a GM session is no player.</summary>
    [Fact]
    public async Task Every_catalog_route_needs_the_portal_the_session_and_the_mail_switch()
    {
        var (host, player) = await StartAsync();
        await using var _ = host; using var __ = player;
        string[] paths = ["mail/items", "mail/items/27312", "items/icon-pack", "items/27312/icon", "items/27650/tooltip"];
        foreach (var path in paths)
            Assert.NotEqual(HttpStatusCode.Forbidden, (await player.Client.GetAsync("/api/player/v1/" + path)).StatusCode);

        await SetPortalAsync(host, mail: false);
        foreach (var path in paths)
        {
            using var refused = await player.Client.GetAsync("/api/player/v1/" + path);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal("feature_disabled", await CodeAsync(refused));
        }
        // The GM catalog is untouched by the player's switch.
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/gm/v1/mail/items")).StatusCode);

        using var anonymous = new PlayerClient(host);
        await SetPortalAsync(host);
        foreach (var path in paths)
        {
            using var refused = await anonymous.Client.GetAsync("/api/player/v1/" + path);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
            Assert.Equal("unauthenticated", await CodeAsync(refused));
        }
        await SetPortalAsync(host, enabled: false);
        foreach (var path in paths)
        {
            using var refused = await player.Client.GetAsync("/api/player/v1/" + path);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal("portal_closed", await CodeAsync(refused));
        }
    }

    private sealed class Tooltips : IItemTooltipSource
    {
        public ValueTask<ItemTooltip?> FindAsync(int itemId, CancellationToken cancellationToken) => ValueTask.FromResult(itemId == 27650
            ? new ItemTooltip(27650, "复仇灵魂之刀", 2, "equipment", "太刀", 20, null, ["鬼剑士（男）"], "sealing", null, false,
                [new("物理攻击力", "186 ~ 201")], null, "用复仇灵魂石铸造的刀。", 45, 2.8, 24640, null) : null);
    }
}
