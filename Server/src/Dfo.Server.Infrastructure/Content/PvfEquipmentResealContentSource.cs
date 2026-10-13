using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfEquipmentResealContentSource(PvfArchiveProvider provider) : IEquipmentResealContentSource
{
    private readonly Lock gate = new();
    private EquipmentResealTable? cached;
    public EquipmentResealTable Load()
    {
        lock (gate)
        {
            if (cached is not null) return cached;
            var document = PvfDocument.Open(provider.Archive, "etc/reseal.etc").AtRoot();
            var bands = document.Read(PvfField.Int32List("[grade]"));
            var rarity = document.Read(PvfField.Int32List("[rarity weight]"));
            var repeat = document.Read(PvfField.Int32List("[extended rarity weight]"));
            var wax = PvfDocument.Open(provider.Archive, "stackable/cash/candle_goldwax.stk").AtRoot();
            var allowed = wax.Read(PvfField.Int32List("[Rarity possible explain]"));
            if (bands.Count == 0 || bands.Any(x => x <= 0) || rarity.Count != 7 || repeat.Count != 7
                || allowed.Count != 7 || rarity.Any(x => x <= 0) || repeat.Any(x => x < 0)
                || allowed.Any(x => x is < 0 or > 1) || !allowed.Contains(1))
                throw new InvalidDataException("PVF etc/reseal.etc or candle_goldwax.stk: invalid reseal costs or rarity restrictions.");
            return cached = new(bands.ToArray(), rarity.ToArray(), repeat.ToArray(),
                allowed.Select((value, index) => (value, index)).Where(x => x.value == 1).Select(x => x.index).ToArray());
        }
    }
}
