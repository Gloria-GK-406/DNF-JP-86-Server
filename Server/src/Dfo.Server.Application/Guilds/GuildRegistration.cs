using Dfo.Server.Abstractions.Guilds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Guilds;

internal static class GuildRegistration
{
    public static void AddGuilds(this IServiceCollection services)
    {
        services.TryAddSingleton<GuildRuleService>();
        services.TryAddSingleton<IGuildDomainService, GuildDomainService>();
        services.TryAddSingleton<IGuildQueryDomainService, GuildQueryDomainService>();
    }
}
