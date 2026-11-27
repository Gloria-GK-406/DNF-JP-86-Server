using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

[Trait("Category", "Unit")]
public sealed class GuardianGemRuleTests
{
    [Theory]
    [InlineData(BusinessInventorySpace.GuardianGem, 0, 0, true)]
    [InlineData(BusinessInventorySpace.GuardianGem, 48, 3, true)]
    [InlineData(BusinessInventorySpace.GuardianGem, -1, 0, false)]
    [InlineData(BusinessInventorySpace.GuardianGem, 49, 0, false)]
    [InlineData(BusinessInventorySpace.GuardianGem, 0, 4, false)]
    [InlineData(BusinessInventorySpace.GuildMedal, 0, 0, false)]
    public void Installation_requires_a_gem_tray_slot_and_a_medal_socket(
        BusinessInventorySpace space, int index, byte socket, bool accepted) =>
        Assert.Equal(accepted, GuardianGemRuleService.AcceptsSlots(new(space, index), socket));

    [Fact]
    public void The_target_is_an_individual_medal_and_the_source_is_a_nonempty_gem_pile()
    {
        var now = DateTimeOffset.UnixEpoch;
        var medal = new ItemSnapshot { LegacyKind = 12 };
        var gem = new ItemSnapshot { LegacyKind = 13, IsPile = true };
        Assert.True(GuardianGemRuleService.AcceptsMedal(medal, now));
        Assert.False(GuardianGemRuleService.AcceptsMedal(medal with { LegacyKind = 13 }, now));
        Assert.False(GuardianGemRuleService.AcceptsMedal(medal with { IsPile = true }, now));
        Assert.True(GuardianGemRuleService.AcceptsGem(gem, now));
        Assert.False(GuardianGemRuleService.AcceptsGem(gem with { LegacyKind = 12 }, now));
        Assert.False(GuardianGemRuleService.AcceptsGem(gem with { IsPile = false }, now));
        Assert.False(GuardianGemRuleService.AcceptsGem(gem with { Count = 0 }, now));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Both_items_must_be_original_unlocked_and_unexpired(bool isGem)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(100);
        var item = new ItemSnapshot { LegacyKind = (byte)(isGem ? 13 : 12), IsPile = isGem };
        Func<ItemSnapshot, DateTimeOffset, bool> accepts =
            isGem ? GuardianGemRuleService.AcceptsGem : GuardianGemRuleService.AcceptsMedal;
        Assert.False(accepts(item with { IsMirror = true }, now));
        Assert.False(accepts(item with { EquipmentLockId = 1 }, now));
        Assert.False(accepts(item with { SortLockFlag = 1 }, now));
        Assert.False(accepts(item with { ExpireTime = 100 }, now));
        Assert.True(accepts(item with { ExpireTime = 101 }, now));
    }
}
