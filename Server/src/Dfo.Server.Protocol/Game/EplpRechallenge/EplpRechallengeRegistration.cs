using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.EplpRechallenge;

/// <summary>What the EplpRechallenge slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class EplpRechallengeRegistration
{
    internal static IServiceCollection AddEplpRechallengeSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.EplpRechallenge.EplpRechallengeNotification,
            Dfo.Server.Protocol.Game.EplpRechallenge.EplpRechallengeEncoder>();
        return services;
    }
}
