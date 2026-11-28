using System.Collections.Frozen;
using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Events;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfFatigueBurningDungeonContentSource(PvfArchiveProvider provider)
    : IFatigueBurningDungeonContentSource
{
    private static readonly PvfField<IReadOnlyList<int>> UnableDungeon = PvfField.Int32List("[unable dungeon]");
    private readonly Lazy<IReadOnlySet<int>> prohibited = new(() =>
        PvfDocument.Open(provider.Archive, "etc/chn_fatigueaccelchkdungeon.etc")
            .Read(UnableDungeon).ToFrozenSet());

    public IReadOnlySet<int> LoadProhibitedDungeons() => prohibited.Value;
}
