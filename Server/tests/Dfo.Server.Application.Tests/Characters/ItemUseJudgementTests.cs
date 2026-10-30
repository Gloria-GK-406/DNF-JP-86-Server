using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Tests.Inventory;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// The archive's declaration judged against a candidate, with nothing that could find a character.
/// </summary>
/// <remarks>
/// The point of these is what they do not need. There is no session, no directory, no database and
/// no character here -- every fact arrives as a value, and the whole judgement is a function of
/// them. That is the separation R-6 asks for, and it is checkable only by writing tests that could
/// not have been written before it.
///
/// What a character is and where it stands is covered where the character is; this covers the
/// comparisons and the order they are made in.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ItemUseJudgementTests
{
    private static readonly IItemUseRuleService Judgement =
        new ItemUseRuleService(new NoItemUseRules());

    /// <summary>These tests hand the judgement a declaration; none of them looks one up.</summary>
    private sealed class NoItemUseRules : IItemUseRuleDefinitionSource
    {
        public ValueTask<ItemUseRule?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>An ordinary consumable with no restriction, and a candidate who meets them all.</summary>
    private static ItemUseCandidate Ready(
        byte level = 50,
        QuestJobRequirement job = QuestJobRequirement.Priest,
        ItemUsePlace place = ItemUsePlace.Village,
        bool coolingDown = false) =>
        new(level, job, place, coolingDown);

    [Fact]
    public void A_candidate_meeting_every_restriction_is_allowed() =>
        Assert.Equal(
            ItemUsability.Allowed,
            Judgement.Judge(ItemUseRules.Potion(cooldownMilliseconds: 0), Ready()));

    [Fact]
    public void A_level_below_the_declared_minimum_is_refused() =>
        Assert.Equal(
            ItemUsability.LevelTooLow,
            Judgement.Judge(ItemUseRules.RequiringLevel(20), Ready(level: 19)));

    [Fact]
    public void A_job_outside_the_declared_families_is_refused() =>
        Assert.Equal(
            ItemUsability.JobMismatch,
            Judgement.Judge(
                ItemUseRules.RequiringJob(QuestJobRequirement.Swordman),
                Ready(job: QuestJobRequirement.Priest)));

    [Fact]
    public void A_place_outside_the_declared_ones_is_refused() =>
        Assert.Equal(
            ItemUsability.PlaceNotAllowed,
            Judgement.Judge(
                ItemUseRules.UsableIn(ItemUsePlace.Dungeon), Ready(place: ItemUsePlace.Village)));

    [Fact]
    public void A_candidate_still_waiting_is_refused() =>
        Assert.Equal(
            ItemUsability.OnCooldown,
            Judgement.Judge(ItemUseRules.Potion(), Ready(coolingDown: true)));

    /// <remarks>
    /// The order is the answer as much as the comparisons are, and here it can be stated without
    /// constructing a character who is simultaneously too low a level and mid-cooldown: both facts
    /// are just values.
    /// </remarks>
    [Fact]
    public void A_permanent_reason_wins_over_one_that_will_expire() =>
        Assert.Equal(
            ItemUsability.LevelTooLow,
            Judgement.Judge(
                // A cooldown of its own, or the cooldown half of this could never be reached and
                // the pair would be stated without being tested. An independent review found it
                // that way once, in the very change that introduced the gate.
                ItemUseRules.RequiringLevel(50) with { CooldownMilliseconds = 3_000 },
                Ready(level: 10, coolingDown: true)));

    [Fact]
    public void A_job_mismatch_wins_over_a_place_that_is_also_wrong()
    {
        var rule = ItemUseRules.RequiringJob(QuestJobRequirement.Swordman) with
        {
            Places = new ItemPlaceRestriction(IsUnrestricted: false, [ItemUsePlace.Dungeon]),
        };

        Assert.Equal(
            ItemUsability.JobMismatch,
            Judgement.Judge(rule, Ready(job: QuestJobRequirement.Priest, place: ItemUsePlace.Village)));
    }

    /// <summary>
    /// The remaining pairs of the order, stated outright.
    /// </summary>
    /// <remarks>
    /// Two of the six pairs were covered and four were not, so four of the ways the order could be
    /// rearranged left the suite green. Written as values because that is what the separation buys:
    /// a character who is simultaneously the wrong job, in the wrong place, under-levelled and
    /// mid-cooldown is a fixture nobody wants to build, and here it is four arguments.
    ///
    /// Six rows for six pairs. The first pass had five: every row that was under-levelled was also
    /// not waiting, so level-against-cooldown was stated in the summary and tested nowhere -- which
    /// is the same mistake, one level up, as the one this whole Theory exists to catch.
    /// </remarks>
    [Theory]
    [InlineData(10, QuestJobRequirement.Priest, ItemUsePlace.Dungeon, true, ItemUsability.LevelTooLow)]
    [InlineData(10, QuestJobRequirement.Priest, ItemUsePlace.Village, false, ItemUsability.LevelTooLow)]
    [InlineData(10, QuestJobRequirement.Swordman, ItemUsePlace.Village, false, ItemUsability.LevelTooLow)]
    [InlineData(50, QuestJobRequirement.Swordman, ItemUsePlace.Village, true, ItemUsability.JobMismatch)]
    [InlineData(50, QuestJobRequirement.Priest, ItemUsePlace.Village, true, ItemUsability.PlaceNotAllowed)]
    [InlineData(50, QuestJobRequirement.Priest, ItemUsePlace.Dungeon, true, ItemUsability.OnCooldown)]
    [InlineData(50, QuestJobRequirement.Priest, ItemUsePlace.Dungeon, false, ItemUsability.Allowed)]
    public void Every_pair_of_reasons_is_reported_in_the_settled_order(
        byte level,
        QuestJobRequirement job,
        ItemUsePlace place,
        bool coolingDown,
        ItemUsability expected)
    {
        // Restricted on all four counts at once: level 50, priests only, dungeons only, and with a
        // cooldown of its own. Each row above relaxes them one at a time, in the order the answers
        // are meant to come out in.
        var rule = ItemUseRules.RequiringLevel(50) with
        {
            Jobs = new ItemJobRestriction(IsExactAll: false, [QuestJobRequirement.Priest]),
            Places = new ItemPlaceRestriction(IsUnrestricted: false, [ItemUsePlace.Dungeon]),
            CooldownMilliseconds = 3_000,
        };

        Assert.Equal(expected, Judgement.Judge(rule, new ItemUseCandidate(level, job, place, coolingDown)));
    }

    /// <remarks>
    /// A declaration with no cooldown of its own cannot be waiting, whatever the register says about
    /// a key it would have shared. This lived beside the lookup until an independent review pointed
    /// out that the lookup and the judgement then answered differently about one declaration.
    /// </remarks>
    [Fact]
    public void A_declaration_with_no_cooldown_is_never_waiting() =>
        Assert.Equal(
            ItemUsability.Allowed,
            Judgement.Judge(
                ItemUseRules.Potion(cooldownMilliseconds: 0, cooltimeGroup: 70),
                Ready(coolingDown: true)));

    /// <remarks>
    /// A group of zero means "shares with nothing", so the key is the item's own. Keyed on the bare
    /// integer instead, using item 70 would silence every item in cooldown group 70.
    /// </remarks>
    [Fact]
    public void An_item_sharing_no_group_waits_under_its_own_identifier() =>
        Assert.Equal(
            ItemCooldownKey.ForItem(8474),
            Judgement.CooldownKeyFor(8474, ItemUseRules.Potion(cooltimeGroup: 0)));

    [Fact]
    public void An_item_declaring_a_group_waits_under_the_group() =>
        Assert.Equal(
            ItemCooldownKey.ForGroup(70),
            Judgement.CooldownKeyFor(8474, ItemUseRules.Potion(3_000, cooltimeGroup: 70)));
}
