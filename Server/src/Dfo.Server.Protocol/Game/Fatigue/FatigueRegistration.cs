using Dfo.Server.Protocol.Game.Fatigue;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.Fatigue;

/// <summary>What the Fatigue slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class FatigueRegistration
{
    internal static IServiceCollection AddFatigueSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<FatigueNotification, FatigueEncoder>();
        return services;
    }
}
