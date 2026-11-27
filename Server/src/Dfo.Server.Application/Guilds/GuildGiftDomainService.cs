using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildGiftDomainService(IGuildDomainStore guilds, IGuildPointAccount pointAccount, IGuildAllianceStore alliances, IGuildGiftStore gifts,
    IGuildBenefitGrants benefits, GuildProductRuleService products, TimeProvider clock) : IGuildGiftDomainService
{
    public async ValueTask<GuildGiftView?> ReadAsync(CharacterId actor, bool history, CancellationToken ct)
    {
        if (await guilds.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { } membership)
            return null;
        var guild = membership.Guild.Id;
        return new(await gifts.ReadAsync(guild, history, ct).ConfigureAwait(false));
    }

    public async ValueTask<GuildGiftResult> SendAsync(CharacterId actor, GuildId recipient, ushort productId, CancellationToken ct)
    {
        if (await guilds.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true, Guild: var sender })
            return new(GuildGiftStatus.LeaderRequired);
        if (await alliances.FindAllyAsync(sender.Id, ct).ConfigureAwait(false) is not { } ally || ally.Id != recipient)
            return new(GuildGiftStatus.AllyRequired);
        var offer = products.EvaluateGiftOffer(productId);
        if (offer.Product is not { } product)
            return new(offer.Status);

        var now = clock.GetLocalNow();
        var sentAt = now.ToUnixTimeSeconds();
        if (sentAt is <= 0 or > uint.MaxValue)
            return new(GuildGiftStatus.ActivationUnavailable);
        var day = GuildProductRuleService.GiftDay(now);
        var alreadySent = await gifts.HasSentAsync(sender.Id, recipient, day, ct).ConfigureAwait(false);
        var eligibility = GuildProductRuleService.EvaluateDailyGift(alreadySent);
        if (eligibility != GuildGiftStatus.Success)
            return new(eligibility);
        if (!await pointAccount.TrySpendAsync(sender.Id, product.Price, ct).ConfigureAwait(false))
            return new(GuildGiftStatus.InsufficientPoints);

        var gift = new GuildGift(sender.Id, sender.Name, ally.Id, ally.Name, product, sentAt);
        await gifts.AddAsync(gift, day, ct).ConfigureAwait(false);
        return new(GuildGiftStatus.Success, gift, ally.Leader);
    }

    public async ValueTask<GuildGiftResult> ClaimAsync(CharacterId actor, GuildGiftKey key, CancellationToken ct)
    {
        if (await guilds.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true, Guild: var recipient })
            return new(GuildGiftStatus.LeaderRequired);
        if (await gifts.FindPendingAsync(recipient.Id, key, ct).ConfigureAwait(false) is not { } gift)
            return new(GuildGiftStatus.MissingGift);

        var (_, content) = await benefits.PrepareAsync(recipient.Id, gift.Content, ct).ConfigureAwait(false);
        if (content is null)
            return new(GuildGiftStatus.ActivationUnavailable);
        await benefits.GrantAsync(recipient.Id, content, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        if (!await gifts.TryMarkClaimedAsync(recipient.Id, key, now, ct).ConfigureAwait(false))
            throw new InvalidOperationException("The guild gift could not be marked as claimed in its activation transaction.");
        return new(GuildGiftStatus.Success, gift with { ClaimedAt = now });
    }
}
