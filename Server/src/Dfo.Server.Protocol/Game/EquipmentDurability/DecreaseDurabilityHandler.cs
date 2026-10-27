using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.EquipmentDurability;

public sealed class DecreaseDurabilityHandler(ICommandBus commands) : ClientMessageHandler<DecreaseDurabilityRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameDecreaseDurabilityCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, DecreaseDurabilityRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameDecreaseDurabilityCommand(context, message), ct).ConfigureAwait(false);
}
