using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentGrowthTests
{
    // The fixture clock is 2026-09-08 12:00 UTC, game day 2026-09-08 (06:00 UTC+8 reset).
    private static readonly DateTimeOffset Today = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Kill_pays_point_times_rank_and_difficulty_rates()
    {
        using var shop = await Worn(3002, experience: 0);
        var ordinary = await Kill(shop, MonsterRank.Ordinary, DungeonDifficulty.Normal);
        Assert.Equal(new EquipmentGrowthChange(Weapon, 3002, EquipmentGrowthChangeKind.Gained, 5, 5, 0), ordinary);
        var champion = await Kill(shop, MonsterRank.SuperChampion, DungeonDifficulty.Adventure);
        Assert.Equal((ushort)22, champion!.Gained); // 5 × 300% × 150%
        Assert.Equal((27, 0), await Growth(shop));
    }

    [Fact]
    public async Task Valley_of_the_dead_and_unworn_weapons_do_not_grow()
    {
        using var shop = await Worn(3002, experience: 3);
        Assert.Null(await Kill(shop, MonsterRank.Boss, DungeonDifficulty.Normal, DungeonRunKind.Sao));
        Assert.Equal((3, 0), await Growth(shop));
        using var bag = new ShopFixture(Content);
        await bag.SeedCore(9, StackableItemCore.Create(1, 3002, 1234));
        await Settled(bag);
        Assert.Null(await Kill(bag, MonsterRank.Boss, DungeonDifficulty.Normal));
    }

    [Fact]
    public async Task A_filled_automatic_star_becomes_the_next_template_with_the_same_uid()
    {
        using var shop = await Worn(3000, experience: 8);
        var uid = await Uid(shop);
        var change = await Kill(shop, MonsterRank.Ordinary, DungeonDifficulty.Normal);
        Assert.Equal(new EquipmentGrowthChange(Weapon, 3001, EquipmentGrowthChangeKind.Upgraded, 2, 0, 0), change);
        Assert.Equal(3001, BitConverter.ToInt32(shop.StoredCore(12, InventoryContainerType.Equipment), 1));
        Assert.Equal(uid, await Uid(shop));
        Assert.Equal((0, 0), await Growth(shop));
    }

    [Fact]
    public async Task A_scroll_star_stops_at_full_and_the_last_star_never_grows()
    {
        using var shop = await Worn(3002, experience: 28);
        var change = await Kill(shop, MonsterRank.Boss, DungeonDifficulty.Normal);
        Assert.Equal(new EquipmentGrowthChange(Weapon, 3002, EquipmentGrowthChangeKind.Gained, 2, 30, 0), change);
        Assert.Null(await Kill(shop, MonsterRank.Boss, DungeonDifficulty.Normal));
        Assert.Equal(3002, BitConverter.ToInt32(shop.StoredCore(12, InventoryContainerType.Equipment), 1));
        using var last = await Worn(3003, experience: 0);
        Assert.Null(await Kill(last, MonsterRank.Boss, DungeonDifficulty.Normal));
    }

    [Fact]
    public async Task Maintained_fatigue_shows_todays_consumption_capped_at_the_threshold()
    {
        using var shop = await Worn(3002, experience: 0);
        await Fatigue(shop, 45, Today);
        var change = await Kill(shop, MonsterRank.Ordinary, DungeonDifficulty.Normal);
        Assert.Equal((byte)30, change!.MaintainedFatigue);
        Assert.Equal((5, 30), await Growth(shop));
    }

    [Fact]
    public async Task Days_short_of_fatigue_cost_a_fifth_each_and_a_maintained_day_costs_nothing()
    {
        using var away = await Worn(3002, experience: 25, settledDay: "2026-09-06");
        var change = await Settle(away);
        // 09-06 had no fatigue on record (one penalty) and 09-07 passed entirely (another): 25 → 20 → 16.
        Assert.Equal(new EquipmentGrowthChange(Weapon, 3002, EquipmentGrowthChangeKind.Reset, 0, 16, 0), change);
        Assert.Null(await Settle(away));

        using var kept = await Worn(3002, experience: 25, settledDay: "2026-09-07");
        await Fatigue(kept, 30, new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        Assert.Null(await Settle(kept));
        Assert.Equal((25, 0), await Growth(kept));
    }

    [Fact]
    public async Task The_first_settlement_only_records_the_day()
    {
        using var shop = await Worn(3002, experience: 25, settledDay: null);
        Assert.Null(await Settle(shop));
        Assert.Equal("2026-09-08", await Day(shop));
        Assert.Equal((25, 0), await Growth(shop));
    }

    private static readonly BusinessSlot Weapon = new(BusinessInventorySpace.WornEquipment, 0);

    private static void Content(IServiceCollection services)
    {
        services.AddSingleton<IEquipmentEmancipateDefinitionSource>(new Emancipation());
        services.AddSingleton<IEquipmentGrowthDefinitionSource>(new GrowthTable());
        services.AddSingleton<IEquipmentSlotDefinitionSource>(new WeaponSlot());
    }

    private sealed class WeaponSlot : IEquipmentSlotDefinitionSource
    {
        public ValueTask<BusinessSlot?> GetAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<BusinessSlot?>(itemId is >= 3000 and <= 3003 ? Weapon : null);
    }

    private static async Task<ShopFixture> Worn(int template, ushort experience, string? settledDay = "2026-09-08")
    {
        var shop = new ShopFixture(Content);
        await shop.SeedCore(12, StackableItemCore.Create(1, template, 1234) with { GrowthExperience = experience },
            InventoryContainerType.Equipment);
        if (settledDay is not null) await InTransaction(shop, async (s, _) =>
        {
            await s.GetRequiredService<ICharacterDailyCounterTable>().ClaimAsync(shop.Session.CharacterId,
                EquipmentGrowthDomainService.SettledDayKey, settledDay, default);
            return 0;
        });
        return shop;
    }

    private static Task Settled(ShopFixture shop) => InTransaction(shop, async (s, _) =>
        await s.GetRequiredService<ICharacterDailyCounterTable>().ClaimAsync(shop.Session.CharacterId,
            EquipmentGrowthDomainService.SettledDayKey, "2026-09-08", default));

    private static Task<EquipmentGrowthChange?> Kill(ShopFixture shop, MonsterRank rank, DungeonDifficulty difficulty,
        DungeonRunKind kind = DungeonRunKind.Ordinary) => InTransaction(shop, (s, growth) =>
        growth.GainFromKillAsync(shop.Session.CharacterId, rank, difficulty, kind, Today, default).AsTask());

    private static Task<EquipmentGrowthChange?> Settle(ShopFixture shop) => InTransaction(shop, (s, growth) =>
        growth.SettleDaysAsync(shop.Session.CharacterId, Today, default).AsTask());

    private static Task Fatigue(ShopFixture shop, int consumed, DateTimeOffset at) => InTransaction(shop, async (s, _) =>
    {
        var table = s.GetRequiredService<ICharacterFatigueTable>();
        return await table.TryWriteAsync(shop.Session.CharacterId, await table.ReadAsync(shop.Session.CharacterId, default),
            new(consumed, consumed, at), default);
    });

    private static Task<string?> Day(ShopFixture shop) => InTransaction(shop, (s, _) =>
        s.GetRequiredService<ICharacterDailyCounterTable>().FindClaimedPeriodAsync(shop.Session.CharacterId,
            EquipmentGrowthDomainService.SettledDayKey, default).AsTask());

    private static async Task<(int Experience, int Fatigue)> Growth(ShopFixture shop)
    {
        var core = shop.StoredCore(12, InventoryContainerType.Equipment);
        return (BitConverter.ToUInt16(core, 74), core[76]);
    }

    private static Task<int?> Uid(ShopFixture shop) => InTransaction(shop, async (s, _) =>
        (await s.GetRequiredService<IItemDomainStore>().SessionIn(s.GetRequiredService<IDomainTransactionAccessor>().Current!)
            .GetAsync(ContainerId.ForCharacter(shop.Session.CharacterId, InventoryContainerType.Equipment), 12, default))?.ItemUid?.Value);

    private static Task<T> InTransaction<T>(ShopFixture shop, Func<IServiceProvider, IEquipmentGrowthDomainService, Task<T>> action) =>
        TestGameplay.RunAsync(shop.Services, async () =>
        {
            await using var tx = ((DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
            var result = await action(shop.Services, shop.Services.GetRequiredService<IEquipmentGrowthDomainService>());
            await tx.CommitAsync(default);
            return result;
        });

    private sealed class Emancipation : IEquipmentEmancipateDefinitionSource
    {
        public ValueTask<IReadOnlyList<EquipmentEmancipateDefinition>> GetEquipmentAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<EquipmentEmancipateDefinition>>(itemId is >= 3000 and <= 3003
                ? [new(null, new(1, itemId - 2999), [new(10000224, 1)], itemId < 3003 ? [new(itemId + 1, 1)] : [])]
                : []);
    }

    private sealed class GrowthTable : IEquipmentGrowthDefinitionSource
    {
        private static readonly EquipmentGrowthPenalty[] Fatigue = [new("fatigue", 30, 20)];
        public ValueTask<EquipmentGrowthGroup?> GetGroupAsync(int group, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentGrowthGroup?>(group != 1 ? null : new(1, 4,
                [new("death", 5, new Dictionary<int, int> { [1] = 100, [2] = 100, [3] = 300, [4] = 140 },
                    [3, 3, 10, 5, 90, 10], [100, 150, 200, 200, 200])],
                [new(1, 10, true, Fatigue), new(2, 20, true, Fatigue), new(3, 30, false, Fatigue), new(4, 0, false, [])]));
    }
}
