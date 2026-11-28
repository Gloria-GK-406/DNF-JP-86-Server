using Dfo.Server.Abstractions.Benefits;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.GameplayParameters;

internal static class GameplayParametersRegistration
{
    internal static IServiceCollection AddGameplayParameters(this IServiceCollection services)
    {
        services.TryAddSingleton<GameplayParameterRuleService>();
        services.TryAddSingleton<GameplayParametersDomainService>();
        services.TryAddSingleton<IFatigueConsumptionQuery>(p => p.GetRequiredService<GameplayParametersDomainService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGameplayBenefitSource<GlobalDropBenefitContext>, DropDrawMultiplierBenefits>());
        services.TryAddSingleton<IDropDrawMultiplierDomainService, DropDrawMultiplierDomainService>();
        return services;
    }
}
