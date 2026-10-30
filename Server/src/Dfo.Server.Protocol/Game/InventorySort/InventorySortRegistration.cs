using Dfo.Server.Protocol.Game.InventorySort;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.InventorySort;

/// <summary>What the InventorySort slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class InventorySortRegistration
{
    internal static IServiceCollection AddInventorySortSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<SortLockChanged, SortLockChangedHandler>();
        services.AddNotificationHandler<InventorySorted, InventorySortedHandler>();
        services.AddGameCmdMessageDecoder<SortLockDecoder>(ClientMessageType.GameSortLock);
        services.AddGameCmdMessageDecoder<SortUnlockDecoder>(ClientMessageType.GameSortUnlock);
        services.AddGameCmdMessageDecoder<SortItemMessageDecoder>(ClientMessageType.GameSortItem);
        services.AddGameCmdMessageEncoder<SortLockResponse, SortLockResponseEncoder>();
        services.AddGameCmdMessageEncoder<SortItemResponse, SortItemResponseEncoder>();
        services.AddProtocolCommandHandler<GameSortLockCommand, bool, GameSortLockCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSortLock, typeof(SortLockRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameSortUnlock, typeof(SortLockRequest)));
        services.AddProtocolCommandHandler<GameSortItemCommand, bool, GameSortItemCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSortItem, typeof(SortItemRequest)));
        services.RegisterProtocolMessageHandler<SortLockHandler, SortLockRequest, GameSortLockCommand>(ClientMessageType.GameSortLock);
        services.RegisterProtocolMessageHandler<SortLockHandler, SortLockRequest, GameSortLockCommand>(ClientMessageType.GameSortUnlock);
        services.RegisterProtocolMessageHandler<SortItemHandler, SortItemRequest, GameSortItemCommand>(ClientMessageType.GameSortItem);
        return services;
    }
}
