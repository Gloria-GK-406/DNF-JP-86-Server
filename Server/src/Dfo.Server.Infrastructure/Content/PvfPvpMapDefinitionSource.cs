using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Pvp;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfPvpMapDefinitionSource(PvfArchiveProvider provider) : IPvpMapDefinitionSource, IContentWarmup
{
    private const string Path = "etc/pvpmapbasicdata.etc";
    private readonly Lazy<PvpMapCatalog> catalog = new(() => Read(provider));
    public PvpMapCatalog Load() => catalog.Value;
    public string Name => "pvp-map";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Load().Order.Count);

    private static PvpMapCatalog Read(PvfArchiveProvider provider)
    {
        var root = PvfDocument.Open(provider.Archive, Path).AtRoot();
        var order = root.Read(PvfField.Int32List("[pvp map order]"));
        // A room's map index travels as one byte, and index 0 alone would leave nothing to draw from.
        PvfField.Require(order.Count is >= 2 and <= byte.MaxValue, Path, "[pvp map order]", "expected 2..255 maps");
        PvfField.Require(order.Distinct().Count() == order.Count, Path, "[pvp map order]", "duplicate map");
        var described = root.ReadAll(PvfField.Int32List("[pvp map basic data]"));
        foreach (var row in described)
            PvfField.Require(row.Count == 7, Path, "[pvp map basic data]", "expected seven values per map");
        var maps = described.Select(row => row[0]).ToHashSet();
        PvfField.Require(order.All(maps.Contains), Path, "[pvp map order]", "every ordered map needs basic data");
        return new(order);
    }
}
