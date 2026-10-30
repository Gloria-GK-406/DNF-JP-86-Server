using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Game.BossTowerInfo;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.BossTowerInfo;

/// <summary>What the BossTowerInfo slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class BossTowerInfoRegistration
{
    internal static IServiceCollection AddBossTowerInfoSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<BossTowerInfoNotification, BossTowerInfoEncoder>();
        return services;
    }
}
