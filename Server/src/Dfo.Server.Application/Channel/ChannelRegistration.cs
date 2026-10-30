using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Channel;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Sessions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Channel;

/// <summary>What the Channel domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class ChannelRegistration
{
    internal static IServiceCollection AddChannel(this IServiceCollection services)
    {
        // What the server declares about its channels, and what a tutorial step hands over.
        services.TryAddSingleton<IChannelContentRuleService, ChannelContentRuleService>();
        services.TryAddSingleton<
            IChannelDirectoryDomainService, ChannelDirectoryDomainService>();
        services.TryAddSingleton<ChannelEntryDomainService>();
        services.TryAddSingleton<IChannelEntryDomainService>(provider => provider.GetRequiredService<ChannelEntryDomainService>());
        return services;
    }
}
