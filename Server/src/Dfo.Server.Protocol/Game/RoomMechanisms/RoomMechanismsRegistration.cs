using Dfo.Server.Application.World;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.RoomMechanisms;

/// <summary>What the RoomMechanisms slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class RoomMechanismsRegistration
{
    internal static IServiceCollection AddRoomMechanismsSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.RoomMechanisms.ElevatorStateMessage,
            Dfo.Server.Protocol.Game.RoomMechanisms.ElevatorStateEncoder>();
        services.AddNotificationHandler<DungeonRoomMechanismsNotification,
            Dfo.Server.Protocol.Game.RoomMechanisms.RoomMechanismNotificationHandler>();
        services.AddDungeonDeadlineSource<Dfo.Server.Protocol.Game.RoomMechanisms.RoomMechanismDeadlineSource>();
        return services;
    }
}
