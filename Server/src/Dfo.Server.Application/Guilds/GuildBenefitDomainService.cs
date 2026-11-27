using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

// Prepare and grant use the caller's write transaction, so a refused purchase never changes an entitlement.
internal interface IGuildBenefitGrants
{
    ValueTask<(GuildMutationStatus Status, GuildPurchasedContent? Content)> PrepareAsync(
        GuildId guild, GuildContentDefinition product, CancellationToken ct);
    ValueTask GrantAsync(GuildId guild, GuildPurchasedContent content, CancellationToken ct);
}

internal sealed class GuildBenefitDomainService(IGuildDomainStore store, GuildProductRuleService products, TimeProvider clock)
    : IGuildBenefitDomainService, IGuildBenefitGrants
{
    public async ValueTask<(GuildMutationStatus Status, GuildPurchasedContent? Content)> PrepareAsync(
        GuildId guild, GuildContentDefinition product, CancellationToken ct)
    {
        var contents = await store.ReadContentsAsync(guild, ct).ConfigureAwait(false);
        var existing = contents.FirstOrDefault(content => content.Kind == product.Kind);
        var uses = existing?.RemainingUses ?? 0;
        long expiry = 0;
        switch (product.Kind)
        {
            case GuildContentKind.PermanentAttributes:
                if (existing is not null)
                    return (GuildMutationStatus.ContentAlreadyActive, null);
                if (products.Attributes(product.Kind, product.Value) is null)
                    return (GuildMutationStatus.UnsupportedContent, null);
                break;
            case GuildContentKind.TemporaryAttributes:
                if (products.Attributes(product.Kind, product.Value) is null)
                    return (GuildMutationStatus.UnsupportedContent, null);
                goto case GuildContentKind.Experience;
            case GuildContentKind.Experience:
                var now = clock.GetUtcNow().ToUnixTimeSeconds();
                expiry = Math.Max(now, existing?.ExpiresAtUnixSeconds ?? 0) + product.DurationDays * 86400L;
                // A21 treats int.MaxValue as permanent and does not display later expiry values.
                if (product.DurationDays <= 0 || expiry >= int.MaxValue)
                    return (GuildMutationStatus.ContentDurationLimit, null);
                break;
            case GuildContentKind.PartyTeleport:
                if (uses > int.MaxValue - product.Value)
                    return (GuildMutationStatus.ContributionLimit, null);
                uses += product.Value;
                break;
            default:
                return (GuildMutationStatus.UnsupportedContent, null);
        }

        return (GuildMutationStatus.Success, new(product.Kind, product.ProductId, product.Value, uses, expiry));
    }

    public ValueTask GrantAsync(GuildId guild, GuildPurchasedContent content, CancellationToken ct) =>
        store.PutContentAsync(guild, content, ct);

    public ValueTask<IReadOnlyList<GuildExpiredContent>> RemoveExpiredAsync(long asOf, int limit, CancellationToken ct) =>
        store.RemoveExpiredContentsAsync(asOf, limit, ct);

    public async ValueTask<IReadOnlyList<GuildPurchasedContent>> ReadContentsAsync(GuildId guild, CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var contents = await store.ReadContentsAsync(guild, ct).ConfigureAwait(false);
        return contents.Where(content => content.IsActive(now)).ToArray();
    }

    public async ValueTask<IReadOnlyList<GuildAttributeBonus>> ReadAttributeBonusesAsync(GuildId guild, CancellationToken ct) =>
        (await ReadContentsAsync(guild, ct).ConfigureAwait(false))
            .Select(content => products.Attributes(content.Kind, content.Value)).OfType<GuildAttributeBonus>().ToArray();

    public async ValueTask<double> ReadMonsterExperienceRateAsync(CharacterId character, CancellationToken ct) =>
        (await store.ReadMonsterExperiencePercentAsync(character, clock.GetUtcNow().ToUnixTimeSeconds(), ct)
            .ConfigureAwait(false) ?? 0) / 100d;

    public async ValueTask<GuildMutationResult> ConsumeTeleportAsync(CharacterId actor, CancellationToken ct)
    {
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { } membership)
            return new(GuildMutationStatus.NotMember);

        var guild = membership.Guild.Id;
        return await store.TryConsumeTeleportAsync(guild, ct).ConfigureAwait(false)
            ? new(GuildMutationStatus.Success, guild) : new(GuildMutationStatus.UnsupportedContent);
    }
}
