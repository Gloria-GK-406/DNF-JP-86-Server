using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>Selection categories, job pools, reinforcement and avatar rows as one draw resolves them (#328).</summary>
public sealed class PackageOpeningDrawTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);

    private static OpeningPackageDefinition Selection(int count, params OpeningSelectionCategory[] categories) =>
        new(1, OpeningPackageKind.Selection, [], [], count, 0, 0, 1, false, []) { Categories = [.. categories] };

    private static OpeningSelectionCategory Category(byte first, byte second, params OpeningReward[] rows) =>
        new(new OpeningCategory(first, second), [.. rows]);

    [Fact]
    public void A_selection_is_looked_up_in_the_requested_category_only()
    {
        var definition = Selection(1, Category(0, 0, new OpeningReward(100, 1)), Category(1, 0, new OpeningReward(100, 7), new OpeningReward(101, 1)));
        var reward = Assert.Single(Rules().Roll(definition, new([100], new(1, 0), 0), 1));
        Assert.Equal(7, reward.Count);
        var error = Assert.Throws<OpeningRejectedException>(() => Rules().Roll(definition, new([101], new(0, 0), 0), 1));
        Assert.Equal(OpeningRejection.InvalidChoice, error.Reason);
        error = Assert.Throws<OpeningRejectedException>(() => Rules().Roll(definition, new([100], new(2, 0), 0), 1));
        Assert.Equal(OpeningRejection.InvalidChoice, error.Reason);
    }

    [Fact]
    public void Selection_count_zero_grants_the_whole_category_when_every_row_is_named()
    {
        var definition = Selection(0, Category(0, 0, new OpeningReward(100, 1), new OpeningReward(101, 2), new OpeningReward(100, 1)), Category(0, 1, new OpeningReward(102, 1)));
        var rewards = Rules().Roll(definition, new([100, 101, 100], new(0, 0), 0), 1);
        Assert.Equal([100, 101, 100], rewards.Select(r => r.ItemId));
        Assert.Throws<OpeningRejectedException>(() => Rules().Roll(definition, new([100, 101], new(0, 0), 0), 1));
        Assert.Throws<OpeningRejectedException>(() => Rules().Roll(definition, new([102], new(0, 0), 0), 1));
    }

    [Fact]
    public void Job_pools_draw_only_for_the_opener_and_a_job_without_one_cannot_open()
    {
        OpeningRewardPool Pool(int job, int item) => new(1, [new(new(item, 1), 100)]) { Job = job };
        var definition = new OpeningPackageDefinition(1, OpeningPackageKind.Package, [], [Pool(0, 500), Pool(6, 506)],
            0, 0, 0, 1, false, []);
        Assert.Equal(506, Assert.Single(Rules().Roll(definition, new([], default, 6), 1)).ItemId);
        var error = Assert.Throws<OpeningRejectedException>(() => Rules().Roll(definition, new([], default, 3), 1));
        Assert.Equal(OpeningRejection.ItemUnusable, error.Reason);
    }

    [Fact]
    public void Random_reinforcement_is_drawn_from_the_declared_levels()
    {
        var row = new OpeningReward(100, 1) { Upgrade = OpeningReward.RandomUpgrade, UpgradeChances = [new(4, 1), new(9, 0), new(6, 1)] };
        var definition = Selection(1, Category(0, 0, row));
        var levels = Enumerable.Range(0, 64).Select(seed => Assert.Single(Rules().Roll(definition, new([100], default, 0), seed)).Upgrade).ToHashSet();
        Assert.Equal(new byte[] { 4, 6 }, levels.Order().ToArray());
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 0)]
    [InlineData(3, 15)]
    [InlineData(null, 15)]
    public async Task An_avatar_row_takes_its_type_row_period_and_out_of_range_keeps_the_avatar_period(int? row, int days)
    {
        var content = new Content();
        content.Items[40000] = new(40000, new(8, 1, new(BusinessInventorySpace.Avatar, 0, 209)) { WorldClass = ItemWorldClass.Avatar },
            0, false, [], null, true, DefaultPeriodDays: 15) { AvatarTypePeriods = [7, 30, 0] };
        var grant = Assert.Single(await Rules(content).PrepareAsync([new(40000, 1) { AvatarTypeRow = row }], Now, default));
        Assert.Equal(days == 0 ? 0 : (int)Now.AddDays(days).ToUnixTimeSeconds(), grant.Expiration);
    }

    [Fact]
    public async Task Equipment_modifiers_reach_equipment_and_are_dropped_elsewhere()
    {
        var content = new Content();
        content.Items[100] = new(100, new(1, 1, new(BusinessInventorySpace.CarriedEquipment, 0, 47)) { WorldClass = ItemWorldClass.Equipment },
            45, false, [], null, true);
        content.Items[3] = new(3, new(2, 10000, new(BusinessInventorySpace.Consumable, 0, 35)), 0, false, [], null, true);
        Assert.Equal(12, Assert.Single(await Rules(content).PrepareAsync([new(100, 1) { Upgrade = 12 }], Now, default)).Reward.Upgrade);
        // A stackable filed under a block's [equipment] category keeps none of its equipment modifiers.
        var stackable = Assert.Single(await Rules(content).PrepareAsync([new(3, 1) { Upgrade = 12, Forging = 5, QualityTier = 3 }], Now, default));
        Assert.Equal((0, 0, (byte?)null), (stackable.Reward.Upgrade, stackable.Reward.Forging, stackable.Reward.QualityTier));
        await Assert.ThrowsAsync<OpeningRejectedException>(async () => await Rules(content).PrepareAsync([new(100, 1) { Forging = 9 }], Now, default));
    }

    private static PackageOpeningRuleService Rules(Content? content = null) =>
        new(content ?? new Content(), null!, TestRequired.NoExpiration, null!);

    private sealed class Content : IPackageOpeningDefinitionSource
    {
        public Dictionary<int, OpeningRewardItemDefinition> Items { get; } = [];
        public ValueTask<OpeningPackageDefinition?> GetAsync(int itemId, CancellationToken ct) => ValueTask.FromResult<OpeningPackageDefinition?>(null);
        public ValueTask<OpeningRewardItemDefinition?> GetRewardAsync(int itemId, CancellationToken ct) => ValueTask.FromResult(Items.GetValueOrDefault(itemId));
    }
}
