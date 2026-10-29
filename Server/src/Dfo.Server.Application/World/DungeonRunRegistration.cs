using Dfo.Server.Abstractions.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.World;

internal static class DungeonRunRegistration
{
    /// <summary>
    /// Registers each narrow dungeon run service as a view of <see cref="IDungeonRunDomainService"/>,
    /// so whichever run registry is registered there is the one every consumer sees.
    /// </summary>
    internal static IServiceCollection AddDungeonRunViews(this IServiceCollection services)
    {
        services.TryAddSingleton<IDungeonRunQueryDomainService>(sp => sp.GetRequiredService<IDungeonRunDomainService>());
        services.TryAddSingleton<IDungeonRunEntryDomainService>(sp => sp.GetRequiredService<IDungeonRunDomainService>());
        services.TryAddSingleton<IDungeonRunCombatDomainService>(sp => sp.GetRequiredService<IDungeonRunDomainService>());
        services.TryAddSingleton<IDungeonRunParticipantDomainService>(sp => sp.GetRequiredService<IDungeonRunDomainService>());
        services.TryAddSingleton<IDungeonRunGroundDropDomainService>(sp => sp.GetRequiredService<IDungeonRunDomainService>());
        services.TryAddSingleton<IDungeonRunSettlementDomainService>(sp => sp.GetRequiredService<IDungeonRunDomainService>());
        services.TryAddSingleton<IDungeonRunCardDomainService>(sp => sp.GetRequiredService<IDungeonRunDomainService>());
        services.TryAddSingleton<IDungeonRunTimerDomainService>(sp => sp.GetRequiredService<IDungeonRunDomainService>());
        services.TryAddSingleton<IDungeonRunDepartureDomainService>(sp => sp.GetRequiredService<IDungeonRunDomainService>());
        return services;
    }
}
