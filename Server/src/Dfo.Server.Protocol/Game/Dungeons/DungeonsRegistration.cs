using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Dungeons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Protocol.Game.Dungeons;

/// <summary>What the Dungeons slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class DungeonsRegistration
{
    internal static IServiceCollection AddDungeonsSlice(this IServiceCollection services)
    {
        services.TryAddSingleton<DungeonRunDeliveryRegistry>();
        services.TryAddSingleton<DungeonPartyRecipients>();
        services.TryAddSingleton<IDungeonRunProjectionDomainService>(provider =>
            provider.GetRequiredService<DungeonRunDeliveryRegistry>());
        services.TryAddSingleton<IDungeonRunParticipantDepartureSink>(provider =>
            provider.GetRequiredService<DungeonRunDeliveryRegistry>());
        services.AddGameNotiMessageEncoder<HellPartyMonsterInfoNotification, HellPartyMonsterInfoEncoder>();
        return services;
    }
}
