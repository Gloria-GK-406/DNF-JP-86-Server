using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.LotteryOpening;
using Dfo.Server.Protocol.Game.UnansweredReports;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class LotteryOpeningTests
{
    private sealed class Fixed : ILotteryResultSelector
    {
        public int Calls;
        public OpeningReward Select(OpeningPackageDefinition definition)
        {
            Calls++;
            return definition.Pools.Single().Choices.First(x => x.Weight > 0).Reward;
        }
    }
    private static CeraShopFixture Fixture(Fixed? selector = null) => new(useArchive: true,
        configure: s => s.AddSingleton<ILotteryResultSelector>(selector ?? new Fixed()));
    private static Task<LotteryOpeningResult> Command(CeraShopFixture shop, bool start, int? slot = 65) =>
        shop.Host.Services.GetRequiredService<ICommandBus>().SendAsync(new LotteryOpeningCommand(shop.Host.OnlineUser, shop.Host.Session, slot, start)).AsTask();

    [Theory]
    [InlineData(7110, 20002)]
    [InlineData(7120, 10402)]
    [InlineData(7150, 31353)]
    public async Task Real_pot_start_freezes_one_reward_confirm_consumes_once_and_next_open_works(int pot, int reward)
    {
        var selector = new Fixed(); using var shop = Fixture(selector);
        await shop.Host.Seed(65, pot, 3);
        Assert.True((await Command(shop, true)).Started);
        Assert.True((await Command(shop, true)).Started);
        Assert.Equal(1, selector.Calls); Assert.Equal(3, shop.Host.Count(pot));
        Assert.Equal(0, shop.Host.Count(reward));
        var result = await Command(shop, false);
        Assert.NotNull(result.Receipt); Assert.Equal(reward, Assert.Single(result.Receipt.Rewards).ItemId);
        Assert.Equal(2, shop.Host.Count(pot)); Assert.Equal(1, shop.Host.Count(reward));
        Assert.Equal(100000, shop.Host.Gold);
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop, false));
        Assert.Equal(2, shop.Host.Count(pot)); Assert.Equal(1, shop.Host.Count(reward));
        await Command(shop, true);
        await Command(shop, false, null); // OVERFLOW_INFO confirmation
        Assert.Equal(1, shop.Host.Count(pot)); Assert.Equal(2, shop.Host.Count(reward));
    }

    [Fact]
    public async Task Missing_changed_expired_or_replaced_session_pending_cannot_consume()
    {
        using var shop = Fixture(); await shop.Host.Seed(65, 7110, 3);
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop, false));
        await Command(shop, true); await shop.Host.Seed(65, 7120, 3);
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop, false));
        Assert.Equal(3, shop.Host.Count(7120));
        await Command(shop, true); shop.Host.Clock.Advance(TimeSpan.FromMinutes(6));
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop, false));
        await Command(shop, true); shop.Host.Select(2);
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop, false));
    }

    [Fact]
    public async Task Failure_rolls_back_consumption_and_keeps_frozen_pending_for_retry()
    {
        var selector = new Fixed(); using var shop = Fixture(selector); await shop.Host.Seed(65, 7110, 3);
        await Command(shop, true);
        shop.Host.Sql("CREATE TRIGGER reject_reward BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT,'reward blocked'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => Command(shop, false));
        Assert.Equal(3, shop.Host.Count(7110)); Assert.Equal(0, shop.Host.Count(20002));
        shop.Host.Sql("DROP TRIGGER reject_reward");
        await Command(shop, false);
        Assert.Equal(1, selector.Calls); Assert.Equal(2, shop.Host.Count(7110));
    }

    [Fact]
    public async Task Full_equipment_band_refuses_without_consuming_pot()
    {
        using var shop = Fixture(); await shop.Host.Seed(65, 7110, 3);
        for (var slot = 9; slot <= 64; slot++) await shop.Host.Seed(slot, 20002, 1, 1);
        await Command(shop, true);
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop, false));
        Assert.Equal(3, shop.Host.Count(7110));
    }

    [Fact]
    public async Task Handler_sends_native_ack_before_absolute_inventory_and_ignores_unrelated_overflow()
    {
        using var shop = Fixture(); await shop.Host.Seed(65, 7110, 1);
        var handler = shop.Host.Services.GetRequiredService<LotteryOpeningHandler>();
        await handler.HandleAsync(new(shop.Host.Connection), new LotteryOpeningRequest(GameMessageMetadata.Empty, 0, 65), default);
        Assert.True(Assert.IsType<LotteryOpeningResponse>(Assert.Single(shop.Host.Sender.Messages)).Result!.Started);
        shop.Host.Sender.Messages.Clear();
        var overflow = shop.Host.Services.GetRequiredService<LotteryOverflowHandler>();
        await overflow.HandleAsync(new(shop.Host.Connection), new OverflowInfoReport(GameMessageMetadata.Empty, [1, 0xC1, 2]), default);
        Assert.Empty(shop.Host.Sender.Messages);
        await overflow.HandleAsync(new(shop.Host.Connection), new OverflowInfoReport(GameMessageMetadata.Empty, [1, 0x1B, 0]), default);
        Assert.IsType<LotteryOverflowResponse>(shop.Host.Sender.Messages[0]);
        var result = Assert.IsType<LotteryOpeningResponse>(shop.Host.Sender.Messages[1]);
        Assert.NotNull(result.Result!.Receipt); Assert.Equal(0, shop.Host.Count(7110));
        // Ensure the exact result is encodable; source is cleared through the shared opening projection.
        var wire = shop.Encode(result); Assert.NotNull(wire);
        var before = shop.Host.Count(20002);
        await overflow.HandleAsync(new(shop.Host.Connection), new OverflowInfoReport(GameMessageMetadata.Empty, [1, 0x1B, 0]), default);
        Assert.Null(Assert.IsType<LotteryOpeningResponse>(shop.Host.Sender.Messages.Last()).Result);
        Assert.Equal(before, shop.Host.Count(20002));
    }

    [Fact]
    public async Task Gold_cost_is_rechecked_and_rolled_back_with_the_reward()
    {
        using var shop = new CeraShopFixture(useArchive: true, configure: services =>
        {
            services.AddSingleton<ILotteryResultSelector>(new Fixed());
            var source = services.Last(x => x.ServiceType == typeof(IPackageOpeningDefinitionSource));
            services.AddSingleton<IPackageOpeningDefinitionSource>(provider => new CostSource(
                (IPackageOpeningDefinitionSource)(source.ImplementationInstance
                    ?? source.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, source.ImplementationType!))));
        });
        await shop.Host.Seed(65, 7110, 3);
        await Command(shop, true);
        shop.Host.Sql("CREATE TRIGGER reject_reward BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT,'reward blocked'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => Command(shop, false));
        Assert.Equal(100000, shop.Host.Gold);
        Assert.Equal(3, shop.Host.Count(7110));
        shop.Host.Sql("DROP TRIGGER reject_reward");
        await Command(shop, false);
        Assert.Equal(40000, shop.Host.Gold);
        Assert.Equal(2, shop.Host.Count(7110));
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop, true));
        Assert.Equal(40000, shop.Host.Gold);
    }

    private sealed class CostSource(IPackageOpeningDefinitionSource inner) : IPackageOpeningDefinitionSource
    {
        public async ValueTask<OpeningPackageDefinition?> GetAsync(int id, CancellationToken token)
        {
            var definition = await inner.GetAsync(id, token);
            return definition is null ? null : definition with { GoldCost = 60000 };
        }
        public ValueTask<OpeningRewardItemDefinition?> GetRewardAsync(int id, CancellationToken token) => inner.GetRewardAsync(id, token);
    }

    [Fact]
    public async Task Concurrent_confirmations_grant_only_once()
    {
        using var shop = Fixture(); await shop.Host.Seed(65, 7110, 3);
        await Command(shop, true);
        async Task<bool> Confirm()
        {
            try { await Command(shop, false); return true; }
            catch (OpeningRejectedException) { return false; }
        }
        var outcomes = await Task.WhenAll(Confirm(), Confirm());
        Assert.Single(outcomes, x => x);
        Assert.Equal(2, shop.Host.Count(7110));
        Assert.Equal(1, shop.Host.Count(20002));
    }

    [Fact]
    public async Task No_fixed_test_configuration_refuses_before_consumption()
    {
        using var shop = new CeraShopFixture(useArchive: true); await shop.Host.Seed(65, 7110, 1);
        await Assert.ThrowsAnyAsync<Exception>(() => Command(shop, true));
        Assert.Equal(1, shop.Host.Count(7110));
    }
}
