using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;
using Section = Dfo.Pvf.Schema.PvfSection;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>Package declarations as the A21 client reads them (Document/tasks/package-opening-328).</summary>
public sealed class PvfPackageOpeningParseTests
{
    private static readonly string[] Strings = ["", "[booster]", "[booster selection]", "[swordman]", "[all]", "[unknown]"];

    [Fact]
    public void Job_pools_carry_their_job_header_and_draw_count()
    {
        var definition = Parse(1, "[booster]",
            new("[booster info]", []),
            new("[charactor]", [S(3), I(1), I(70), I(2), I(500), I(100), I(1), I(501), I(300), I(2)]), new("[/charactor]", []),
            new("[charactor]", [S(3), I(1), I(85), I(1)]), new("[/charactor]", []),
            new("[charactor]", [S(4), I(1), I(70), I(1), I(600), I(100), I(1)]), new("[/charactor]", []),
            new("[etc]", [I(1), I(700), I(100), I(5)]), new("[/etc]", []),
            new("[/booster info]", []));
        Assert.Empty(definition.Diagnostics);
        Assert.Equal(3, definition.Pools.Length);
        Assert.Equal((0, (1, 70), 2), (definition.Pools[0].Job, definition.Pools[0].JobHeader!.Value, definition.Pools[0].Draws));
        Assert.Equal([500, 501], definition.Pools[0].Choices.Select(c => c.Reward.ItemId));
        Assert.Null(definition.Pools[1].Job);
        Assert.Null(definition.Pools[2].Job);
        Assert.Null(definition.Pools[2].JobHeader);
    }

