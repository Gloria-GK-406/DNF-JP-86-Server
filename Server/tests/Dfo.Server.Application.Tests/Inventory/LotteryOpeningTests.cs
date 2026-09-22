using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Inventory;
using System.Buffers.Binary;
using Dfo.Server.Protocol.Game.LotteryOpening;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class LotteryOpeningTests
{
    private sealed class Fixed : ILotteryResultSelector
    {
        public int Calls;
        public OpeningReward Roll(int id)
        { Calls++; return new(id switch { 7110 => 20002, 7120 => 10402, 7150 => 31353, _ => throw new InvalidOperationException() }, 1); }
    }
    private static CeraShopFixture Fixture(Fixed? selector = null)
    {
        var shop = new CeraShopFixture(useArchive: true,
            configure: s => s.AddSingleton<ILotteryResultSelector>(selector ?? new Fixed()));
        shop.Host.Services.GetRequiredService<ILotteryDefinitionCache>().InitializeAsync().GetAwaiter().GetResult();
        return shop;
    }
    private static Task<LotteryOpeningResult> Command(CeraShopFixture shop, ushort sequence = 10, ushort mode = 0, int slot = 65) =>
        shop.Host.Services.GetRequiredService<ICommandBus>().SendAsync(new LotteryOpeningCommand(shop.Host.OnlineUser, shop.Host.Session, slot, mode, sequence)).AsTask();

    [Theory]
    [InlineData(7110, 20002)]
    [InlineData(7120, 10402)]
    [InlineData(7150, 31353)]
    public async Task Ordinary_request_commits_one_reward_and_fresh_request_opens_next_pot(int pot, int reward)
    {
        var selector = new Fixed(); using var shop = Fixture(selector); await shop.Host.Seed(65, pot, 3);
        var result = await Command(shop);
        Assert.Equal(reward, Assert.Single(result.Receipt.Rewards).ItemId);
        Assert.Equal(2, shop.Host.Count(pot)); Assert.Equal(1, shop.Host.Count(reward));
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop));
        Assert.Equal(1, selector.Calls);
        await Command(shop, 11);
        Assert.Equal(1, shop.Host.Count(pot)); Assert.Equal(2, shop.Host.Count(reward));
        Assert.Equal(100000, shop.Host.Gold);
    }

    [Fact]
    public async Task Unsupported_mode_or_missing_source_does_not_consume()
    {
        using var shop = Fixture(); await shop.Host.Seed(65, 7110, 3);
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop, mode: 1));
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop, slot: 66));
        Assert.Equal(3, shop.Host.Count(7110));
    }

    [Fact]
    public async Task Failure_rolls_back_and_same_sequence_can_retry()
    {
        using var shop = Fixture(); await shop.Host.Seed(65, 7110, 3);
        shop.Host.Sql("CREATE TRIGGER reject_reward BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT,'reward blocked'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => Command(shop));
        Assert.Equal(3, shop.Host.Count(7110)); Assert.Equal(0, shop.Host.Count(20002));
        shop.Host.Sql("DROP TRIGGER reject_reward"); await Command(shop);
        Assert.Equal(2, shop.Host.Count(7110));
    }

    [Fact]
    public async Task Full_equipment_band_refuses_without_consuming_pot()
    {
        using var shop = Fixture(); await shop.Host.Seed(65, 7110, 3);
        for (var slot = 9; slot <= 64; slot++) await shop.Host.Seed(slot, 20002, 1, 1);
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop));
        Assert.Equal(3, shop.Host.Count(7110));
    }

    [Fact]
    public async Task Handler_sends_full_native_result_before_absolute_inventory()
    {
        using var shop = Fixture(); await shop.Host.Seed(65, 7110, 1);
        var handler = shop.Host.Services.GetRequiredService<LotteryOpeningHandler>();
        await handler.HandleAsync(new(shop.Host.Connection), new LotteryOpeningRequest(GameMessageMetadata.Empty, 0, 65), default);
        var result = Assert.IsType<LotteryOpeningResponse>(shop.Host.Sender.Messages[0]);
        Assert.NotNull(result.Result); Assert.Equal(0, shop.Host.Count(7110));
        var frame = shop.Encode(result);
        Assert.Equal(77, frame.Length); // 15-byte frame + 62-byte A21 success body.
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(15 + 37)));
        Assert.Equal(8, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(15 + 45)));
        Assert.All(frame.Skip(15 + 57), value => Assert.Equal((byte)0, value));
        await handler.HandleAsync(new(shop.Host.Connection), new LotteryOpeningRequest(GameMessageMetadata.Empty, 0, 65), default);
        Assert.Null(Assert.IsType<LotteryOpeningResponse>(shop.Host.Sender.Messages.Last()).Result);
        Assert.Equal(1, shop.Host.Count(20002));
    }

    [Fact]
    public async Task Concurrent_same_sequence_grants_only_once()
    {
        using var shop = Fixture(); await shop.Host.Seed(65, 7110, 3);
        async Task<bool> Open()
        { try { await Command(shop); return true; } catch (OpeningRejectedException) { return false; } }
        Assert.Single(await Task.WhenAll(Open(), Open()), x => x);
        Assert.Equal(2, shop.Host.Count(7110)); Assert.Equal(1, shop.Host.Count(20002));
    }

    [Fact]
    public async Task Gold_and_pot_roll_back_together_and_insufficient_gold_is_rejected()
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
        await shop.Host.Services.GetRequiredService<ILotteryDefinitionCache>().InitializeAsync();
        await shop.Host.Seed(65, 7110, 3);
        shop.Host.Sql("CREATE TRIGGER reject_reward BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT,'reward blocked'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => Command(shop));
        Assert.Equal(100000, shop.Host.Gold); Assert.Equal(3, shop.Host.Count(7110));
        shop.Host.Sql("DROP TRIGGER reject_reward"); await Command(shop);
        Assert.Equal(40000, shop.Host.Gold); Assert.Equal(2, shop.Host.Count(7110));
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop, 11));
        Assert.Equal(40000, shop.Host.Gold);
    }

    [Fact]
    public async Task Source_id_outside_pot_cache_throws_without_consumption()
    {
        using var shop = Fixture();
        await shop.Host.Seed(65, 201, 3);
        var error = await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop));
        Assert.Contains("201", error.Message);
        Assert.Contains("cache", error.Message);
        Assert.Equal(3, shop.Host.Count(201));
        Assert.Equal(100000, shop.Host.Gold);
    }

    [Fact]
    public async Task Production_selector_opens_without_fixed_configuration()
    {
        using var shop = new CeraShopFixture(useArchive: true); await shop.Host.Seed(65, 7110, 1);
        await shop.Host.Services.GetRequiredService<ILotteryDefinitionCache>().InitializeAsync();
        var result = await Command(shop);
        var reward = Assert.Single(result.Receipt.Rewards);
        Assert.Contains(shop.Host.Services.GetRequiredService<ILotteryDefinitionCache>().GetRequired(7110).Pools[0].Choices,
            row => row.Weight > 0 && row.Reward.ItemId == reward.ItemId);
        Assert.Equal(0, shop.Host.Count(7110));
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

}
