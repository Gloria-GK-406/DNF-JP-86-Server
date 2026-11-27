using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildProfileDomainService(
    IGuildDomainStore store, IGuildPointAccount pointAccount, GuildProfileRuleService rules) : IGuildProfileDomainService
{
    public async ValueTask<GuildMutationStatus> SetPromotionAsync(CharacterId actor, string promotion, CancellationToken ct)
    {
        if (!GuildTextRules.IsValidPromotion(promotion))
            return GuildMutationStatus.InvalidText;
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true } membership)
            return GuildMutationStatus.LeaderRequired;

        await store.SetPromotionAsync(membership.Guild.Id, promotion, ct).ConfigureAwait(false);
        return GuildMutationStatus.Success;
    }

    public async ValueTask<GuildMutationStatus> SetAnnouncementAsync(
        CharacterId actor, string announcement, CancellationToken ct)
    {
        if (!GuildTextRules.IsValidAnnouncement(announcement))
            return GuildMutationStatus.InvalidText;
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true } membership)
            return GuildMutationStatus.LeaderRequired;

        await store.SetAnnouncementAsync(membership.Guild.Id, announcement, ct).ConfigureAwait(false);
        return GuildMutationStatus.Success;
    }

    public async ValueTask<GuildMutationStatus> SetMarkAsync(CharacterId actor, byte mark, CancellationToken ct)
    {
        if (mark >= GuildMarkPricing.MarkCount)
            return GuildMutationStatus.InvalidMark;
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true } membership)
            return GuildMutationStatus.LeaderRequired;
        if (membership.Guild.Mark == mark)
            return GuildMutationStatus.Success;
        if (rules.MarkPrice(mark) is not { } cost)
            return GuildMutationStatus.MarkPricingUnavailable;

        if (!await pointAccount.TrySpendAsync(membership.Guild.Id, cost, ct).ConfigureAwait(false))
            return GuildMutationStatus.InsufficientGuildPoints;
        await store.SetMarkAsync(membership.Guild.Id, mark, ct).ConfigureAwait(false);
        return GuildMutationStatus.Success;
    }

    public async ValueTask<GuildMutationStatus> SetMedalAuraAsync(CharacterId actor, GuildMedalAura aura, CancellationToken ct)
    {
        if (!rules.AllowsMedalAura(aura)) return GuildMutationStatus.InvalidMedalAura;
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true } membership)
            return GuildMutationStatus.LeaderRequired;
        if (membership.Guild.MedalAura == aura) return GuildMutationStatus.Success;
        return await store.TrySetMedalAuraAsync(membership.Guild.Id, actor, aura, ct).ConfigureAwait(false)
            ? GuildMutationStatus.Success : GuildMutationStatus.LeaderRequired;
    }

    public async ValueTask<GuildMutationStatus> SetRecommendedChannelAsync(CharacterId actor, int channel, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channel);
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true } membership)
            return GuildMutationStatus.LeaderRequired;

        await store.SetRecommendedChannelAsync(membership.Guild.Id, channel, ct).ConfigureAwait(false);
        return GuildMutationStatus.Success;
    }
}
