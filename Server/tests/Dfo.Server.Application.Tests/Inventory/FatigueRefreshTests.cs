using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Fatigue;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;
namespace Dfo.Server.Application.Tests.Inventory;
public sealed class FatigueRefreshTests
{
    [Fact]
    public async Task Online_refresh_notifies_rollover_once_and_switching_character_uses_its_own_balance()
    {
        using var shop = new ShopFixture();
        var fatigue = shop.Services.GetRequiredService<ICharacterFatigueDomainService>();
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
        await TestGameplay.RunAsync(shop.Services, async () =>
        {
            await using (var tx = scope.Begin())
            {
                await fatigue.SpendAsync(shop.Session.CharacterId, 20, default);
                await tx.CommitAsync(default);
            }
        });
        var refresh = ActivatorUtilities.CreateInstance<FatigueDailyRefreshService>(shop.Services);
        await refresh.RefreshAsync(default);
        await refresh.RefreshAsync(default);
        Assert.Single(shop.Sender.Messages);
        Assert.Equal(ServerMessageType.GameFatigue, shop.Sender.Messages[0].MessageType);
        shop.Clock.Advance(TimeSpan.FromDays(1));
        await refresh.RefreshAsync(default);
        await refresh.RefreshAsync(default);
        Assert.Equal(2, shop.Sender.Messages.Count);
        Assert.Equal(0, (await fatigue.BalanceOfAsync(shop.Session.CharacterId, default)).Used);
        shop.Select(2);
        await refresh.RefreshAsync(default);
        Assert.Equal(3, shop.Sender.Messages.Count);
    }
}
