using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Sessions;

/// <summary>What the Sessions domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class SessionsRegistration
{
    internal static IServiceCollection AddSessions(this IServiceCollection services)
    {
        services.TryAddSingleton<SelectedSessionDomainService>();
        services.TryAddSingleton<ISelectedSessionRefreshDomainService, SelectedSessionRefreshDomainService>();
        services.TryAddSingleton<ISelectedSessionDomainService>(sp => sp.GetRequiredService<SelectedSessionDomainService>());
        services.TryAddSingleton<ISelectedSessionSnapshotQuery>(sp => sp.GetRequiredService<SelectedSessionDomainService>());
        services.TryAddSingleton<ISelectedCharacterCurrentViewDomainService, SelectedCharacterCurrentViewDomainService>();
        services.TryAddSingleton<IExpertStoreDomainService, ExpertStoreDomainService>();
        services.TryAddSingleton<IPvpLobbyDomainService, PvpLobbyDomainService>();
        services.TryAddSingleton<ISessionAdjacentEntryDomainService, SessionAdjacentEntryDomainService>();
        services.TryAddSingleton<ISelectedSessionCleanupDomainService, SelectedSessionCleanupService>();
        services.TryAddSingleton<ISelectCharacterSessionEntryDomainService, SelectCharacterSessionEntryService>();
        return services;
    }
}
