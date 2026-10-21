using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class PremiumServiceUsageReportTests
{
    [Theory]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(4, false)]
    public async Task Reports_refresh_authoritative_entitlements_without_spending_or_creating_daily_usage(ushort slot, bool subscribed)
    {
        using var shop = new ShopFixture();
        if (subscribed)
            shop.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,584,4000000000)");
        var commands = shop.Services.GetRequiredService<ICommandBus>();
        var before = await commands.SendAsync(new ReadPremiumSnapshotCommand(shop.Session));
        await TestGameplay.RunAsync(shop.Services, async () =>
        {
            var handler = shop.Services.GetRequiredService<PremiumServiceUsageReportHandler>();
            for (var attempt = 0; attempt < 2; attempt++)
                await handler.HandleAsync(new(shop.Connection), new PremiumServiceUsageReport(GameMessageMetadata.Empty, slot), default);
        });
        Assert.Equal(2, shop.Sender.Messages.Count);
        Assert.All(shop.Sender.Messages, message => Assert.Equal("PremiumServiceStateMessage", message.GetType().Name));
        var after = await commands.SendAsync(new ReadPremiumSnapshotCommand(shop.Session));
        Assert.Equal(before.Usage, after.Usage);
        Assert.DoesNotContain(after.Usage, item => item.SlotIndex == 4 && item.UsedCount != 0);
        Assert.Equal(0, shop.Scalar("SELECT COUNT(*) FROM character_daily_counters"));
        Assert.Equal(subscribed ? 1 : 0, shop.Scalar("SELECT COUNT(*) FROM account_premiums"));
        Assert.Equal(100000, shop.Gold);
    }
}
