using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Marriage;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfMarriageCapsuleContentSource(PvfArchiveProvider provider) : IMarriageCapsuleContentSource
{
    private readonly Lazy<MarriageCapsuleRules> rules = new(() =>
    {
        var d = PvfDocument.Open(provider.Archive, "etc/couplegrowthcapsule.etc");
        var r = new MarriageCapsuleRules(d.Read(PvfField.Int32("[max own count]")), d.Read(PvfField.Int32("[max taken count]")),
            d.Read(PvfField.Int32("[gage]")), d.Read(PvfField.Int32("[max gage]")), d.Read(PvfField.Int32("[capsule id]")));
        if (r.MaximumOwned is < 1 or > 255 || r.MaximumPending is < 1 or > 255 || r.Gain <= 0 || r.MaximumEnergy < r.Gain || r.ItemId <= 0)
            throw new InvalidDataException("Invalid marriage capsule configuration.");
        return r;
    });
    public MarriageCapsuleRules Load() => rules.Value;
}
