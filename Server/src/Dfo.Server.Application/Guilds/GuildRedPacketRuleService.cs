using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildRedPacketRuleService(IGuildRedPacketRuleSource content)
{
    public GuildRedPacketRules? Read() => content.Read();

    public int DailyContribution(GuildDailyContributionKind kind) =>
        Read()?.DailyContributionPoints.GetValueOrDefault(kind) ?? 0;

    public static (int ItemId, GuildRedPacketProduct Product)? Product(GuildRedPacketRules rules, int selection)
    {
        var itemId = rules.Products.ContainsKey(selection) ? selection
            : selection is >= 0 && selection < rules.Products.Count ? rules.Products.Keys.ElementAt(selection) : 0;
        return rules.Products.TryGetValue(itemId, out var product) ? (itemId, product) : null;
    }
}
