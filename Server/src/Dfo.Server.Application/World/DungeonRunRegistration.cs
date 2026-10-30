using Dfo.Server.Abstractions.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.World;

internal static class DungeonRunRegistration
{
    /// <summary>Registers the run store and each run service behind its own narrow interface.</summary>
    internal static IServiceCollection AddDungeonRuns(this IServiceCollection services)
    {
        services.TryAddSingleton<DungeonRunStore>();
        services.TryAddSingleton<IDungeonRunQueryDomainService>(sp => sp.GetRequiredService<DungeonRunStore>());
        services.TryAddSingleton<IDungeonRunEntryDomainService, DungeonRunStore.EntryService>();
        services.TryAddSingleton<IDungeonRunCombatDomainService, DungeonRunStore.CombatService>();
        services.TryAddSingleton<IDungeonRunParticipantDomainService, DungeonRunStore.ParticipantService>();
        services.TryAddSingleton<IDungeonRunGroundDropDomainService, DungeonRunStore.GroundDropService>();
        services.TryAddSingleton<IDungeonRunSettlementDomainService, DungeonRunStore.SettlementService>();
        services.TryAddSingleton<IDungeonRunCardDomainService, DungeonRunStore.CardService>();
        services.TryAddSingleton<IDungeonRunTimerDomainService, DungeonRunStore.TimerService>();
        services.TryAddSingleton<IDungeonRunDepartureDomainService, DungeonRunStore.DepartureService>();
        return services;
    }
}
