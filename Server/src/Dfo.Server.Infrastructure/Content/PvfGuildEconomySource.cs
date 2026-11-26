using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfGuildEconomySource(PvfArchiveProvider provider) : IGuildEconomySource
{
    private const string Path = "etc/(r)guild.etc";
    private static readonly PvfField<int> Cost = PvfField.Int32("[guild create cost]", optional: true, minimum: 1);
    private static readonly PvfField<int> Donation = PvfField.Int32("[guild donate]", optional: true, minimum: 1);

    private static readonly PvfField<IReadOnlyList<int>> Mark = PvfField.Int32List("[guild mark]", optional: true);

    private readonly Lazy<GuildMarkPricing?> markPricing = new(() => ReadMark(provider));
    private readonly Lazy<int?> cost = new(() => Read(provider, Cost));
    private readonly Lazy<int?> donation = new(() => Read(provider, Donation));

    private static int? Read(PvfArchiveProvider provider, PvfField<int> field)
    {
        var archive = provider.Archive;
        var sections = PvfDocument.TryOpen(archive, Path, PvfPreamble.Ignore)?.Sections ?? [];
        return PvfSections.SingleInt32(archive, sections, field, PvfRepeats.AsUndeclared, Path);
    }

    private static GuildMarkPricing? ReadMark(PvfArchiveProvider provider)
    {
        var archive = provider.Archive;
        var sections = PvfDocument.TryOpen(archive, Path, PvfPreamble.Ignore)?.Sections ?? [];
        if (PvfSections.Single(sections, Mark, PvfRepeats.AsUndeclared, Path) is not { Values: [var first, var price] }
            || first.Kind != PvfTokenKind.Int32 || price.Kind != PvfTokenKind.Int32
            || first.Value is < 0 or > GuildMarkPricing.MarkCount || price.Value <= 0)
            return null;
        return new GuildMarkPricing(first.Value, price.Value);
    }

    public GuildMarkPricing? ReadMarkPricing() => markPricing.Value;
    public int? ReadGoldCost() => cost.Value;
    public int? ReadDonationGoldPerPoint() => donation.Value;
}
