using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonRevivalTests
{
    private static async Task<int> Coins(ShopFixture shop) =>
        (await shop.Services.GetRequiredService<ICharacterContainerDomainService>()
            .FindContainer(shop.Session.CharacterId, InventoryContainerType.Main)!
            .GetAsync(1, default))?.Snapshot.ToCore().Value ?? 0;

    private static DungeonRunId EnterDead(ShopFixture shop)
    {
        var runs = shop.Services.GetRequiredService<IDungeonRunDomainService>();
        var layout = new DungeonLayout(144, 0, 1, 1, new(0, 0), new(0, 0), 61000, 1,
            new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(61000)});
        var run = runs.PreparePartyEntry([new(shop.Session.AccountId, shop.Session.CharacterId, 0, true)],
            layout, DungeonDifficulty.Normal).Run;
        runs.TryMarkParticipantReady(shop.Session.CharacterId, run.RunId, run.RoomVersion);
        runs.TryMarkParticipantDead(shop.Session.CharacterId, run.RunId, run.RoomVersion);
        runs.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch);
        return run.RunId;
    }

    [Theory]
    [InlineData(10)]
    [InlineData(1)]
    public async Task Revival_commits_one_coin_and_cancels_wipe_duplicate_is_refused(int coins)
    {
        using var shop = new ShopFixture();
        await shop.Seed(1, 1, coins, 0);
        var run = EnterDead(shop);
        var command = new UseRevivalCoinCommand(shop.OnlineUser, shop.Session, shop.Session.CharacterId, run);
        var bus = shop.Services.GetRequiredService<ICommandBus>();
        Assert.True((await bus.SendAsync(command)).Succeeded);
        Assert.Equal(coins - 1, (await Coins(shop)));
        Assert.Empty(shop.Services.GetRequiredService<IDungeonRunDomainService>().FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(11)));
        Assert.False((await bus.SendAsync(command)).Succeeded);
        Assert.Equal(coins - 1, (await Coins(shop)));
    }

    [Fact]
    public async Task Teammate_revival_charges_payer_and_second_death_gets_fresh_deadline()
    {
        using var shop = new ShopFixture(services => services.AddSingleton<IDungeonRunDomainService>(provider =>
            new Dfo.Server.Application.World.DungeonRunDomainService(
                provider.GetRequiredService<Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor>())));
        await shop.Seed(1, 1, 10, 0);
        var runs = shop.Services.GetRequiredService<IDungeonRunDomainService>();
        var layout = new DungeonLayout(144, 0, 1, 1, new(0, 0), new(0, 0), 61000, 1,
            new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(61000)});
        var target = new Dfo.Server.Data.Characters.CharacterId(2);
        var run = runs.PreparePartyEntry([
            new(shop.Session.AccountId, shop.Session.CharacterId, 0, true),
            new(new(2), target, 1, false)], layout, DungeonDifficulty.Normal).Run;
        foreach (var id in new[] { shop.Session.CharacterId, target })
        {
            runs.TryMarkParticipantReady(id, run.RunId, run.RoomVersion);
            runs.TryMarkParticipantDead(id, run.RunId, run.RoomVersion);
        }
        runs.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch);
        var result = await shop.Services.GetRequiredService<ICommandBus>().SendAsync(
            new UseRevivalCoinCommand(shop.OnlineUser, shop.Session, target, run.RunId));
        Assert.True(result.Succeeded);
        Assert.Equal(9, await Coins(shop));
        Assert.Empty(runs.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(11)));
        runs.TryMarkParticipantDead(target, run.RunId, run.RoomVersion);
        Assert.Empty(runs.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(12)));
        Assert.Empty(runs.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(21)));
        Assert.Equal([run.RunId], runs.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(22)));
    }

    [Fact]
    public async Task Durable_failure_preserves_coins_and_pending_wipe()
    {
        using var shop = new ShopFixture();
        await shop.Seed(1, 1, 10, 0);
        var run = EnterDead(shop);
        shop.Sql("CREATE TRIGGER refuse_coin BEFORE UPDATE OF count ON item_slots BEGIN SELECT RAISE(ABORT,'coin failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(async () => await shop.Services.GetRequiredService<ICommandBus>()
            .SendAsync(new UseRevivalCoinCommand(shop.OnlineUser, shop.Session, shop.Session.CharacterId, run)));
        Assert.Equal(10, (await Coins(shop)));
        Assert.Equal([run], shop.Services.GetRequiredService<IDungeonRunDomainService>().FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(11)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_coin_or_foreign_target_does_not_cancel_wipe(bool foreignTarget)
    {
        using var shop = new ShopFixture();
        if (foreignTarget) await shop.Seed(1, 1, 10, 0);
        var run = EnterDead(shop);
        var result = await shop.Services.GetRequiredService<ICommandBus>().SendAsync(new UseRevivalCoinCommand(
            shop.OnlineUser, shop.Session, foreignTarget ? new(999) : shop.Session.CharacterId, run));
        Assert.False(result.Succeeded);
        Assert.Equal(foreignTarget ? 10 : 0, (await Coins(shop)));
        Assert.Equal([run], shop.Services.GetRequiredService<IDungeonRunDomainService>().FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(11)));
    }
}

