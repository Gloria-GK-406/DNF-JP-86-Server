using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Game.BossTowerContinue;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.BossTowerContinue;

/// <summary>What the BossTowerContinue slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class BossTowerContinueRegistration
{
    internal static IServiceCollection AddBossTowerContinueSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<BossTowerContinueNotification, BossTowerContinueEncoder>();
        return services;
    }
}
