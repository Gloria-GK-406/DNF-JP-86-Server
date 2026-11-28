using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfGuildEconomySource(PvfArchiveProvider provider) : IGuildEconomySource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private const string Path = "etc/(r)guild.etc";
    private const string MedalAuraPath = "etc/guild/insigniashapelist.etc";
    private static readonly PvfField<int> Cost = PvfField.Int32("[guild create cost]", optional: true, minimum: 1);
    private static readonly PvfField<int> Donation = PvfField.Int32("[guild donate]", optional: true, minimum: 1);

    private static readonly PvfField<IReadOnlyList<int>> Mark = PvfField.Int32List("[guild mark]", optional: true);
    private static readonly PvfField<IReadOnlyList<PvfToken>> AuraSymbols = PvfField.Tokens("[insignia mark]", optional: true);
    private static readonly PvfField<IReadOnlyList<PvfToken>> AuraFrames = PvfField.Tokens("[insignia frame]", optional: true);
    private static readonly PvfField<int> RecommendedClear = PvfField.Int32("[proper dungeon clear]", optional: true, minimum: 1);
    private static readonly PvfField<int> MonthlyTarget = PvfField.Int32("[aim]", minimum: 1);
    private static readonly PvfField<IReadOnlyList<int>> MonthlyItem = PvfField.Int32List("[item]");

    private readonly Lazy<GuildMarkPricing?> markPricing = new(() => ReadMark(provider));
    private readonly Lazy<GuildMedalAuraCatalog?> medalAuras = new(() => ReadMedalAuras(provider));
    private readonly Lazy<GuildContentCatalog?> contents = new(() =>
    {
        var archive = provider.Archive;
        var sections = PvfDocument.TryOpen(archive, Path, PvfPreamble.Ignore)?.Sections ?? [];
        return PvfGuildContentCatalog.Read(archive, sections);
    });
    private readonly Lazy<int?> cost = new(() => Read(provider, Cost));
    private readonly Lazy<int?> donation = new(() => Read(provider, Donation));
    private readonly Lazy<GuildMonthlyContributionRule?> monthly = new(() =>
        provider.IsConfigured ? ReadMonthly(provider) : null);

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

    public string Name => "guild-economy";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        var loaded = new object?[] { ReadContentCatalog(), ReadMarkPricing(), ReadMedalAuraCatalog(),
            ReadGoldCost(), ReadDonationGoldPerPoint(), ReadMonthlyContribution() };
        return ValueTask.FromResult(loaded.Count(value => value is not null));
    }

    public GuildContentCatalog? ReadContentCatalog() => contents.Value;
    public GuildMarkPricing? ReadMarkPricing() => markPricing.Value;
    public GuildMedalAuraCatalog? ReadMedalAuraCatalog() => medalAuras.Value;
    public int? ReadGoldCost() => cost.Value;
    public int? ReadDonationGoldPerPoint() => donation.Value;
    public GuildMonthlyContributionRule? ReadMonthlyContribution() => monthly.Value;

    private static GuildMedalAuraCatalog? ReadMedalAuras(PvfArchiveProvider provider)
    {
        var sections = PvfDocument.TryOpen(provider.Archive, MedalAuraPath)?.Sections ?? [];
        var symbols = PvfSections.Single(sections, AuraSymbols, PvfRepeats.AsUndeclared, MedalAuraPath)?.Values.Count ?? 0;
        var frames = PvfSections.Single(sections, AuraFrames, PvfRepeats.AsUndeclared, MedalAuraPath)?.Values.Count ?? 0;
        return symbols > 0 && frames > 0 ? new(symbols, frames) : null;
    }

    private static GuildMonthlyContributionRule? ReadMonthly(PvfArchiveProvider provider)
    {
        var archive = provider.Archive;
        var sections = PvfDocument.TryOpen(archive, Path, PvfPreamble.Ignore)?.Sections ?? [];
        var rewardBlock = sections.SkipWhile(section => section.Name != "[guild member reward]").Skip(1)
            .TakeWhile(section => section.Name != "[/guild member reward]").ToArray();
        if (rewardBlock.Length == 0)
            return null;

        var clear = PvfSections.SingleInt32(archive, sections, RecommendedClear, PvfRepeats.AsUndeclared, Path);
        var target = PvfSections.SingleInt32(archive, rewardBlock, MonthlyTarget, PvfRepeats.AsUndeclared, Path);
        var section = PvfSections.Single(rewardBlock, MonthlyItem, PvfRepeats.AsUndeclared, Path)!;
        var item = MonthlyItem.Decode(archive, section.Values, Path);
        PvfField.Require(clear is > 0 && target is > 0 && item is [> 0, > 0], Path,
            "guild member reward", "expected a target, a positive clear score and a reward item with quantity");
        return new(clear!.Value, target!.Value, item[0], item[1]);
    }
}
