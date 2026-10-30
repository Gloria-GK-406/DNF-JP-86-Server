using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.World.IllusionTower;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.World.IllusionTower;

/// <summary>What the World.IllusionTower domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class IllusionTowerRegistration
{
    internal static IServiceCollection AddIllusionTower(this IServiceCollection services)
    {
        services.TryAddSingleton<IIllusionTowerRuleService, IllusionTowerRuleService>();
        services.TryAddSingleton<IIllusionTowerRewardRuleService, IllusionTowerRewardRuleService>();
        services.TryAddSingleton<IIllusionTowerRewardDomainService, IllusionTowerRewardDomainService>();
        services.TryAddSingleton<IIllusionTowerDomainService, IllusionTowerDomainService>();
        return services;
    }
}
