using System.Collections.Immutable;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildRedPacketRuleService(IGuildRedPacketRuleSource content)
{
    public GuildRedPacketRules? Read() => content.Read();

    public static GuildRedPacketStatus EvaluateDailyClaim(GuildRedPacketRules rules, int claimedToday) =>
        claimedToday < rules.DailyTakeLimit ? GuildRedPacketStatus.Success : GuildRedPacketStatus.DailyTakeLimit;

    public CeraShopQuote PreparePurchase(CeraShopQuote quote, DateTimeOffset now)
    {
        if (Read() is not { } rules || !quote.Charges.Any(charge => PurchaseProduct(rules, charge) is not null))
            return quote;
        return quote with
        {
            Charges = quote.Charges.Select(charge => PurchaseProduct(rules, charge) is not null
                ? charge with { Mode = CeraShopPaymentMode.CeraOnly } : charge).ToImmutableArray(),
            Grants = quote.Grants.Select(grant => grant.ExpireDate == 0 && rules.Products.ContainsKey(grant.Item.ItemId)
                ? grant with { ExpireDate = checked((int)now.AddDays(10).ToUnixTimeSeconds()) } : grant).ToImmutableArray(),
        };
    }

    public (GuildRedPacketPurchaseTerms? Terms, string? Refusal) QuotePurchase(CeraShopQuote quote)
    {
        if (Read() is not { } rules)
            return (null, quote.Grants.Any(grant => grant.Item.ItemId is >= 490003263 and <= 490003266)
                ? "当前无法读取公会红包规则。" : null);
        long total = 0;
        foreach (var charge in quote.Charges)
        {
            if (PurchaseProduct(rules, charge) is not { } product)
                continue;
            if (charge.Cera != product.Price || charge.Gold != 0)
                return (null, "红包售价与活动配置不一致。");
            total += charge.Cera;
        }
        return (total == 0 ? null : new(total, rules.MinimumMembers, rules.DailyPurchaseLimit), null);
    }

    public static GuildRedPacketPurchaseDecision EvaluatePurchase(GuildRedPacketPurchaseTerms terms, int memberCount, int spent)
    {
        if (memberCount < terms.MinimumMembers)
            return new(0, $"公会成员达到 {terms.MinimumMembers} 人后才能购买红包。");
        if (terms.Price + spent > terms.DailyLimit)
            return new(0, $"角色当天购买红包的上限为 {terms.DailyLimit} 点券。");
        return new(checked((int)terms.Price), null);
    }

    private static GuildRedPacketProduct? PurchaseProduct(GuildRedPacketRules rules, CeraShopCharge charge) =>
        rules.Products.Values.FirstOrDefault(product => product.ShopId == charge.ProductId);

    public static int RollClaimAmount(int remaining, int sharesLeft, int seed)
    {
        if (sharesLeft == 1)
            return remaining;

        var upperBound = Math.Min(remaining - sharesLeft + 1, 2 * (remaining / sharesLeft));
        return new Random(seed).Next(1, upperBound + 1);
    }

    public static int RewardDay(DateTimeOffset localTime) =>
        DateOnly.FromDateTime(localTime.DateTime.AddHours(-6)).DayNumber;

    public static int? EvaluateDailyContribution(GuildRedPacketRules rules, GuildDailyContributionKind kind,
        int joiningDay, int rewardDay)
    {
        if (kind == GuildDailyContributionKind.Attendance && joiningDay == rewardDay)
            return null;
        var points = rules.DailyContributionPoints.GetValueOrDefault(kind);
        return points > 0 ? points : null;
    }

    public static int GuildDungeonClearContribution(GuildClearContributionRule rule, long priorClears) =>
        rule.Required == 0 || priorClears == rule.Required - 1L ? rule.Points : 0;

    public static int? PartyClearContribution(GuildClearContributionRule rule, int guildMemberCount) =>
        guildMemberCount >= rule.Required ? rule.Points : null;

    public static GuildRedPacketSettlement CalculateSettlement(int amount, int remaining, int contributionRate, bool senderDeparted)
    {
        if (senderDeparted)
            return new(0, 0);

        var claimed = (long)amount - remaining;
        return new(remaining, checked((int)(claimed * contributionRate / 10_000)));
    }

    public static GuildRedPacketIssuance EvaluateIssuance(GuildRedPacketRules rules, int selection, byte shares,
        int activeCount, DateTimeOffset now)
    {
        if (shares is < GuildRedPacketRules.MinShares or > GuildRedPacketRules.MaxShares)
            return new(GuildRedPacketStatus.Invalid);
        if (activeCount >= GuildRedPacketRules.GuildActiveLimit)
            return new(GuildRedPacketStatus.Full);

        var itemId = rules.Products.ContainsKey(selection) ? selection
            : selection is >= 0 && selection < rules.Products.Count ? rules.Products.Keys.ElementAt(selection) : 0;
        return rules.Products.TryGetValue(itemId, out var product)
            ? new(GuildRedPacketStatus.Success, itemId, product, now.AddHours(24).ToUnixTimeSeconds())
            : new(GuildRedPacketStatus.Invalid);
    }
}

internal readonly record struct GuildRedPacketSettlement(int Refund, int ContributionPoints);

internal sealed record GuildRedPacketPurchaseTerms(long Price, int MinimumMembers, int DailyLimit);
internal readonly record struct GuildRedPacketPurchaseDecision(int Amount, string? Refusal);

internal readonly record struct GuildRedPacketIssuance(GuildRedPacketStatus Status, int ItemId = 0,
    GuildRedPacketProduct? Product = null, long ExpiresAt = 0);
