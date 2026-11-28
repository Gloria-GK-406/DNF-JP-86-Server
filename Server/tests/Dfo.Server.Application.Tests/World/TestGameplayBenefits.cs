using Dfo.Server.Abstractions.Benefits;
using Dfo.Server.Application.Benefits;
using Dfo.Server.Application.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

internal static class TestGameplayBenefits
{
    public static LuckyCharacterBenefitRuleService LuckyRules { get; } =
        new(TestRequired.Absent<Dfo.Server.Abstractions.Events.IFatigueBurningDungeonContentSource>());

    public static GameplayBenefitsQuery<TContext> Query<TContext>(params IGameplayBenefitSource<TContext>[] sources) =>
        new(new GameplayBenefitRegistry(), sources, TimeProvider.System);
}
