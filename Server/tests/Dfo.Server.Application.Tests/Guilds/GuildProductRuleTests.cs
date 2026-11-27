using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Application.Guilds;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Guilds;

[Trait("Category", "Unit")]
public sealed class GuildProductRuleTests
{
    [Theory]
    [InlineData(490001375, true)]
    [InlineData(490001374, false)]
    [InlineData(490001376, false)]
    public void Rename_voucher_requires_the_designated_item(int itemId, bool eligible) =>
        Assert.Equal(eligible, GuildProductRuleService.IsRenameVoucher(itemId));

    [Theory]
    [InlineData(8)]
    [InlineData(-5)]
    public void Gift_day_changes_at_midnight_in_the_supplied_local_offset(int offsetHours)
    {
        var midnight = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromHours(offsetHours));
        Assert.Equal(new DateOnly(2026, 12, 31), GuildProductRuleService.GiftDay(midnight.AddSeconds(-1)));
        Assert.Equal(new DateOnly(2027, 1, 1), GuildProductRuleService.GiftDay(midnight));
    }

    [Theory]
    [InlineData(GuildContentKind.TemporaryAttributes, true, 200, 1, true)]
    [InlineData(GuildContentKind.Experience, true, 200, 1, true)]
    [InlineData(GuildContentKind.Experience, false, 200, 1, false)]
    [InlineData(GuildContentKind.Experience, true, 0, 1, false)]
    [InlineData(GuildContentKind.TemporaryAttributes, true, 200, 0, false)]
    [InlineData(GuildContentKind.PermanentAttributes, true, 200, 1, false)]
    public void Gift_offer_uses_the_flag_kind_price_and_duration(
        GuildContentKind kind, bool giftable, int price, int days, bool eligible)
    {
        var product = new GuildContentDefinition(7, kind, 10, price, days, giftable);
        var catalog = new GuildContentCatalog(new Dictionary<uint, GuildContentDefinition> { [7] = product }, null);
        var rules = new GuildProductRuleService(TestRequired.Absent<IGuildEconomySource>((_, _) => catalog));
        var offer = rules.EvaluateGiftOffer(7);
        Assert.Equal(eligible ? GuildGiftStatus.Success : GuildGiftStatus.UnsupportedContent, offer.Status);
        Assert.Equal(eligible ? product : null, offer.Product);
    }

    [Theory]
    [InlineData(null, 0, 8, GuildMutationStatus.UnsupportedContent, null)]
    [InlineData(56, 48, 8, GuildMutationStatus.Success, 56)]
    [InlineData(56, 49, 8, GuildMutationStatus.WarehouseCapacityLimit, null)]
    [InlineData(int.MaxValue, int.MaxValue, 1, GuildMutationStatus.WarehouseCapacityLimit, null)]
    public void Warehouse_expansion_checks_the_result_before_granting_capacity(
        int? limit, int current, int slots, GuildMutationStatus status, int? capacity)
    {
        var catalog = new GuildContentCatalog(new Dictionary<uint, GuildContentDefinition>(), null, WarehouseCapacityLimit: limit);
        var rules = new GuildProductRuleService(TestRequired.Absent<IGuildEconomySource>((_, _) => catalog));
        var expansion = rules.EvaluateWarehouseExpansion(new(1), current, slots);
        Assert.Equal(status, expansion.Status);
        Assert.Equal(capacity, expansion.WarehouseCapacity);
    }
}
