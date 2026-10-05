using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class ItemAdmissionRuleTests
{
    [Theory]
    [InlineData(ItemTraits.None, false)]
    [InlineData(ItemTraits.Consumable, false)]
    [InlineData(ItemTraits.Material, false)]
    [InlineData(ItemTraits.AccountShared, false)]
    [InlineData(ItemTraits.AccountShared | ItemTraits.Consumable, true)]
    [InlineData(ItemTraits.AccountShared | ItemTraits.Material, true)]
    [InlineData(ItemTraits.AccountShared | ItemTraits.Consumable | ItemTraits.Material, true)]
    [InlineData(ItemTraits.AccountShared | ItemTraits.Consumable | ItemTraits.Equipment, false)]
    [InlineData(ItemTraits.AccountShared | ItemTraits.Material | (ItemTraits)256, false)]
    public void Required_feature_and_one_of_the_categories_must_both_match(ItemTraits traits, bool expected)
    {
        var rule = new ItemAdmissionRule(
            ItemTraits.AccountShared | ItemTraits.Consumable | ItemTraits.Material,
            all: ItemTraits.AccountShared,
            any: ItemTraits.Consumable | ItemTraits.Material);

        Assert.Equal(expected, rule.Matches(traits));
    }

    [Theory]
    [InlineData(ItemTraits.Creature, false)]
    [InlineData(ItemTraits.Consumable, false)]
    [InlineData(ItemTraits.Creature | ItemTraits.Consumable, true)]
    [InlineData(ItemTraits.Creature | ItemTraits.Consumable | ItemTraits.AccountShared, true)]
    public void All_requires_every_specified_feature(ItemTraits traits, bool expected)
    {
        var rule = new ItemAdmissionRule(
            ItemTraits.Creature | ItemTraits.Consumable | ItemTraits.AccountShared,
            all: ItemTraits.Creature | ItemTraits.Consumable);

        Assert.Equal(expected, rule.Matches(traits));
    }

    [Theory]
    [InlineData(ItemTraits.Quest, false)]
    [InlineData(ItemTraits.Equipment, true)]
    [InlineData(ItemTraits.Material, true)]
    [InlineData(ItemTraits.Equipment | ItemTraits.Material, true)]
    [InlineData(ItemTraits.Equipment | ItemTraits.Quest, true)]
    public void Any_accepts_one_or_more_alternatives_not_unrelated_allowed_features(ItemTraits traits, bool expected)
    {
        var rule = new ItemAdmissionRule(
            ItemTraits.Equipment | ItemTraits.Material | ItemTraits.Quest,
            any: ItemTraits.Equipment | ItemTraits.Material);

        Assert.Equal(expected, rule.Matches(traits));
    }

    [Fact]
    public void Omitted_constraints_preserve_allowed_subset_semantics()
    {
        var rule = new ItemAdmissionRule(ItemTraits.Material | ItemTraits.AccountShared);
        Assert.True(rule.Matches(ItemTraits.Material));
        Assert.True(rule.Matches(ItemTraits.AccountShared));
        Assert.True(rule.Matches(ItemTraits.Material | ItemTraits.AccountShared));
        Assert.False(rule.Matches(ItemTraits.None));
        Assert.False(rule.Matches(ItemTraits.Material | ItemTraits.Equipment));
    }

    [Fact]
    public void Default_rule_and_empty_allowed_set_reject_all_features()
    {
        foreach (var traits in Enum.GetValues<ItemTraits>().Append((ItemTraits)(-1)))
        {
            Assert.False(default(ItemAdmissionRule).Matches(traits));
            Assert.False(new ItemAdmissionRule(ItemTraits.None).Matches(traits));
        }
    }

    [Fact]
    public void Impossible_constraints_are_rejected_when_created()
    {
        Assert.Throws<ArgumentException>(() => new ItemAdmissionRule(ItemTraits.Material, all: ItemTraits.Consumable));
        Assert.Throws<ArgumentException>(() => new ItemAdmissionRule(ItemTraits.Material, any: ItemTraits.Consumable));
        Assert.Throws<ArgumentException>(() => new ItemAdmissionRule(ItemTraits.None, all: ItemTraits.Material));
        Assert.Throws<ArgumentException>(() => new ItemAdmissionRule(ItemTraits.None, any: ItemTraits.Material));
    }

    [Fact]
    public void Any_can_include_disallowed_alternatives_as_long_as_one_can_match()
    {
        var rule = new ItemAdmissionRule(ItemTraits.Material, any: ItemTraits.Material | ItemTraits.Consumable);
        Assert.True(rule.Matches(ItemTraits.Material));
        Assert.False(rule.Matches(ItemTraits.Consumable));
        Assert.False(rule.Matches(ItemTraits.Material | ItemTraits.Consumable));
    }
}
