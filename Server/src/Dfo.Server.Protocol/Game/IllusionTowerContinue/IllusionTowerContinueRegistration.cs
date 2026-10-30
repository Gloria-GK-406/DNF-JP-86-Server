using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.IllusionTowerContinue;

/// <summary>What the IllusionTowerContinue slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class IllusionTowerContinueRegistration
{
    internal static IServiceCollection AddIllusionTowerContinueSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<IllusionTowerContinueNotification, IllusionTowerContinueEncoder>();
        return services;
    }
}
