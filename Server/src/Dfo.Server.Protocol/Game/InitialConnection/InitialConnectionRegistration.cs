using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.InitialConnection;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.InitialConnection;

/// <summary>What the InitialConnection slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class InitialConnectionRegistration
{
    internal static IServiceCollection AddInitialConnectionSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<GameInitialConnectionNotice, GameInitialConnectionNoticeEncoder>();
        return services;
    }
}
