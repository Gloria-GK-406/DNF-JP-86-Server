using Dfo.Server.Abstractions.Benefits;
using Dfo.Server.Application.Benefits;

namespace Dfo.Server.Application.Tests.World;

internal static class TestGameplayBenefits
{
    public static GameplayBenefitsQuery<TContext> Query<TContext>(params IGameplayBenefitSource<TContext>[] sources) =>
        new(new GameplayBenefitRegistry(), sources, TimeProvider.System);
}
