using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;

namespace Dfo.Server.Application.Creatures;

internal sealed class CreatureRuleService(ICreatureRuleSource source,
    ICreatureEquipmentLifetimeSource lifetimes) : ICreatureRuleService
{
    public ValueTask<CreatureRule?> GetAsync(int itemTemplateId, CancellationToken ct) =>
        source.GetAsync(itemTemplateId, ct);

    public ValueTask<CreatureArtifactExperienceRule?> GetArtifactExperienceAsync(int itemTemplateId,
        CancellationToken ct) => source.GetArtifactExperienceAsync(itemTemplateId, ct);

    public async ValueTask<int> BirthExpirationAsync(int itemTemplateId, DateTimeOffset at,
        CancellationToken ct)
    {
        var lifetime =
            await lifetimes.GetAsync(itemTemplateId, at, ct).ConfigureAwait(false);
        var remaining = lifetime.DirectRemainingSeconds > 0
            ? lifetime.DirectRemainingSeconds : lifetime.DetailFallbackRemainingSeconds;
        return remaining == 0 ? 0 : checked((int)Math.Min(int.MaxValue, at.ToUnixTimeSeconds() + remaining));
    }
}
