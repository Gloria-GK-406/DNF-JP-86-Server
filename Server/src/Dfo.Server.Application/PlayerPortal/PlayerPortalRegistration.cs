using Dfo.Server.Abstractions.PlayerPortal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.PlayerPortal;

/// <summary>What the PlayerPortal domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class PlayerPortalRegistration
{
    internal static IServiceCollection AddPlayerPortal(this IServiceCollection services)
    {
        services.TryAddSingleton<PlayerPortalRuleService>();
        services.TryAddSingleton<IPlayerPortalAccessDomainService, PlayerPortalAccessDomainService>();
        services.TryAddSingleton<IPlayerPortalAccountDomainService, PlayerPortalAccountDomainService>();
        services.TryAddSingleton<IPlayerPortalAdministrationDomainService, PlayerPortalAdministrationDomainService>();
        return services;
    }
}
