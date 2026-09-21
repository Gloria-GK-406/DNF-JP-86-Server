using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Tests.Transactions;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.RecoverStamina;
using Dfo.Server.Protocol.Game.Weakness;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class WeaknessGoldRecoveryTests
{
    [Fact]
    public async Task Concurrent_requests_charge_only_once()
    {
        using var shop = new ShopFixture();
        var state = await Weaken(shop);
        await Task.WhenAll(Recover(shop).AsTask(), Recover(shop).AsTask());
        Assert.Equal(43470, shop.Gold);
        Assert.Equal(100, state.Value);
        Assert.Equal(new byte[] { 0, 18 }, shop.Sender.Messages.OfType<RecoverStaminaResponse>().Select(item => item.ErrorCode));
    }

    [Theory]
    [InlineData(85, 32, 42711)]
    [InlineData(85, 46, 33918)]
    [InlineData(85, 82, 11306)]
    [InlineData(85, 10, 56530)]
    [InlineData(85, 100, 0)]
    [InlineData(20, 10, 5000)]
    [InlineData(19, 10, 0)]
    [InlineData(255, 10, 61780)]
    [InlineData(99, 0, 68644)]
    public void Client_price_table_matches_samples_and_boundaries(byte level, byte value, int expected) =>
        Assert.Equal(expected, new WeaknessRuleService(new ConfigurationBuilder().Build()).RecoveryGoldCost(level, value));

    [Fact]
    public async Task Paid_recovery_commits_gold_and_weakness_before_projection_and_duplicate_cannot_charge()
    {
        using var shop = new ShopFixture();
        var state = await Weaken(shop);
        Assert.Equal(56530, await state.RecoveryGoldCost());
        shop.Sender.BeforeSend = () => { Assert.Equal(43470, shop.Gold); Assert.Equal(100, state.Value); };
        await Recover(shop);
        Assert.Equal(3, shop.Sender.Messages.Count);
        Assert.Equal(0, Assert.IsType<RecoverStaminaResponse>(shop.Sender.Messages.Last()).ErrorCode);
        await Recover(shop);
        Assert.Equal(18, Assert.IsType<RecoverStaminaResponse>(shop.Sender.Messages.Last()).ErrorCode);
        Assert.Equal(43470, shop.Gold);
        Assert.Equal(0, await state.RecoveryGoldCost());
    }

    [Fact]
    public async Task Insufficient_gold_retains_weakness_and_money()
    {
        using var shop = new ShopFixture();
        var state = await Weaken(shop);
        shop.Sql("UPDATE character_stats SET carried_gold=100 WHERE character_id=1");
        await Recover(shop);
        Assert.Equal(22, Assert.IsType<RecoverStaminaResponse>(Assert.Single(shop.Sender.Messages)).ErrorCode);
        Assert.Equal(10, state.Value);
        Assert.Equal(100, shop.Gold);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public async Task Unsupported_service_flags_do_not_charge_or_grant_recovery(ushort mode)
    {
        using var shop = new ShopFixture();
        var state = await Weaken(shop);
        await Recover(shop, mode);
        Assert.Equal(4, Assert.IsType<RecoverStaminaResponse>(Assert.Single(shop.Sender.Messages)).ErrorCode);
        Assert.Equal(10, state.Value);
        Assert.Equal(100000, shop.Gold);
    }

    [Fact]
    public async Task Quote_uses_current_recovery_value_instead_of_an_old_dialog_price()
    {
        using var shop = new ShopFixture();
        var state = await Weaken(shop);
        Assert.Equal(56530, await state.RecoveryGoldCost());
        await shop.Services.GetRequiredService<ICommandBus>().SendAsync(new AdvanceWeaknessRecoveryCommand());
        Assert.Equal(19, state.Value);
        Assert.Equal(50877, await state.RecoveryGoldCost());
        await Recover(shop);
        Assert.Equal(49123, shop.Gold);
        Assert.Equal(100, state.Value);
    }

    [Fact]
    public async Task A_database_write_failure_leaves_both_gold_and_weakness_unchanged()
    {
        using var shop = new ShopFixture();
        var state = await Weaken(shop);
        shop.Sql("CREATE TRIGGER refuse_gold BEFORE UPDATE OF carried_gold ON character_stats BEGIN SELECT RAISE(ABORT,'gold failure'); END;");
        await Recover(shop);
        Assert.Equal(4, Assert.IsType<RecoverStaminaResponse>(Assert.Single(shop.Sender.Messages)).ErrorCode);
        Assert.Equal(100000, shop.Gold);
        Assert.Equal(10, state.Value);
    }

    [Fact]
    public async Task Failure_at_final_durable_commit_does_not_publish_memory_recovery()
    {
        using var shop = new ShopFixture();
        var state = await Weaken(shop);
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using var transaction = scope.Begin();
        var durable = new RecordingParticipant("database", [], true) { CommitFailure = new IOException("commit failed") };
        scope.Current!.Enlist(durable, () => durable);
        state.Recover();
        Assert.Equal(10, state.Value);
        await Assert.ThrowsAsync<IOException>(() => transaction.CommitAsync(default).AsTask());
        Assert.Equal(10, state.Value);
    }

    [Fact]
    public async Task A_replaced_session_cannot_recover_the_old_character()
    {
        using var shop = new ShopFixture();
        var state = await Weaken(shop);
        var old = shop.Session;
        shop.Select(2);
        var result = await shop.Services.GetRequiredService<ICommandBus>().SendAsync(
            new RecoverStaminaCommand(shop.OnlineUser, old, 0));
        Assert.Equal(4, result.ErrorCode);
        Assert.Equal(10, state.Value);
        Assert.Equal(100000, shop.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=1"));
    }

    private static async Task<IWeakness> Weaken(ShopFixture shop)
    {
        shop.Sql("UPDATE characters SET level=85 WHERE character_id=1");
        var state = shop.Services.GetRequiredService<IWeaknessDomainService>().For(shop.Session.CharacterId);
        await state.SetWeakness();
        Assert.Equal(10, state.Value);
        return state;
    }

    private static ValueTask Recover(ShopFixture shop, ushort mode = 0) =>
        shop.Services.GetRequiredService<RecoverStaminaHandler>().HandleAsync(new(shop.Connection),
            new RecoverStaminaRequest(GameMessageMetadata.Empty, mode), default);
}
