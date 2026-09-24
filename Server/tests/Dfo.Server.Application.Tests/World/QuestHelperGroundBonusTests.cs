using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class QuestHelperGroundBonusTests
{
    [Fact]
    public void Bonus_count_is_three_independent_trials_with_expected_distribution()
    {
        var rules = new PremiumServiceRuleService(new Definitions());
        var counts = new int[4];
        for (ulong seed = 0; seed < 20000; seed++)
        {
            var count = rules.DrawQuestHelperBonusCount(seed);
            Assert.InRange(count, 1, 3);
            Assert.Equal(count, rules.DrawQuestHelperBonusCount(seed));
            counts[count]++;
        }
        Assert.InRange(counts[1] / 20000.0, .19, .23);
        Assert.InRange(counts[2] / 20000.0, .56, .60);
        Assert.InRange(counts[3] / 20000.0, .19, .23);
        // A miss in the first attempt must not suppress later attempts.
        var independent = new PremiumServiceRuleService(new Definitions([0, 100, 100]));
        Assert.Equal(2, independent.DrawQuestHelperBonusCount(1));
    }

    [Fact]
    public async Task Any_member_enables_union_bonus_once_without_changing_personal_or_unneeded_loot()
    {
        await using var storage = new LearningFixture();
        await storage.SeedAsync();
        var query = new Materials();
        var service = new QuestHelperGroundBonusDomainService(
            storage.Services.GetRequiredService<IPremiumServiceDomainService>(),
            new PremiumServiceRuleService(new Definitions()), query);
        var context = Context();
        var original = Cell(context);
        Assert.Same(original, await service.ApplyAsync(context, original, default));
        Assert.Empty(query.Calls);
        // Only the second member has a contract; only the first member needs item 100.
        storage.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(2,582,4000000000)");
        var result = await service.ApplyAsync(context, original, default);
        Assert.Equal(new[] { 10, 20 }, query.Calls);
        var actor = Assert.Single(result.Actors);
        Assert.InRange(actor.PendingLoot[0].Count, 2, 4);
        Assert.Equal(1, actor.PendingLoot[1].Count); // duplicate material: no second bonus
        Assert.Equal(original.Actors[0].PendingLoot.Skip(2), actor.PendingLoot.Skip(2));
        Assert.Equal(1, original.Actors[0].PendingLoot[0].Count); // no mutation of the seed
        Assert.InRange(result.GroundItems[0].Count, 2, 4);
        Assert.Equal(original.GroundItems.Skip(1), result.GroundItems.Skip(1));
        Assert.Equal(original.Objects, result.Objects);
        Assert.All(actor.PendingLoot, drop => Assert.NotEqual(200, drop.ItemId)); // needed but absent: not fabricated
        // Additional subscriptions never add another round of bonuses.
        storage.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,582,4000000000)");
        var two = await service.ApplyAsync(context, original, default);
        Assert.Equal(actor.PendingLoot, two.Actors[0].PendingLoot);
        Assert.Equal(result.GroundItems, two.GroundItems);
        storage.Sql("UPDATE account_premiums SET end_time=1");
        Assert.Same(original, await service.ApplyAsync(context, original, default));
    }

    [Fact]
    public async Task No_outstanding_materials_mean_no_bonus_even_with_active_contract()
    {
        await using var storage = new LearningFixture();
        await storage.SeedAsync();
        storage.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,582,4000000000)");
        var query = new Materials(empty: true);
        var service = new QuestHelperGroundBonusDomainService(
            storage.Services.GetRequiredService<IPremiumServiceDomainService>(),
            new PremiumServiceRuleService(new Definitions()), query);
        var context = Context();
        var cell = Cell(context);
        Assert.Same(cell, await service.ApplyAsync(context, cell, default));
        Assert.Equal(new[] { 10, 20 }, query.Calls);
    }

    private static DungeonCellCreationContext Context()
    {
        var cell = new MazeCell(0, 0);
        var layout = new DungeonLayout(84, 0, 1, 1, cell, cell, 123, 456,
            new Dictionary<MazeCell, int> { [cell] = 123 });
        return new(DungeonRunId.New(), layout, cell, 123, 0, DateTimeOffset.UnixEpoch,
            [new(new(1), new(10), 0, true), new(new(2), new(20), 1, false)]);
    }
    private static DungeonCellSeed Cell(DungeonCellCreationContext context) => new(
        context.Cell, context.MapId, 1,
        [new(1, new DungeonRoomActor(99, 1, 0), false, false,
            [new(100, 1, DropRarity.Common, DropItemKind.Stackable),
             new(100, 1, DropRarity.Common, DropItemKind.Stackable),
             new(999, 1, DropRarity.Common, DropItemKind.Stackable),
             new(0, 500, DropRarity.Common, DropItemKind.Gold),
             new(100, 1, DropRarity.Common, DropItemKind.Equipment),
             new(100, 1, DropRarity.Common, DropItemKind.Stackable, OwnerCharacterId: new(10),
                 Delivery: RolledDropDelivery.DirectQuestItem)])],
        [new(1, 100, 1, 0, false, DungeonGroundItemSource.Declared(0)),
         new(2, 100, 1, 0, false, DungeonGroundItemSource.Declared(0)),
         new(3, 100, 1, 0, false, DungeonGroundItemSource.FromPlayer()),
         new(4, 0, 500, 0, false, DungeonGroundItemSource.Declared(1)),
         new(5, 100, 1, 0, true, DungeonGroundItemSource.Declared(2))]);
    private sealed class Materials(bool empty = false) : IQuestMaterialRequirementDomainService
    {
        public List<int> Calls { get; } = [];
        public ValueTask<IReadOnlySet<int>> GetOutstandingMaterialIdsAsync(AccountId accountId,
            CharacterId characterId, CancellationToken cancellationToken)
        {
            Calls.Add(characterId.Value);
            return ValueTask.FromResult<IReadOnlySet<int>>(empty ? new HashSet<int>()
                : characterId.Value == 10 ? new HashSet<int> { 100 } : new HashSet<int> { 200 });
        }
    }
    private sealed class Definitions(int[]? rates = null) : IPremiumServiceDefinitionSource
    {
        public IReadOnlyList<int> ReadQuestHelperMinimumDropRates() => rates ?? [100, 70, 30];
        public DoublePotRules ReadDoublePotRules() => throw new NotSupportedException();
        public PremiumContractLevels ReadContractLevels() => throw new NotSupportedException();
        public int ReadFreeWeaknessDailyLimit() => throw new NotSupportedException();
        public int ReadDungeonBuffDailyLimit() => throw new NotSupportedException();
        public int ReadAutoRepairDailyLimit() => throw new NotSupportedException();
        public int ReadFreeGoldCardDailyLimit() => throw new NotSupportedException();
    }
}
