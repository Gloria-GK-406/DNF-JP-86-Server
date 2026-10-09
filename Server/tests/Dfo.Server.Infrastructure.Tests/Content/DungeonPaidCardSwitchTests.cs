using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// The dungeon's own switch for the paid card, read out of the archive.
/// </summary>
/// <remarks>
/// All five dungeons that declare <c>[gold card use]</c> are ordinary maze dungeons this
/// server reads, which is what separates this declaration from the face declaration beside it --
/// see <see cref="DungeonResultCardDeclarationTests"/>, which pins which dungeons declare which of
/// the two and which of them this server can reach at all.
/// <para>
/// Both directions are asserted here and that is the point of the file. "Reads the switch" and
/// "leaves every other dungeon alone" are two claims, and the second is the one a change to this
/// reader would break silently: a reader that answered false for everything would satisfy the
/// first claim on its own.
/// </para>
/// </remarks>
public sealed class DungeonPaidCardSwitchTests
{
    /// <summary>
    /// The dungeons that declare <c>[gold card use] 0</c> and are readable through this source.
    /// </summary>
    public static TheoryData<int> SwitchedOff => [100, 3503, 3700, 3901, 3902];

    /// <summary>
    /// Dungeons that declare no <c>[gold card use]</c> at all, which is 1042 of the 1047 listed.
    /// </summary>
    /// <remarks>
    /// 144 is the dungeon this subject's captures were taken in; 10000 and 10004 are the two
    /// tutorials, which the entry path has to work for first. Three rather than one, because the
    /// claim is about every dungeon that says nothing and one sample cannot carry it.
    /// </remarks>
    public static TheoryData<int> Untouched => [144, 10000, 10004];

    [SkippableTheory]
    [MemberData(nameof(SwitchedOff))]
    public async Task A_dungeon_declaring_the_switch_offers_no_paid_card(int dungeonId)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);

        var dungeon = await source.GetAsync(dungeonId, CancellationToken.None);

        Assert.NotNull(dungeon);
        Assert.False(dungeon!.OffersPaidCard);
    }

    /// <remarks>
    /// The other half of the reading, and the red line of the change that introduced it: a dungeon
    /// that declares nothing about the paid card is left exactly as it was.
    /// </remarks>
    [SkippableTheory]
    [MemberData(nameof(Untouched))]
    public async Task A_dungeon_declaring_nothing_still_offers_the_paid_card(int dungeonId)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);

        var dungeon = await source.GetAsync(dungeonId, CancellationToken.None);

        Assert.NotNull(dungeon);
        Assert.True(dungeon!.OffersPaidCard);
    }

    /// <summary>
    /// Reading the switch changed nothing else the dungeon file declares.
    /// </summary>
    /// <remarks>
    /// <c>[gold card use]</c> sits at file scope among the tags this reader already walks past, so
    /// the way it could go wrong is not by being misread but by consuming an integer that belonged
    /// to a neighbour. Dungeon 144's other readings are asserted against fixed figures elsewhere;
    /// what is asserted here is that the dungeon which declares the switch still reads as the same
    /// dungeon in every other respect a run depends on.
    /// </remarks>
    [SkippableFact]
    public async Task Reading_the_switch_leaves_the_rest_of_the_dungeon_alone()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);

        var dungeon = await source.GetAsync(100, CancellationToken.None);

        Assert.NotNull(dungeon);
        Assert.False(dungeon!.OffersPaidCard);
        Assert.Equal("dungeon/SouthernDale/Grimseeker.dgn", dungeon.ArchivePath);
        Assert.NotEmpty(dungeon.Mazes);
        Assert.All(dungeon.Mazes, maze => Assert.NotEmpty(maze.Cells));
        Assert.True(dungeon.MinimumRequiredLevel > 0);
        Assert.True(dungeon.BasisLevel > 0);
        Assert.True(dungeon.ExperienceWeight > 0);
    }
}
