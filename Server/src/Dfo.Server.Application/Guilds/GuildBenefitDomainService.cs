using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

// Prepare and grant use the caller's write transaction, so a refused purchase never changes an entitlement.
internal interface IGuildBenefitGrants
{
    ValueTask<GuildBenefitGrantDecision> PrepareAsync(
        GuildId guild, GuildContentDefinition product, CancellationToken ct);
    ValueTask GrantAsync(GuildId guild, GuildPurchasedContent content, CancellationToken ct);
}

internal sealed class GuildBenefitDomainService(IGuildDomainStore store, GuildProductRuleService products, TimeProvider clock)
    : IGuildBenefitDomainService, IGuildBenefitGrants
{
    public async ValueTask<GuildBenefitGrantDecision> PrepareAsync(
        GuildId guild, GuildContentDefinition product, CancellationToken ct)
    {
        var contents = await store.ReadContentsAsync(guild, ct).ConfigureAwait(false);
        var existing = contents.FirstOrDefault(content => content.Kind == product.Kind);
        return products.EvaluateGrant(product, existing, clock.GetUtcNow());
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
