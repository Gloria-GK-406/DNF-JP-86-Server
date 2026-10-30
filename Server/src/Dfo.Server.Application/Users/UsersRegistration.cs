using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Users;
using Dfo.Server.Application.World;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Users;

/// <summary>What the Users domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class UsersRegistration
{
    internal static IServiceCollection AddUsers(this IServiceCollection services)
    {
        // One store for who each connection is: online users, their accounts, the selected
        // session and its stage, behind a single lock. The services are responsibilities read off
        // it; the selected-session one is registered with the session services below.
        services.TryAddSingleton<OnlineStateRegistry>();
        services.TryAddSingleton(
            provider => new OnlineUserDomainService(
                provider.GetRequiredService<OnlineStateRegistry>()));
        services.TryAddSingleton<IOnlineUserDomainService>(
            provider => provider.GetRequiredService<OnlineUserDomainService>());
        services.TryAddSingleton<IOnlineUserQueryDomainService>(
            provider => provider.GetRequiredService<OnlineUserDomainService>());
        services.TryAddSingleton<IGameSessionEndpointDomainService>(provider =>
            new GameSessionEndpointDomainService(
                provider.GetRequiredService<OnlineStateRegistry>()));
        services.TryAddSingleton<IAuthenticatedGamePresenceQuery>(provider => provider.GetRequiredService<OnlineUserDomainService>());
        return services;
    }
}
