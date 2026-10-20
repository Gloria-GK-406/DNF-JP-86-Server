using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.UseStackable;
using Dfo.Server.Protocol.Game.ReviveCoin;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class ReviveCoinTests
{
    [Fact]
    public async Task Wire_claim_returns_one_byte_quantity_then_absolute_wallet_and_repeated_claim_refuses()
    {
        using var f = new CeraShopFixture();
        var dispatcher = f.Host.Services.GetRequiredService<INetworkMessageDispatcher>();
        await dispatcher.DispatchAsync(new(f.Host.Connection), new DailyReviveCoinRequest(GameMessageMetadata.Empty), default);
        var response = Assert.Single(f.Host.Sender.Messages.OfType<DailyReviveCoinResponse>());
        Assert.True(response.Granted);
        Assert.Equal(new byte[] { 1, 1 }, f.Encode(response).AsSpan(15).ToArray());
        Assert.Equal(1, await Balance(f));
        await dispatcher.DispatchAsync(new(f.Host.Connection), new DailyReviveCoinRequest(GameMessageMetadata.Empty), default);
        Assert.Equal(1, await Balance(f));
        Assert.False(f.Host.Sender.Messages.OfType<DailyReviveCoinResponse>().Last().Granted);
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Actual_pvf_coin_is_a_consumable_and_projects_the_wallet_after_use()
    {
        using var f = new CeraShopFixture(useArchive: true);
        await f.Host.Seed(69, 42, 2);
        var dispatcher = f.Host.Services.GetRequiredService<INetworkMessageDispatcher>();
        var body = new byte[11];
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(body, 69);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(7), 42);
        var metadata = new GameMessageMetadata(1, 0x002C, 0, 1) { InboundOrdinal = 1, SequenceIsFresh = true }.WithRawBody(body);
        await dispatcher.DispatchAsync(new(f.Host.Connection), new UseStackableRequest(metadata,
            new(InventoryContainerType.Main, 69), 0, 42), default);
        var ack = Assert.Single(f.Host.Sender.Messages.OfType<UseStackableResponse>());
        Assert.Null(ack.ErrorCode);
        Assert.Equal(1, await Balance(f));
        Assert.Equal(1, f.Host.Count(42));
        Assert.NotNull(ack.OpeningReceipt);
        Assert.Contains(ack.OpeningReceipt.Updates, x => x.Slot == new BusinessSlot(BusinessInventorySpace.VirtualResource, 1) && x.Core.Count == 1);
    }

    private static async Task<int> Balance(CeraShopFixture f) =>
        (await f.Host.Services.GetRequiredService<ICharacterContainerDomainService>().FindContainer(
            f.Host.Session.CharacterId, InventoryContainerType.Main)!.GetAsync(1, default))?.Count ?? 0;
    private static Task<DailyReviveCoinResult> Claim(CeraShopFixture f) => f.Host.Services.GetRequiredService<ICommandBus>()
        .SendAsync(new ClaimDailyReviveCoinCommand(f.Host.OnlineUser, f.Host.Session)).AsTask();

    [Fact]
    public async Task Free_claim_is_once_per_character_period_even_after_the_coin_is_spent()
    {
        using var f = new CeraShopFixture();
        Assert.True((await Claim(f)).Granted);
        Assert.Equal(1, (await Balance(f)));
        await f.Host.Seed(1, 1, 0, 0);
        Assert.False((await Claim(f)).Granted);
        Assert.Equal(0, (await Balance(f)));
        Assert.True(await f.Host.Services.GetRequiredService<IReviveCoinDomainService>().ClaimedAsync(f.Host.Session.CharacterId, default));
        f.Host.Clock.Advance(TimeSpan.FromDays(1));
        Assert.False(await f.Host.Services.GetRequiredService<IReviveCoinDomainService>().ClaimedAsync(f.Host.Session.CharacterId, default));
        Assert.True((await Claim(f)).Granted);
        Assert.Equal(1, (await Balance(f)));
    }

    [Fact]
    public async Task Nonzero_balance_does_not_spend_the_free_claim()
    {
        using var f = new CeraShopFixture();
        await f.Host.Seed(1, 1, 2, 0);
        Assert.False((await Claim(f)).Granted);
        Assert.False(await f.Host.Services.GetRequiredService<IReviveCoinDomainService>().ClaimedAsync(f.Host.Session.CharacterId, default));
        await f.Host.Seed(1, 1, 0, 0);
        Assert.True((await Claim(f)).Granted);
    }

    [Fact]
    public async Task Claim_ledger_failure_rolls_back_the_wallet_and_can_be_retried()
    {
        using var f = new CeraShopFixture();
        f.Host.Sql("CREATE TRIGGER reject_free_coin BEFORE INSERT ON character_daily_counters BEGIN SELECT RAISE(ABORT,'claim failed'); END");
        await Assert.ThrowsAsync<SqliteException>(() => Claim(f));
        Assert.Equal(0, (await Balance(f)));
        f.Host.Sql("DROP TRIGGER reject_free_coin");
        Assert.True((await Claim(f)).Granted);
        Assert.Equal(1, (await Balance(f)));
    }

    [Fact]
    public async Task Legacy_claim_counts_only_for_its_own_business_day()
    {
        using var f = new CeraShopFixture();
        f.Host.Sql("INSERT INTO character_daily_counters(character_id,counter_key,period,value) VALUES(1,'revive_coin_daily_claim','2026-09-08',1)");
        Assert.False((await Claim(f)).Granted);
        f.Host.Clock.Advance(TimeSpan.FromDays(1));
        Assert.True((await Claim(f)).Granted);
    }

    [Fact]
    public async Task Legacy_unclaimed_row_does_not_block_the_new_period_key()
    {
        using var f = new CeraShopFixture();
        f.Host.Sql("INSERT INTO character_daily_counters(character_id,counter_key,period,value) VALUES(1,'revive_coin_daily_claim','2026-09-08',0)");
        Assert.True((await Claim(f)).Granted);
        Assert.Equal(1, await Balance(f));
    }

    [Fact]
    public async Task Rapid_claims_have_exactly_one_grant_and_the_wire_request_is_registered()
    {
        using var f = new CeraShopFixture();
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Claim(f)));
        Assert.Single(attempts, x => x.Granted);
        Assert.Equal(1, (await Balance(f)));
        var dispatcher = f.Host.Services.GetRequiredService<INetworkMessageDispatcher>();
        await dispatcher.DispatchAsync(new(f.Host.Connection), new DailyReviveCoinRequest(GameMessageMetadata.Empty), default);
        Assert.Contains(f.Host.Sender.Messages, x => x is DailyReviveCoinResponse { Granted: false });
    }

    [Fact]
    public async Task Consumable_write_failure_preserves_both_the_item_and_wallet()
    {
        using var f = new CeraShopFixture(configure: s => s.AddSingleton<IItemUseRuleDefinitionSource>(
            new FakeItemUseRules((42, ItemUseRules.Potion(cooldownMilliseconds: 0)))));
        await f.Host.Seed(69, 42, 2);
        f.Host.Sql("CREATE TRIGGER reject_coin BEFORE INSERT ON item_slots WHEN NEW.slot_index=1 AND NEW.list_type=0 BEGIN SELECT RAISE(ABORT,'coin failed'); END");
        await Assert.ThrowsAsync<SqliteException>(() => f.Host.Services.GetRequiredService<ICommandBus>().SendAsync(
            new UseStackableCommand(f.Host.Session, new(BusinessInventorySpace.Consumable, 4), 42)).AsTask());
        Assert.Equal(2, f.Host.Count(42));
        Assert.Equal(0, (await Balance(f)));
    }

    [Fact]
    public async Task Coin_consumable_spends_one_item_and_adds_one_wallet_coin()
    {
        using var f = new CeraShopFixture(configure: s => s.AddSingleton<IItemUseRuleDefinitionSource>(
            new FakeItemUseRules((42, ItemUseRules.Potion(cooldownMilliseconds: 0)))));
        await f.Host.Seed(69, 42, 2);
        var before = (await Balance(f));
        var result = await f.Host.Services.GetRequiredService<ICommandBus>().SendAsync(
            new UseStackableCommand(f.Host.Session, new(BusinessInventorySpace.Consumable, 4), 42));
        Assert.Equal(ItemUseStatus.Used, result.Status);
        Assert.Equal(1, f.Host.Count(42));
        Assert.Equal(before + 1, (await Balance(f)));
    }
}
