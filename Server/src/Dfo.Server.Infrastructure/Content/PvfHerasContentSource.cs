using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfHerasContentSource(PvfArchiveProvider provider, IDungeonDefinitionSource dungeons) : IHerasContentSource
{
    public async ValueTask<HerasDefinition> ReadAsync(CancellationToken ct)
    {
        var dungeon = await dungeons.GetAsync(3900, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Missing Heras dungeon.");
        var root = PvfDocument.Open(provider.Archive, dungeon.ArchivePath).AtRoot();
        int Read(string name) => root.Read(PvfField.Int32(name));
        if (Read("[limit party count]") != 1 || Read("[mob level charac level replace flag]") != 1
            || Read("[designate dungeon difficulty]") != 1 || Read("[schedule]") != 2367
            || dungeon.ConsumesFatigue || dungeon.RequiredItems.Count != 0 || dungeon.MinimumRequiredLevel != 50
            || dungeon.Mazes.Count != 1 || dungeon.Mazes[0].Width != 3 || dungeon.Mazes[0].Height != 3
            || dungeon.Mazes[0].Cells.Count != 9 || dungeon.Mazes[0].ConfigurationError is not null)
            throw new InvalidDataException("Unsupported Heras 3x3 declaration.");
        var values = PvfDocument.Open(provider.Archive, "etc/dungeonetc/dungeonincountinfo.etc")
            .Read(PvfField.Int32List("[dungeon incount info]"));
        if (values.Count % 4 != 0) throw new InvalidDataException("Truncated dungeon entry counts.");
        var rows = Enumerable.Range(0, values.Count / 4).Select(x => x * 4).Where(x => values[x + 1] == 3900).ToArray();
        if (rows.Length != 1 || values[rows[0] + 2] is < 1 or > byte.MaxValue)
            throw new InvalidDataException("Invalid Heras entry count.");
        return new(dungeon, values[rows[0] + 2]);
    }
}