    [Theory]
    [InlineData(5, "Invalid job pool")]
    [InlineData(-1, "Mismatched booster category")]
    public void Unknown_job_or_mismatched_closing_tag_is_refused(int job, string expected)
    {
        Section[] rows = job < 0
            ? [new("[booster info]", []), new("[etc]", [I(1), I(700), I(100), I(5)]), new("[/avatar]", []), new("[/booster info]", [])]
            : [new("[booster info]", []), new("[charactor]", [S(job), I(1), I(70), I(1), I(500), I(100), I(1)]),
                new("[/charactor]", []), new("[/booster info]", [])];
        var definition = Parse(1, "[booster]", rows);
        Assert.Contains(definition.Diagnostics, d => d.StartsWith(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Selection_blocks_become_categories_with_equipment_modifiers_applied_after_them()
    {
        var definition = Parse(2, "[booster selection]",
            new("[booster category num]", [I(2), I(2), I(1)]),
            new("[booster selection num]", [I(0)]),
            new("[booster select category]", [I(0), I(0)]),
            new("[default select]", [I(1), I(900)]),
            new("[stackable]", [I(900), I(5)]), new("[/stackable]", []),
            new("[booster equipment upgrade]", [I(255)]),
            new("[random probability]", [I(1), I(20), I(5), I(80)]),
            new("[equipment]", [I(100), I(1), I(101), I(1)]), new("[/equipment]", []),
            new("[avatar]", [I(40000), I(1), I(3), I(-1)]), new("[/avatar]", []),
            new("[/booster select category]", []),
            new("[booster select category]", [I(1), I(0)]),
            new("[equipment]", [I(100), I(2)]), new("[/equipment]", []),
            new("[/booster select category]", []),
            new("[booster select category]", [I(1), I(1)]),
            new("[/booster select category]", []),
            new("[/booster select category]", []));
        Assert.Empty(definition.Diagnostics);
        Assert.Equal(OpeningPackageKind.Selection, definition.Kind);
        Assert.Empty(definition.FixedRewards);
        Assert.Equal([new(0, 0), new(1, 0), new(1, 1)], definition.Categories.Select(c => c.Key));
        var first = definition.Categories[0].Rewards;
        Assert.Equal([900, 100, 101, 40000], first.Select(r => r.ItemId));
        Assert.Equal(0, first[0].Upgrade);
        Assert.Equal(OpeningReward.RandomUpgrade, first[1].Upgrade);
        Assert.Equal(new OpeningUpgradeChance[] { new(1, 20), new(5, 80) }, first[1].UpgradeChances.ToArray());
        Assert.Equal(0, first[3].Upgrade);
        Assert.Equal((3, (byte)0), (first[3].AvatarTypeRow, first[3].Ability));
        // Modifiers end with their block; a later block's equipment carries none.
        Assert.Equal((100, 2, (byte)0), (definition.Categories[1].Rewards[0].ItemId, definition.Categories[1].Rewards[0].Count, definition.Categories[1].Rewards[0].Upgrade));
        Assert.Empty(definition.Categories[2].Rewards);
    }

    [Fact]
    public void Random_probability_without_the_random_upgrade_marker_is_refused_like_the_client()
    {
        var definition = Parse(2, "[booster selection]",
            new("[booster selection num]", [I(1)]),
            new("[booster select category]", [I(0), I(0)]),
            new("[booster equipment upgrade]", [I(3)]),
            new("[random probability]", [I(1), I(20)]),
            new("[equipment]", [I(100), I(1)]), new("[/equipment]", []),
            new("[/booster select category]", []));
        Assert.Contains("Random probability without random upgrade", definition.Diagnostics);
    }

    [Fact]
    public void Repeated_category_coordinates_must_declare_the_same_rows()
    {
        Section Block(int item) => new("[equipment]", [I(item), I(1)]);
        var same = Parse(2, "[booster selection]",
            new("[booster select category]", [I(0), I(0)]), Block(100), new("[/booster select category]", []),
            new("[booster select category]", [I(0), I(0)]), Block(100), new("[/booster select category]", []));
        Assert.Empty(same.Diagnostics);
        Assert.Single(same.Categories);
        var conflicting = Parse(2, "[booster selection]",
            new("[booster select category]", [I(0), I(0)]), Block(100), new("[/booster select category]", []),
            new("[booster select category]", [I(0), I(0)]), Block(101), new("[/booster select category]", []));
        Assert.Contains("Conflicting selection category", conflicting.Diagnostics);
    }

    [Fact]
    public void Empty_pools_and_a_category_closer_without_its_slash_declare_nothing_as_the_client_reads_them()
    {
        var pool = Parse(1, "[booster]",
            new("[booster info]", []), new("[etc]", [I(1)]), new("[/etc]", []),
            new("[stackable]", [I(1), I(700), I(100), I(5)]), new("[/stackable]", []), new("[/booster info]", []));
        Assert.Empty(pool.Diagnostics);
        Assert.Equal(700, Assert.Single(Assert.Single(pool.Pools).Choices).Reward.ItemId);
        var selection = Parse(2, "[booster selection]",
            new("[booster selection num]", [I(1)]),
            new("[booster select category]", [I(0), I(0)]), new("[equipment]", [I(100), I(1)]), new("[equipment]", []),
            new("[/booster select category]", []),
            new("[booster select category]", [I(0), I(1)]), new("[equipment]", []), new("[/equipment]", []),
            new("[/booster select category]", []));
        Assert.Empty(selection.Diagnostics);
        Assert.Equal([100], selection.Categories[0].Rewards.Select(r => r.ItemId));
        Assert.Empty(selection.Categories[1].Rewards);
    }

    [Fact]
    public void Fixed_package_takes_its_last_package_data_declaration()
    {
        var definition = Parse(3, "[booster]",
            new("[package data]", [I(10), I(1)]), new("[package data]", [I(11), I(2), I(12), I(3)]));
        Assert.Empty(definition.Diagnostics);
        Assert.Equal([11, 12], definition.FixedRewards.Select(r => r.ItemId));
    }

    private static OpeningPackageDefinition Parse(int id, string type, params Section[] rows)
    {
        var typeIndex = Array.IndexOf(Strings, type);
        return PvfPackageOpeningDefinitionSource.Parse(id,
            [new("[stackable type]", [new(PvfTokenKind.QuotedString, typeIndex), I(0)]), .. rows],
            token => Strings[token.Value]);
    }

    private static PvfToken I(int value) => new(PvfTokenKind.Int32, value);
    private static PvfToken S(int index) => new(PvfTokenKind.QuotedString, index);
}
