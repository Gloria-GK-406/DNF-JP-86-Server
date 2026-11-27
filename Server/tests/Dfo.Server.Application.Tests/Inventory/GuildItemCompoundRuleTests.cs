using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

[Trait("Category", "Unit")]
public sealed class GuildItemCompoundRuleTests
{
    [Theory]
    [InlineData(GuildCompoundKind.GuardianGem, 0, 2, 2, true)]
    [InlineData(GuildCompoundKind.Medal, 6, 4, 4, true)]
    [InlineData(GuildCompoundKind.GuardianGem, 1, 1, 4, false)]
    [InlineData(GuildCompoundKind.Medal, 1, 5, 5, false)]
    [InlineData(GuildCompoundKind.GuardianGem, 1, 4, 3, false)]
    [InlineData(GuildCompoundKind.Medal, -1, 2, 4, false)]
    [InlineData(GuildCompoundKind.GuardianGem, 7, 2, 4, false)]
    [InlineData((GuildCompoundKind)2, 1, 2, 4, false)]
    public void Recipe_requires_supported_kind_rarity_and_material_count(
        GuildCompoundKind kind, int rarity, int count, int recipeLimit, bool eligible)
    {
        var recipe = new GuildCompoundRule(recipeLimit, 0, [100], []);
        var content = TestRequired.Absent<IGuildCompoundContentSource>((_, _) => recipe);
        var rules = new GuildItemCompoundRuleService(content, TestRequired.Absent<ILotteryRandomSource>());
        var result = rules.Recipe(new(kind, rarity, new BusinessSlot[count]));
        Assert.Same(eligible ? recipe : null, result);
    }

    [Theory]
    [InlineData(GuildCompoundKind.GuardianGem, BusinessInventorySpace.GuardianGem, 48, 3, true)]
    [InlineData(GuildCompoundKind.GuardianGem, BusinessInventorySpace.GuardianGem, 49, 1, false)]
    [InlineData(GuildCompoundKind.GuardianGem, BusinessInventorySpace.GuildMedal, 0, 1, false)]
    [InlineData(GuildCompoundKind.Medal, BusinessInventorySpace.GuildMedal, 0, 1, true)]
    [InlineData(GuildCompoundKind.Medal, BusinessInventorySpace.GuildMedal, -1, 1, false)]
    [InlineData(GuildCompoundKind.Medal, BusinessInventorySpace.GuildMedal, 0, 2, false)]
    public void Material_slots_match_the_compound_kind_and_allowed_repeats(
        GuildCompoundKind kind, BusinessInventorySpace space, int index, int count, bool eligible) =>
        Assert.Equal(eligible, GuildItemCompoundRuleService.AcceptsMaterialSlot(kind, new(space, index), count));

    [Theory]
    [InlineData(GuildCompoundKind.GuardianGem, 13, true)]
    [InlineData(GuildCompoundKind.Medal, 12, false)]
    public void Only_available_materials_of_the_requested_kind_and_rarity_are_eligible(
        GuildCompoundKind kind, byte legacyKind, bool isPile)
    {
        var content = TestRequired.Absent<IGuildCompoundContentSource>((_, args) =>
            Equals(args![0], kind) && Equals(args[1], 101) ? 2 : (int?)null);
        var rules = new GuildItemCompoundRuleService(content, TestRequired.Absent<ILotteryRandomSource>());
        var input = new GuildCompoundInput(kind, 2, []);
        var now = DateTimeOffset.FromUnixTimeSeconds(100);
        var item = new ItemSnapshot { ItemId = 101, LegacyKind = legacyKind, IsPile = isPile };

        Assert.True(rules.AcceptsMaterial(input, item, now));
        Assert.True(rules.AcceptsMaterial(input, item with { ExpireTime = 101 }, now));
        Assert.False(rules.AcceptsMaterial(input, item with { ExpireTime = 100 }, now));
        Assert.False(rules.AcceptsMaterial(input, item with { LegacyKind = 0 }, now));
        Assert.False(rules.AcceptsMaterial(input, item with { IsPile = !isPile }, now));
        Assert.False(rules.AcceptsMaterial(input, item with { IsMirror = true }, now));
        Assert.False(rules.AcceptsMaterial(input, item with { EquipmentLockId = 1 }, now));
        Assert.False(rules.AcceptsMaterial(input, item with { SortLockFlag = 1 }, now));
        Assert.False(rules.AcceptsMaterial(input, item with { ItemId = 102 }, now));
        Assert.False(rules.AcceptsMaterial(input with { Rarity = 1 }, item, now));
    }
}
