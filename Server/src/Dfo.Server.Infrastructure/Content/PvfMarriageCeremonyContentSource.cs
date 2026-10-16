using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Marriage;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfMarriageCeremonyContentSource(PvfArchiveProvider provider)
    : IMarriageCeremonyContentSource
{
    private readonly Lazy<MarriageCeremonyContent> content = new(() =>
    {
        var document = PvfDocument.Open(provider.Archive, "etc/weddingsystem.etc");
        var values = document.Read(PvfField.Int32List("[ceremony]"));
        if (values.Count == 0 || values.Count % 4 != 0)
            throw new InvalidDataException("Wedding ceremony rules require four values per tier.");
        var tiers = Enumerable.Range(0, values.Count / 4).Select(index =>
        {
            var offset = index * 4;
            return new MarriageCeremonyTier(values[offset], values[offset + 1],
                values[offset + 2], values[offset + 3]);
        }).ToArray();
        if (tiers.Select(x => x.Tier).Distinct().Count() != tiers.Length
            || tiers.Any(x => x.Tier < 0 || x.Gold < 0)
            || tiers.All(x => x.Tier != 0))
            throw new InvalidDataException("Wedding ceremony tiers are invalid.");
        var waits = document.Read(PvfField.Int32List("[minute selection]")).ToArray();
        if (waits.Length != 3 || waits.Any(x => x <= 0))
            throw new InvalidDataException("Wedding ceremony requires three positive wait selections.");
        return new MarriageCeremonyContent(tiers, waits);
    });

    public MarriageCeremonyContent Load() => content.Value;
}
