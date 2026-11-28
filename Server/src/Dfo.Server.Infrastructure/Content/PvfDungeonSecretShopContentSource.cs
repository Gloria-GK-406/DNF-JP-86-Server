using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Content;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfDungeonSecretShopContentSource(PvfArchiveProvider provider,
    ILogger<PvfDungeonSecretShopContentSource> logger) : IDungeonSecretShopContentSource, IContentWarmup
{
    private const string Path = "etc/secretshop.etc";
    private readonly Lock gate = new();
    private DungeonSecretShopCatalog? catalog;

    public string Name => "dungeon-secret-shop";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read().Pools.Count);
    }

    public DungeonSecretShopCatalog Read()
    {
        if (!provider.IsConfigured)
            return DungeonSecretShopCatalog.Empty;
        lock (gate)
        {
            if (catalog is not null)
                return catalog;
            try
            {
                return catalog = Load(provider.Archive);
            }
            catch (InvalidDataException exception)
            {
                logger.LogWarning(exception, "Cannot load dungeon secret shop data from {Path}", Path);
                return catalog = DungeonSecretShopCatalog.Empty;
            }
        }
    }

    internal static DungeonSecretShopCatalog Load(PvfArchive archive)
    {
        var doc = PvfDocument.TryOpen(archive, Path);
        if (doc is null)
            return DungeonSecretShopCatalog.Empty;
        var levelField = PvfField.Int32List("[level section]", optional: true);
        var dungeonWeightField = PvfField.Int32List("[dungeon npc]", optional: true);
        var levelWeightField = PvfField.Int32List("[level npc]", optional: true);
        var levels = Rows(doc.Read(levelField), 3, levelField.Name)
            .Select(row => new DungeonSecretShopLevel(row[0], row[1], row[2])).ToArray();
        var dungeonWeights = Rows(doc.Read(dungeonWeightField), 3, dungeonWeightField.Name)
            .Select(row => new DungeonSecretShopNpcWeight(row[0], row[1], row[2..])).ToArray();
        var levelWeights = Rows(doc.Read(levelWeightField), 7, levelWeightField.Name)
            .Select(row => new DungeonSecretShopNpcWeight(row[0], row[1], row[2..])).ToArray();

        var pools = new List<DungeonSecretShopPool>();
        var npc = 0;
        foreach (var section in doc.Sections)
        {
            if (section.Name == "[npc]")
            {
                npc = PvfField.Int32(section.Name).Decode(archive, section.Values, Path);
                continue;
            }
            if (section.Name == "[/npc]")
            {
                npc = 0;
                continue;
            }
            if (npc == 0 || section.Name is not ("[level]" or "[dungeon]"))
                continue;

            var values = PvfField.Int32List(section.Name).Decode(archive, section.Values, Path).ToArray();
            PvfField.Require(values.Length >= 2 && values[1] >= 0, Path, section.Name, "missing or invalid pool header");
            var candidates = Rows(values[2..], 6, section.Name)
                .Where(row => row[0] > 0 && row[1] is 0 or 1 && row[2] >= 0 && row[4] > 0 && row[5] > 0
                    && (row[1] == 0 || row[2] > 0 && row[3] > 0))
                .Select(row => new DungeonSecretShopCandidate(
                    new(row[0], row[2], row[1] == 1 ? row[3] : 0, row[4]), row[5]))
                .ToArray();
            pools.Add(new(npc, values[0], section.Name == "[dungeon]", values[1], candidates));
        }
        return new(levels, dungeonWeights, levelWeights, pools);
    }

    private static IEnumerable<int[]> Rows(IReadOnlyList<int>? values, int width, string section)
    {
        if (values is null)
            return [];
        PvfField.Require(values.Count % width == 0, Path, section, "incomplete row");
        return values.Chunk(width);
    }
}
