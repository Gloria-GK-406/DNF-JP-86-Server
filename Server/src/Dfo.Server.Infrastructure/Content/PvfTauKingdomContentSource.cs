using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Pvf.Schema;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfTauKingdomContentSource(PvfArchiveProvider provider, IDungeonDefinitionSource dungeons)
    : ITauKingdomContentSource
{
    public async ValueTask<TauKingdomDefinition> ReadAsync(CancellationToken ct)
    {
        var dungeon = await dungeons.GetAsync(3700, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Missing Tau Kingdom dungeon.");
        var sections = (PvfDocument.TryOpen(provider.Archive, dungeon.ArchivePath, PvfPreamble.Ignore)?.Sections ?? []);
        int Read(string name)
        {
            var matches = sections.Where(x => x.Name == name).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Missing or duplicate Tau Kingdom {name}.");
            var values = matches[0].Values;
            if (values.Count != 1 || values[0].Kind != PvfTokenKind.Int32)
                throw new InvalidDataException($"Invalid Tau Kingdom {name}.");
            return values[0].Value;
        }
        var party = Read("[limit party count]");
        var adaptive = Read("[mob level charac level replace flag]");
        if (party != 1 || adaptive != 1 || dungeon.ConsumesFatigue
            || dungeon.RequiredItems.Count != 1 || dungeon.RequiredItems[0] != new DungeonRequiredItem(690001556, 1, true))
            throw new InvalidDataException("Unsupported Tau Kingdom admission declaration.");
        return new(dungeon, party, adaptive == 1);
    }
}
