using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildProfileRuleService(IGuildEconomySource content)
{
    public static string? NameKey(string name) => GuildTextRules.NameKey(name);

    public static bool AllowsCreationPromotion(string promotion) => GuildTextRules.IsValidText(promotion);

    public static bool AllowsPromotion(string promotion) => GuildTextRules.IsValidPromotion(promotion);

    public static bool AllowsRequestMessage(string message) => GuildTextRules.IsValidPromotion(message);

    public static bool AllowsAnnouncement(string announcement) => GuildTextRules.IsValidAnnouncement(announcement);

    public static bool AllowsBoardPost(string text) => GuildTextRules.IsValidBoardPost(text);

    public static bool AllowsMemberMemo(string memo) => GuildTextRules.IsValidMemberMemo(memo);

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
