using Dfo.Server.Abstractions.Benefits;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Benefits;

namespace Dfo.Server.Application.World;

internal sealed class ExperienceBonusDomainService(
    ISelectedSessionDomainService sessions,
    GameplayBenefitsQuery<DungeonExperienceBenefitContext> benefits) : IExperienceBonusDomainService
{
    public async ValueTask<IReadOnlyList<ExperienceModifier>> CollectAsync(
        ExperienceOccasion occasion, CancellationToken cancellationToken)
    {
        if (!sessions.TryGetInWorld(occasion.Character, out var session) || session.AccountId != occasion.Account)
            return [];

        var snapshot = await benefits.ReadAsync(new(occasion, session.ListenerPort), cancellationToken,
            GameplayBenefitScope.Global, GameplayBenefitScope.For(occasion.Account), GameplayBenefitScope.For(occasion.Character))
            .ConfigureAwait(false);
        return sessions.IsInWorld(session)
            ? snapshot.Effects<ExperienceBenefit>().Where(effect => effect.Event == occasion.Event)
                .Select(effect => effect.Modifier).ToArray()
            : [];
    }
}
