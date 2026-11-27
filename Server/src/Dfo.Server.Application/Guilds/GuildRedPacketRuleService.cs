using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildRedPacketRuleService(IGuildRedPacketRuleSource content)
{
    public GuildRedPacketRules? Read() => content.Read();

    public static int RollClaimAmount(int remaining, int sharesLeft, int seed)
    {
        if (sharesLeft == 1)
            return remaining;

        var upperBound = Math.Min(remaining - sharesLeft + 1, 2 * (remaining / sharesLeft));
        return new Random(seed).Next(1, upperBound + 1);
    }

    public int DailyContribution(GuildDailyContributionKind kind) =>
        Read()?.DailyContributionPoints.GetValueOrDefault(kind) ?? 0;

    public static GuildRedPacketSettlement CalculateSettlement(int amount, int remaining, int contributionRate, bool senderDeparted)
    {
        if (senderDeparted)
            return new(0, 0);

        var claimed = (long)amount - remaining;
        return new(remaining, checked((int)(claimed * contributionRate / 10_000)));
    }

    public static (int ItemId, GuildRedPacketProduct Product)? Product(GuildRedPacketRules rules, int selection)
    {
        var itemId = rules.Products.ContainsKey(selection) ? selection
            : selection is >= 0 && selection < rules.Products.Count ? rules.Products.Keys.ElementAt(selection) : 0;
        return rules.Products.TryGetValue(itemId, out var product) ? (itemId, product) : null;
    }
}

internal readonly record struct GuildRedPacketSettlement(int Refund, int ContributionPoints);
