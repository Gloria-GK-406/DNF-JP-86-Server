using Dfo.Server.Application.Administration;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.World;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.World;

/// <summary>What the World domain registers for Towns; called once from <see cref="ApplicationModule"/>.</summary>
internal static class TownsRegistration
{
    internal static IServiceCollection AddTowns(this IServiceCollection services)
    {
        services.TryAddSingleton<ITutorialRewardRuleService, TutorialRewardRuleService>();
        // Whether a character may stand where they say, and what a tutorial step declares.
        services.TryAddSingleton<ITownMovementDomainService, TownMovementDomainService>();
        services.TryAddSingleton<ITownTransitDomainService, TownTransitDomainService>();
        services.TryAddSingleton<ITutorialRewardDomainService, TutorialRewardDomainService>();
        services.TryAddSingleton<OnlineCharacterLocationDomainService>();
        services.TryAddSingleton<ITownAreaDomainService>(provider =>
            provider.GetRequiredService<OnlineCharacterLocationDomainService>());
        return services;
    }
}
