using Dfo.Pvf;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// The dungeon file's per-difficulty <c>[champion]</c> rates, which decide whether a room gains a
/// random champion (#355).
/// </summary>
public sealed class DungeonChampionRatesTests
{
    [SkippableTheory]
    [InlineData(7, new[] { 3, 33, 50, 70, 80 })] // GrakKarak
    [InlineData(34, new[] { 6, 33, 50, 70, 80 })] // MagmaCave
    [InlineData(2, new[] { 0, 0, 0, 0, 0 })]
    public async Task A_dungeon_keeps_its_declared_rates_in_difficulty_order(int dungeonId, int[] rates)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var dungeon = await new PvfDungeonDefinitionSource(provider).GetAsync(dungeonId, CancellationToken.None);
        Assert.NotNull(dungeon);
        Assert.Equal(rates, dungeon!.ChampionRates);
    }

    /// <remarks>
    /// The reader rejects a malformed declaration, and a rejected dungeon cannot be entered, so the
    /// whole list is walked: no listed dungeon may fail on this field.
    /// </remarks>
    [SkippableFact]
    public async Task No_listed_dungeon_fails_on_its_champion_declaration()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);
        var failures = new List<string>();
        var declared = 0;
        foreach (var (id, _) in PvfListFile.Read(provider.Archive, "dungeon/dungeon.lst"))
        {
            try
            {
                if ((await source.GetAsync(id, CancellationToken.None))?.ChampionRates.Count > 0) declared++;
            }
            catch (InvalidDataException error) when (error.Message.Contains("[champion]", StringComparison.Ordinal))
            {
                failures.Add(error.Message);
            }
            catch (InvalidDataException)
            {
                // Other fields' failures are not this reader's concern.
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(20)));
        // 768 of the readable maze dungeons in the 0.3.3-dev PVF; mazeless families answer null.
        Assert.True(declared > 700, $"Only {declared} dungeons declared champion rates.");
    }
}
