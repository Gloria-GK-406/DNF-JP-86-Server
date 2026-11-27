using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildDonationDomainService(
    IGuildDomainStore store, IGuildPointAccount pointAccount,
    GuildEconomyRuleService rules, ICharacterDomainService characters, TimeProvider time) : IGuildDonationDomainService
{
    public async ValueTask<GuildMutationResult> DonateGoldAsync(CharacterId actor, int amount, CancellationToken ct)
    {
        var (status, points) = rules.EvaluateDonation(amount);
        if (status != GuildMutationStatus.Success)
            return new(status);
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { } membership)
            return new(GuildMutationStatus.NotMember);

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
