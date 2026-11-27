using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildProfileRuleService(IGuildEconomySource content)
{
    public GuildMarkChangeDecision EvaluateMarkChange(byte mark, GuildMembership? membership)
    {
        if (mark >= GuildMarkPricing.MarkCount)
            return new(GuildMutationStatus.InvalidMark);
        if (membership is not { Member.IsLeader: true })
            return new(GuildMutationStatus.LeaderRequired);
        if (membership.Guild.Mark == mark)
            return new(GuildMutationStatus.Success);
        if (content.ReadMarkPricing() is not { FirstPaidMark: >= 0 and <= GuildMarkPricing.MarkCount, PointCost: > 0 } pricing)
            return new(GuildMutationStatus.MarkPricingUnavailable);
        return new(GuildMutationStatus.Success, pricing.Price(mark));
    }

    public bool AllowsMedalAura(GuildMedalAura aura) =>
        aura == GuildMedalAura.None || content.ReadMedalAuraCatalog()?.Contains(aura) == true;
}

internal readonly record struct GuildMarkChangeDecision(GuildMutationStatus Status, int? Cost = null);
