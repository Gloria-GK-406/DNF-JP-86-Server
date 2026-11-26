using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Guilds;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfGuildEconomySource(PvfArchiveProvider provider) : IGuildEconomySource
{
    private const string Path = "etc/(r)guild.etc";
    private static readonly PvfField<int> Cost = PvfField.Int32("[guild create cost]", optional: true, minimum: 1);
    private static readonly PvfField<int> Donation = PvfField.Int32("[guild donate]", optional: true, minimum: 1);

    private readonly Lazy<int?> cost = new(() => Read(provider, Cost));
    private readonly Lazy<int?> donation = new(() => Read(provider, Donation));

    private static int? Read(PvfArchiveProvider provider, PvfField<int> field)
    {
        var archive = provider.Archive;
        var sections = PvfDocument.TryOpen(archive, Path, PvfPreamble.Ignore)?.Sections ?? [];
        return PvfSections.SingleInt32(archive, sections, field, PvfRepeats.AsUndeclared, Path);
    }

    public int? ReadGoldCost() => cost.Value;
    public int? ReadDonationGoldPerPoint() => donation.Value;
}
