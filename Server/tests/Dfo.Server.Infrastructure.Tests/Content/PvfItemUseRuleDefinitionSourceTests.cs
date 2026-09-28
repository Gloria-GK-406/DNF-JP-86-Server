using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// What the archive declares about using an item, read from the archive itself.
/// </summary>
/// <remarks>
/// The six tags this reads sit in no fixed order and their values are quoted strings rather than
/// identifiers, which is the detail a reader written from the tag list alone gets wrong: assuming
/// bracketed values such as <c>[all]</c> open a new section makes every section read as empty and
/// every restriction read as absent. Stubbing the content interface would leave exactly that
/// unproven, so these run against the real file.
/// </remarks>
public sealed class PvfItemUseRuleDefinitionSourceTests
{
    [SkippableFact]
    public async Task Real_archive_reads_spirit_potion_bonus_and_effect_duration()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfItemUseRuleDefinitionSource(provider);

        var rule = await source.GetAsync(7377, CancellationToken.None);

        Assert.NotNull(rule);
        Assert.True(rule.IsReadable);
        Assert.Equal(1, rule.ExpBonusRate);
        Assert.Equal(7_200_000, rule.EffectDurationMilliseconds);
    }

    /// <remarks>
    /// <c>stackable/tutorial/tutorial_8474.stk</c>, the potion the tutorial hands out and the one
    /// the live walkthrough drinks: consumed, ten seconds, sharing its wait with nothing.
    /// </remarks>
    [SkippableFact]
    public async Task Real_archive_reads_the_tutorial_potion()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfItemUseRuleDefinitionSource(provider);

        var rule = await source.GetAsync(8474, CancellationToken.None);

        Assert.NotNull(rule);
        Assert.True(rule.IsConsumedOnUse);
        Assert.Equal(10_000, rule.CooldownMilliseconds);
        Assert.Equal(0, rule.CooltimeGroup);
        Assert.Equal(1, rule.MinimumLevel);
        Assert.True(rule.Jobs.IsExactAll);
        Assert.True(rule.Places.IsUnrestricted);
        Assert.True(rule.IsReadable);
    }

    /// <remarks>
    /// <c>stackable/cash/owl_unlimited.stk</c>: one of the eighty-four the server used to destroy.
    /// It is also the only one of the three fixtures that declares a shared group and a place, so
    /// it is what proves those two sections are read rather than skipped.
    /// </remarks>
    [SkippableFact]
    public async Task Real_archive_reads_an_item_that_survives_its_own_use()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfItemUseRuleDefinitionSource(provider);

        var rule = await source.GetAsync(51, CancellationToken.None);

        Assert.NotNull(rule);
        Assert.False(rule.IsConsumedOnUse);
        Assert.Equal(3_000, rule.CooldownMilliseconds);
        Assert.Equal(70, rule.CooltimeGroup);
        Assert.False(rule.Places.IsUnrestricted);
        Assert.Equal([ItemUsePlace.Village, ItemUsePlace.Dungeon], rule.Places.Places);
        Assert.True(rule.IsReadable);
    }

    /// <remarks>
    /// <c>stackable/quest/scroll_wpmaster.stk</c>, the swordman's job-change scroll. 653 items
    /// restrict their <c>[usable job]</c> to something other than <c>[all]</c>; reading that
    /// section as empty would hand every one of them to every class.
    /// </remarks>
    [SkippableFact]
    public async Task Real_archive_reads_a_job_restricted_item()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfItemUseRuleDefinitionSource(provider);

        var rule = await source.GetAsync(4041, CancellationToken.None);

        Assert.NotNull(rule);
        Assert.False(rule.Jobs.IsExactAll);
        Assert.Equal([QuestJobRequirement.Swordman], rule.Jobs.JobFamilies);
        Assert.True(rule.IsReadable);
        Assert.False(rule.Jobs.Allows(QuestJobRequirement.Priest));
        Assert.True(rule.Jobs.Allows(QuestJobRequirement.Swordman));
    }

    /// <remarks>
    /// The two indexes do not overlap at all -- 101,868 equipment identifiers and 34,542 stackable
    /// ones share none -- so the archive declares nothing usable under a piece of gear. That absence
    /// is how gear dragged onto a quick slot is turned away. Item 10000 is the lowest equipment
    /// identifier the archive lists.
    /// </remarks>
    [SkippableFact]
    public async Task Real_archive_answers_nothing_for_an_item_it_does_not_list_as_stackable()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfItemUseRuleDefinitionSource(provider);

        Assert.Null(await source.GetAsync(10000, CancellationToken.None));
    }

    /// <remarks>
    /// Asked twice, answered the same. The reader rewinds between tags and caches per item, and a
    /// rewind left out would make the second read of an item find its later tags already consumed.
    /// </remarks>
    [SkippableFact]
    public async Task Real_archive_answers_the_same_rule_twice()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfItemUseRuleDefinitionSource(provider);

        var first = await source.GetAsync(51, CancellationToken.None);
        var second = await source.GetAsync(51, CancellationToken.None);

        Assert.Equal(first, second);
    }

    /// <remarks>
    /// The three states of a declaration, which the reader has to keep apart: not there, there with
    /// a value, and there with nothing in it. The current archive contains no empty declaration --
    /// <c>[usable job]</c> yields 34,533 values over 34,532 files -- so this is the one case the
    /// real-file tests above cannot reach, and it is exactly the case where reading "restriction I
    /// cannot parse" as "no restriction" hands out an item the archive withheld.
    /// </remarks>
    [Fact]
    public void An_undeclared_job_is_no_restriction_and_an_empty_declaration_is_an_unreadable_one()
    {
        Assert.True(PvfItemUseRuleDefinitionSource.ProjectJobs(null).IsExactAll);
        Assert.True(PvfItemUseRuleDefinitionSource.ProjectJobs(null).IsValid);

        var empty = PvfItemUseRuleDefinitionSource.ProjectJobs([]);
        Assert.False(empty.IsExactAll);
        Assert.False(empty.IsValid);
    }

    [Fact]
    public void An_undeclared_place_is_no_restriction_and_an_empty_declaration_is_an_unreadable_one()
    {
        Assert.True(PvfItemUseRuleDefinitionSource.ProjectPlaces(null).IsUnrestricted);
        Assert.True(PvfItemUseRuleDefinitionSource.ProjectPlaces(null).IsValid);

        var empty = PvfItemUseRuleDefinitionSource.ProjectPlaces([]);
        Assert.False(empty.IsUnrestricted);
        Assert.False(empty.IsValid);
    }

    /// <remarks>
    /// The archive spells five items' job <c>[prist]</c>, twenty-one <c>[creator mage]</c>, and one
    /// each <c>[account]</c> and <c>[pall]</c>; one item's place is <c>[dont open]</c>. None is a
    /// family or a place this server models, and none may be read as permission.
    /// </remarks>
    [Fact]
    public void A_declaration_naming_something_the_server_does_not_model_is_unreadable()
    {
        Assert.False(PvfItemUseRuleDefinitionSource.ProjectJobs(["[prist]"]).IsValid);
        Assert.False(PvfItemUseRuleDefinitionSource.ProjectJobs(["[creator mage]"]).IsValid);
        Assert.False(PvfItemUseRuleDefinitionSource.ProjectPlaces(["[dont open]"]).IsValid);
    }

    /// <remarks>
    /// A scalar declaration with nothing readable behind it is the same defect as an empty section,
    /// and <c>[stackable type]</c> is where it bites hardest: read as absence it means "consumed",
    /// so an unlimited item whose type nobody could read would be destroyed -- the exact behaviour
    /// this outcome exists to stop.
    /// </remarks>
    [Fact]
    public void A_declared_but_unreadable_scalar_makes_the_whole_rule_unreadable()
    {
        var readable = new ItemUseRule(
            IsConsumedOnUse: true,
            CooldownMilliseconds: 0,
            CooltimeGroup: 0,
            MinimumLevel: 1,
            Jobs: ItemJobRestriction.Any,
            Places: ItemPlaceRestriction.Anywhere);

        Assert.True(readable.IsReadable);
        Assert.False((readable with { HasUnreadableDeclaration = true }).IsReadable);
    }

    /// <remarks>
    /// 33,879 of the 34,532 files declare <c>[all]</c>. Reading that as a concrete family list
    /// would leave every one of them restricted to nothing and therefore usable by nobody.
    /// </remarks>
    [Fact]
    public void An_exact_all_declaration_allows_every_job()
    {
        var all = PvfItemUseRuleDefinitionSource.ProjectJobs(["[all]"]);

        Assert.True(all.IsExactAll);
        Assert.True(all.IsValid);
        Assert.True(all.Allows(QuestJobRequirement.Priest));
        Assert.True(all.Allows(QuestJobRequirement.Knight));
    }
}
