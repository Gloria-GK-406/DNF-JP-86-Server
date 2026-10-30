using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DisjointItem;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.DisjointItem;

/// <summary>What the DisjointItem slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class DisjointItemRegistration
{
    internal static IServiceCollection AddDisjointItemSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<EquipmentDisjointCompleted, EquipmentDisjointCompletedHandler>();
        services.AddProtocolCommandHandler<GameDisjointItemCommand, EquipmentDisjointResult, GameDisjointItemCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameDisjointItem, typeof(DisjointItemRequest)));
        services.AddGameCmdMessageDecoder<DisjointItemMessageDecoder>(ClientMessageType.GameDisjointItem);
        services.AddGameCmdMessageEncoder<DisjointItemResponse, DisjointItemResponseEncoder>();
        services.RegisterProtocolMessageHandler<DisjointItemHandler, DisjointItemRequest, GameDisjointItemCommand>(ClientMessageType.GameDisjointItem);
        return services;
    }
}
