using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildDonationDomainService(
    IGuildDomainStore store, IGuildPointAccount pointAccount,
    GuildEconomyRuleService rules, ICharacterDomainService characters, TimeProvider time) : IGuildDonationDomainService
{
    private const int MaximumDonationGold = 10_000_000;

    public async ValueTask<GuildMutationResult> DonateGoldAsync(CharacterId actor, int amount, CancellationToken ct)
    {
        if (amount <= 0)
            return new(GuildMutationStatus.InvalidDonationAmount);
        if (amount > MaximumDonationGold)
            return new(GuildMutationStatus.DonationTooLarge);
        if (rules.DonationGoldPerPoint() is not { } rate)
            return new(GuildMutationStatus.DonationUnavailable);
        if (amount % rate != 0)
            return new(GuildMutationStatus.InvalidDonationAmount);
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { } membership)
            return new(GuildMutationStatus.NotMember);

        var points = amount / rate;
        if (membership.Member.DonationPoints > int.MaxValue - points ||
            !await pointAccount.CanCreditAsync(membership.Guild.Id, points, ct).ConfigureAwait(false))
            return new(GuildMutationStatus.ContributionLimit);
        if (await characters.Get(actor).TrySpendGoldAsync(amount, ct).ConfigureAwait(false) is not { } remaining)
            return new(GuildMutationStatus.InsufficientGold);

        if (!await pointAccount.TryCreditAsync(membership.Guild.Id, points, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Guild points changed during donation.");
        await store.AddDonationAsync(membership.Guild.Id, actor, amount, points, time.GetUtcNow(), ct).ConfigureAwait(false);
        return new(GuildMutationStatus.Success, membership.Guild.Id, remaining);
    }

    public async ValueTask<IReadOnlyList<GuildDonationEntry>?> ReadDonationHistoryAsync(
        CharacterId viewer, int limit, CancellationToken ct)
    {
        if (await store.FindMembershipAsync(viewer, ct).ConfigureAwait(false) is not { } membership)
            return null;
        return await store.ReadDonationHistoryAsync(membership.Guild.Id, limit, ct).ConfigureAwait(false);
    }
}
