using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildRedPacketPurchasePolicy(GuildRedPacketRuleService source, IGuildRedPacketStore store,
    IGuildDomainStore guilds, TimeProvider clock) : IGuildRedPacketPurchasePolicy
{
    public CeraShopQuote Prepare(CeraShopQuote quote, DateTimeOffset now) => source.PreparePurchase(quote, now);

    public async ValueTask<string?> RecordPurchaseAsync(CharacterId character, CeraShopQuote quote, DateTimeOffset now,
        CancellationToken ct)
    {
        var (terms, refusal) = source.QuotePurchase(quote);
        if (terms is null)
            return refusal;
        var day = GuildRedPacketRuleService.RewardDay(TimeZoneInfo.ConvertTime(now, clock.LocalTimeZone));
        var members = await guilds.ReadRosterCharacterIdsAsync(character, ct).ConfigureAwait(false);
        var spent = await store.PurchasedAsync(character, day, ct).ConfigureAwait(false);
        var decision = GuildRedPacketRuleService.EvaluatePurchase(terms, members.Count, spent);
        if (decision.Refusal is null)
            await store.RecordPurchaseAsync(character, day, decision.Amount, ct).ConfigureAwait(false);
        return decision.Refusal;
    }
}
