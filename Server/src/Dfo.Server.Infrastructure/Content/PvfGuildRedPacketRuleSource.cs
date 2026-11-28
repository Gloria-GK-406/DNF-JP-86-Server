using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfGuildRedPacketRuleSource(PvfArchiveProvider provider) : IGuildRedPacketRuleSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private const string Path = "etc/chn_hongbao/chn_hongbao.etc";
    private readonly Lazy<GuildRedPacketRules?> rules = new(() => Load(provider));

    public string Name => "guild-red-packets";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Read()?.Products.Count ?? 0);

    public GuildRedPacketRules? Read() => rules.Value;

    private static GuildRedPacketRules? Load(PvfArchiveProvider provider)
    {
        var archive = provider.Archive;
        if (PvfDocument.TryOpen(archive, Path, PvfPreamble.Ignore)?.Sections is not { } sections)
            return null;
        var products = PvfSections.Single(sections, PvfField.Int32List("[hongbao]", optional: true),
            PvfRepeats.AsUndeclared, Path)?.Values;
        if (products is null || products.Count == 0 || products.Count % 4 != 0)
            return null;

        var byItem = new Dictionary<int, GuildRedPacketProduct>();
        var shopIds = new HashSet<int>();
        for (var i = 0; i < products.Count; i += 4)
        {
            var item = products[i];
            var shop = products[i + 1];
            var price = products[i + 2];
            var rate = products[i + 3];
            if (item.Kind != PvfTokenKind.Int32 || shop.Kind != PvfTokenKind.Int32 ||
                price.Kind != PvfTokenKind.Int32 || rate.Kind != PvfTokenKind.Int32 ||
                item.Value <= 0 || shop.Value <= 0 || price.Value is < GuildRedPacketRules.MaxShares or > ushort.MaxValue ||
                rate.Value is < 1 or > 100 || !shopIds.Add(shop.Value) ||
                !byItem.TryAdd(item.Value, new(item.Value, shop.Value, price.Value, rate.Value)))
                return null;
        }

        int? ReadLimit(string field) => PvfSections.SingleInt32(archive, sections,
            PvfField.Int32(field, optional: true, minimum: 1), PvfRepeats.AsUndeclared, Path);
        if (ReadLimit("[min guild member count]") is not { } members
            || ReadLimit("[max total cera per day]") is not { } total
            || ReadLimit("[max take count per day]") is not int takes || takes is < 1 or > ushort.MaxValue)
            return null;
        var rewards = ReadContributionRewards(archive, sections);
        var daily = new Dictionary<GuildDailyContributionKind, int>();
        if (rewards.GetValueOrDefault("guild attendance") is { Required: 1, Points: var attendance })
            daily.Add(GuildDailyContributionKind.Attendance, attendance);
        if (rewards.GetValueOrDefault("guild chat") is { Required: 1, Points: var chat })
            daily.Add(GuildDailyContributionKind.Chat, chat);
        return new(byItem, members, total, takes)
        {
            DailyContributionPoints = daily,
            GuildDungeonContribution = rewards.GetValueOrDefault("guild dungeon clear"),
            PartyDungeonContribution = rewards.GetValueOrDefault("party dungeon clear") is { Required: >= 2 and <= 4 } party
                ? party : null,
        };
    }

    private static Dictionary<string, GuildClearContributionRule> ReadContributionRewards(PvfArchive archive, IReadOnlyList<PvfSection> sections)
    {
        var values = PvfSections.Single(sections, PvfField.Tokens("[hongbao point reward]", optional: true),
            PvfRepeats.AsUndeclared, Path)?.Values ?? [];
        var rewards = new Dictionary<string, GuildClearContributionRule>(StringComparer.Ordinal);
        for (var offset = 0; offset + 2 < values.Count; offset += 3)
        {
            if (!values[offset].IsString || values[offset + 1] is not { Kind: PvfTokenKind.Int32, Value: > 0 and var required }
                || values[offset + 2] is not { Kind: PvfTokenKind.Int32, Value: > 0 and <= ushort.MaxValue and var points })
                continue;
            rewards.TryAdd(archive.ResolveString(values[offset].Value), new(required, points));
        }
        return rewards;
    }
}
