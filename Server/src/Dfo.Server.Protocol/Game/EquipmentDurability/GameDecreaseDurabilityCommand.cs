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

public sealed record GameDecreaseDurabilityCommand(NetworkDispatchContext Dispatch, DecreaseDurabilityRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameDecreaseDurabilityCommandHandler(IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions, IEquipmentDurabilityDomainService durability, ICommandWorkUnits units,
    ICommandProjections notifications, IGameCommandOutbox sender)
    : ICommandHandler<GameDecreaseDurabilityCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameDecreaseDurabilityCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        var session = sessions.TryGetInWorld(context.Connection.ConnectionId, out var inWorld) ? inWorld : null;
        EquipmentDurabilityResult result;
        if (session is null || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null)
            result = new(EquipmentDurabilityRejection.NoSession);
        else if (TryMapSlot(message.SlotIndex, out var slot))
            result = await units.WriteAsync(execution, new("DecreaseDurability", [new(session, context.Connection, message.Metadata)]),
                (_, token) => durability.DecreaseAsync(session, slot, token), cancellationToken).ConfigureAwait(false);
        else
            result = new(EquipmentDurabilityRejection.InvalidSlot);

        if (result.Receipt is { } receipt)
            await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new EquipmentDurabilityChanged(receipt), cancellationToken).ConfigureAwait(false);
        else
            await sender.RegisterCmdAsync(context.Connection.ConnectionId,
                new DecreaseDurabilityResponse(null, 17), cancellationToken).ConfigureAwait(false);
        return true;
    }

    private bool TryMapSlot(byte index, out BusinessSlot slot)
    {
        try
        {
            slot = A21InventoryLayout.ToBusinessSlot(new GameSlot(InventoryContainerType.Equipment, index));
            return true;
        }
        catch (InventorySlotMappingException)
        {
            slot = default;
            return false;
        }
    }
}
