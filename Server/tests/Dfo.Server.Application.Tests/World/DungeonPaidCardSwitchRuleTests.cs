using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// The rule service answering, off the real archive, whether a dungeon offers a paid flip.
/// </summary>
/// <remarks>
/// The seam between the dungeon file and the price. What the file says is asserted in the
/// infrastructure tests; what a clear costs is asserted in <see cref="ClearRewardArchiveTests"/>.
/// This is the one place where the two meet, and the thing it has to pin is that they meet in one
/// direction only: a dungeon that switched its card off names no price, and every other dungeon
/// is priced exactly as it was before the switch existed.
/// </remarks>
public sealed class DungeonPaidCardSwitchRuleTests
{
    /// <summary>
    /// A level the archive prices a flip at, asked of the price as a dungeon's own basis level.
    /// </summary>
    private const int Level = 86;

    /// <summary>The dungeons that declare the switch and that this server can read.</summary>
    public static TheoryData<int> SwitchedOff => [100, 3700, 3901, 3902];

    /// <summary>Dungeons that declare no switch: this subject's own, and the two tutorials.</summary>
    public static TheoryData<int> Untouched => [144, 10000, 10004];

    [SkippableTheory]
    [MemberData(nameof(SwitchedOff))]
    public async Task A_dungeon_that_switched_its_paid_card_off_offers_no_flip(int dungeonId)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();

        Assert.False(await Rules(provider).OffersPaidFlipAsync(dungeonId, CancellationToken.None));
    }

    /// <summary>
    /// <b>The red line: a dungeon that declares nothing is answered exactly as before.</b>
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(Untouched))]
    public async Task A_dungeon_that_declares_nothing_still_offers_a_flip(int dungeonId)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();

        Assert.True(await Rules(provider).OffersPaidFlipAsync(dungeonId, CancellationToken.None));
    }

    /// <summary>
    /// A dungeon this server has no definition for is offered a flip, not refused one.
    /// </summary>
    /// <remarks>
    /// Two ways to have no definition and both answer the same: an identifier the archive's list
    /// does not name at all, and 3503 -- which <b>does</b> declare <c>[gold card use] 0</c> but
    /// declares a maze with no rooms, so the source answers nothing for it exactly as it does for
    /// the four dungeons that declare a face of their own.
    /// <para>
    /// <b>So 3503's own switch is not honoured, and that is recorded rather than hidden.</b> It
    /// cannot be observed -- a dungeon with no definition gets no layout and so no run, and a
    /// clear is what a paid flip belongs to -- but the reading is nonetheless incomplete, and this
    /// assertion is what will go red on the day maze-less dungeons become readable.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task A_dungeon_with_no_definition_is_not_treated_as_switched_off()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();
        var rules = Rules(provider);

        Assert.True(await rules.OffersPaidFlipAsync(999_999, CancellationToken.None));
        Assert.True(await rules.OffersPaidFlipAsync(3503, CancellationToken.None));

        // And the reason it answers that way is the missing definition, not a missing tag.
        Assert.Null(
            await provider.GetRequiredService<IDungeonDefinitionSource>()
                .GetAsync(3503, CancellationToken.None));
    }

    /// <summary>
    /// <b>The switch decides whether there is a flip, and never what one costs.</b>
    /// </summary>
    /// <remarks>
    /// The archive's <c>[gold card cost table]</c> is keyed on a level and on nothing else. Which
    /// level that is did change -- it is the dungeon's own <c>[basis level]</c> rather than the
    /// character's, see <see cref="IClearRewardRuleService.PaidFlipCost"/> -- so the price does
    /// now vary by dungeon; what it still cannot vary by is this switch. Two dungeons set at the
    /// same level are priced alike whether one of them has closed its card or not, and what
    /// differs is only whether that figure is ever reached, which is decided one seam further out
    /// where the settlement is composed.
    /// </remarks>
    [SkippableFact]
    public async Task The_switch_does_not_move_the_price()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();
        var rules = Rules(provider);
        var dungeons = provider.GetRequiredService<IDungeonDefinitionSource>();

        var priced = rules.PaidFlipCost(Level);

        Assert.NotNull(priced);
        Assert.True(priced > 0);
        Assert.False(await rules.OffersPaidFlipAsync(100, CancellationToken.None));
        Assert.True(await rules.OffersPaidFlipAsync(144, CancellationToken.None));

        // Same price on both sides of the switch: the price takes a level, and this question
        // takes a dungeon, and neither reaches into the other.
        Assert.Equal(priced, rules.PaidFlipCost(Level));
        Assert.Equal(10300, priced);

        // And a closed dungeon's own level is priced exactly as any open dungeon at that level
        // would be -- 100 is closed, 144 is not, and each is priced by the level it declares.
        var closed = await dungeons.GetAsync(100, CancellationToken.None);
        var open = await dungeons.GetAsync(144, CancellationToken.None);
        Assert.NotNull(closed);
        Assert.NotNull(open);
        Assert.Equal(5, open.BasisLevel);
        Assert.Equal(580, rules.PaidFlipCost(open.BasisLevel));

        // The closed one is priced too -- the table has a row for its level like any other -- so
        // its refusal is the switch's doing and not the price running out.
        Assert.NotNull(rules.PaidFlipCost(closed.BasisLevel));
    }

    private static ClearRewardRuleService Rules(ServiceProvider provider) =>
        new(
            provider.GetRequiredService<IClearRewardRateSource>(),
            provider.GetRequiredService<IGoldDropTableSource>(),
            provider.GetRequiredService<IDropItemPoolSource>(),
            provider.GetRequiredService<IDungeonDefinitionSource>(),
            new ClearRewardRateOptions());
}
