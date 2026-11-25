using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Guilds;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfGuildCreationCostSource(PvfArchiveProvider provider) : IGuildCreationCostSource
{
    private const string Path = "etc/(r)guild.etc";
    private static readonly PvfField<int> Cost = PvfField.Int32("[guild create cost]", optional: true, minimum: 1);

    private readonly Lazy<int?> cost = new(() =>
    {
        var archive = provider.Archive;
        var sections = PvfDocument.TryOpen(archive, Path, PvfPreamble.Ignore)?.Sections ?? [];
        return PvfSections.SingleInt32(archive, sections, Cost, PvfRepeats.AsUndeclared, Path);
    });

    public int? ReadGoldCost() => cost.Value;
}
