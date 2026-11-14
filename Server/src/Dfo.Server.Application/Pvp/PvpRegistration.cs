using Dfo.Server.Abstractions.Pvp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Pvp;

/// <summary>What the Pvp domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class PvpRegistration
{
    internal static IServiceCollection AddPvp(this IServiceCollection services)
    {
        // One owner behind two faces: readers take the query face only.
        services.TryAddSingleton<PvpRoomDomainService>();
        services.TryAddSingleton<IPvpRoomDomainService>(sp => sp.GetRequiredService<PvpRoomDomainService>());
        services.TryAddSingleton<IPvpRoomQueryDomainService>(sp => sp.GetRequiredService<PvpRoomDomainService>());
        return services;
    }
}
