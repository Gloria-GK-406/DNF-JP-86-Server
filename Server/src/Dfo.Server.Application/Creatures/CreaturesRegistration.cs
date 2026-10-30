using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Creatures;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Creatures;

/// <summary>What the Creatures domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class CreaturesRegistration
{
    internal static IServiceCollection AddCreatures(this IServiceCollection services)
    {
        services.TryAddSingleton<ICreatureRuleService, CreatureRuleService>();
        services.TryAddSingleton<ICreatureDomainService, CreatureDomainService>();
        services.TryAddSingleton<ICreatureNamingDomainService, CreatureNamingDomainService>();
        services.TryAddSingleton<CreatureRuntimeDomainService>();
        services.TryAddSingleton<ICreatureRuntimeDomainService>(provider => provider.GetRequiredService<CreatureRuntimeDomainService>());
        services.TryAddSingleton<ICreatureInstanceDomainService>(provider => provider.GetRequiredService<CreatureRuntimeDomainService>());
        return services;
    }
}
