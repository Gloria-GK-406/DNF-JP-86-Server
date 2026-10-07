using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// Which dungeons declare a card face of their own, and which of those this server can reach.
/// </summary>
/// <remarks>
/// Two declarations exist and they are not the same size of thing. <c>[result card]</c> names the
/// whole face -- a nested <c>[reward item rate]</c> of <c>itemId weight count</c> rows, then rows
/// of <c>key goldWeight itemWeight emptyWeight</c> -- and <c>[gold card use]</c> is a single
/// integer. The field names are the reference server's, not the archive's: it reads the same two
/// sections in <c>Tool/PvfLib/Models/DungeonFile.cs</c> (<c>TournamentResultCard</c>,
/// <c>TournamentRewardItemRate</c>) and consumes them in
/// <c>Game/Dungeon/Tournament/TournamentDungeonApplicationService.cs</c>.
/// <para>Structure declarations are readable even without [map specification]. This does not
/// imply the ordinary entry service implements tournament gameplay or its card rewards.</para>
/// </remarks>
public sealed class DungeonResultCardDeclarationTests
{
    private const string ListPath = "dungeon/dungeon.lst";
    private const string DungeonDirectory = "dungeon";

    /// <summary>An ordinary dungeon that declares neither, kept here as the negative control.</summary>
    /// <remarks>
    /// Dungeon 144 is the one this subject's captures were taken in, so it is the dungeon whose
    /// unchanged behaviour the global table has to keep answering for.
    /// </remarks>
    private const int OrdinaryDungeonId = 144;

    private static readonly Lazy<ArchiveDeclarations> Declarations = new(Scan);

    [SkippableFact]
    public async Task Result_card_dungeons_have_implicit_structure_and_require_special_entry()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var declared = Declarations.Value.ResultCard;

        Assert.Equal([120, 121, 122, 4119], declared);
        Assert.DoesNotContain(OrdinaryDungeonId, declared);

        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);
        foreach (var dungeonId in declared)
        {
            var dungeon = await source.GetAsync(dungeonId, CancellationToken.None);
            Assert.NotNull(dungeon);
            Assert.NotEmpty(dungeon.Mazes);
            Assert.Equal(Dfo.Server.Abstractions.Content.DungeonEntryMode.Tournament, dungeon.EntryMode);
        }

        // The control has to be reachable, or "absent" would be saying nothing about the four.
        Assert.NotNull(await source.GetAsync(OrdinaryDungeonId, CancellationToken.None));
    }

    [SkippableFact]
    public async Task The_dungeons_switching_the_paid_card_off_are_mostly_reachable()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var switched = Declarations.Value.GoldCardUse;

        Assert.Equal([100, 3503, 3700, 3901, 3902], switched.Keys);
        Assert.All(switched.Values, value => Assert.Equal(0, value));
        Assert.DoesNotContain(OrdinaryDungeonId, switched.Keys);

        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);
        var reachable = new List<int>();
        foreach (var dungeonId in switched.Keys)
        {
            if (await source.GetAsync(dungeonId, CancellationToken.None) is not null)
                reachable.Add(dungeonId);
        }

        // 3503 has occupied implicit cells; its switch is now readable too.
        Assert.Equal([100, 3503, 3700, 3901, 3902], reachable);
    }

    /// <summary>
    /// One pass over every listed dungeon, collecting both declarations.
    /// </summary>
    /// <remarks>
    /// Deliberately a scan rather than a list of expected ids checked one at a time: the claim
    /// being pinned is that <b>no other</b> dungeon declares a face, and a spot check of four
    /// known ids could not tell that from a fifth having been missed.
    /// <para>
    /// The path table is built the way <see cref="PvfDungeonDefinitionSource"/> builds it, first
    /// entry winning, so a duplicated identifier is read out of the same file both agree on.
    /// </para>
    /// </remarks>
    private static ArchiveDeclarations Scan()
    {
        using var provider = ContentArchive.CreateProvider();
        var archive = provider.Archive;
        var resultCardTag = archive.GetTag(PvfField.Tokens("[result card]"));
        var goldCardUseTag = archive.GetTag(PvfField.Tokens("[gold card use]"));
        Assert.True(resultCardTag.IsResolved, "the archive interns no [result card] tag");
        Assert.True(goldCardUseTag.IsResolved, "the archive interns no [gold card use] tag");

        var paths = new Dictionary<int, string>();
        foreach (var (dungeonId, relativePath) in PvfListFile.Read(archive, ListPath))
            paths.TryAdd(dungeonId, relativePath);

        var resultCard = new SortedSet<int>();
        var goldCardUse = new SortedDictionary<int, int>();
        foreach (var (dungeonId, relativePath) in paths)
        {
            var fileIndex = archive.FindFile($"{DungeonDirectory}/{relativePath}");
            if (fileIndex < 0)
                continue;

            ReadOne(archive, fileIndex, dungeonId, resultCardTag, goldCardUseTag, resultCard, goldCardUse);
        }

        return new ArchiveDeclarations(resultCard, goldCardUse);
    }

    /// <remarks>
    /// Separate from <see cref="Scan"/> because <see cref="PvfTokenReader"/> is a ref struct and
    /// may not be held across anything that could suspend.
    /// </remarks>
    private static void ReadOne(
        PvfArchive archive,
        int fileIndex,
        int dungeonId,
        TagRef resultCardTag,
        TagRef goldCardUseTag,
        SortedSet<int> resultCard,
        SortedDictionary<int, int> goldCardUse)
    {
        var reader = archive.OpenScript(fileIndex);
        var awaitingSwitch = false;
        while (reader.Read(out var token))
        {
            if (awaitingSwitch)
            {
                awaitingSwitch = false;
                if (token.Kind == PvfTokenKind.Int32)
                {
                    goldCardUse[dungeonId] = token.Value;
                    continue;
                }
            }

            if (resultCardTag.Matches(token))
                resultCard.Add(dungeonId);
            else if (goldCardUseTag.Matches(token))
                awaitingSwitch = true;
        }
    }

    private sealed record ArchiveDeclarations(
        SortedSet<int> ResultCard, SortedDictionary<int, int> GoldCardUse);
}
