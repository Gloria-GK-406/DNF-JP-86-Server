using System.Collections.Immutable;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildRedPacketPurchasePolicy(GuildRedPacketRuleService source, IGuildRedPacketStore store,
    IGuildDomainStore guilds, TimeProvider clock) : IGuildRedPacketPurchasePolicy
{
    public CeraShopQuote Prepare(CeraShopQuote quote, DateTimeOffset now)
    {
        var rules = source.Read();
        if (rules is null || !quote.Charges.Any(charge => rules.Products.Values.Any(product => product.ShopId == charge.ProductId)))
            return quote;
        return quote with
        {
            Charges = quote.Charges.Select(charge => rules.Products.Values.Any(product => product.ShopId == charge.ProductId)
                ? charge with { Mode = CeraShopPaymentMode.CeraOnly } : charge).ToImmutableArray(),
            Grants = quote.Grants.Select(grant => grant.ExpireDate == 0 && rules.Products.ContainsKey(grant.Item.ItemId)
                ? grant with { ExpireDate = checked((int)now.AddDays(10).ToUnixTimeSeconds()) } : grant).ToImmutableArray(),
        };
    }

    public async ValueTask<string?> RecordPurchaseAsync(CharacterId character, CeraShopQuote quote, DateTimeOffset now,
        CancellationToken ct)
    {
        var rules = source.Read();
        if (rules is null)
            return quote.Grants.Any(grant => grant.Item.ItemId is >= 490003263 and <= 490003266)
                ? "当前无法读取公会红包规则。" : null;
        long total = 0;
        foreach (var charge in quote.Charges)
        {
            var product = rules.Products.Values.FirstOrDefault(product => product.ShopId == charge.ProductId);
            if (product is null)
                continue;
            if (charge.Cera != product.Price || charge.Gold != 0)
                return "红包售价与活动配置不一致。";
            total += charge.Cera;
        }
        if (total == 0)
            return null;
        if ((await guilds.ReadRosterCharacterIdsAsync(character, ct).ConfigureAwait(false)).Count < rules.MinimumMembers)
            return $"公会成员达到 {rules.MinimumMembers} 人后才能购买红包。";
        var day = GuildRedPacketDomainService.Day(TimeZoneInfo.ConvertTime(now, clock.LocalTimeZone));
        if (total + await store.PurchasedAsync(character, day, ct).ConfigureAwait(false) > rules.DailyPurchaseLimit)
            return $"角色当天购买红包的上限为 {rules.DailyPurchaseLimit} 点券。";
        await store.RecordPurchaseAsync(character, day, checked((int)total), ct).ConfigureAwait(false);
        return null;
    }
}
