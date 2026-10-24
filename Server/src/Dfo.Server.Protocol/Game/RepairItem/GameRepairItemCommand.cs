using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Microsoft.Extensions.Logging;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.RepairItem;

public sealed record GameRepairItemCommand(NetworkDispatchContext Dispatch, RepairItemRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameRepairItemCommandHandler(IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions, IInventorySlotMappingRuleService slotMappings,
    IEquipmentRepairDomainService repairs, ICommandWorkUnits units, ICommandProjections notifications,
    IGameCommandOutbox outbox, TimeProvider clock, ILogger<GameRepairItemCommandHandler> logger) : ICommandHandler<GameRepairItemCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameRepairItemCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch;
        var message = command.Input;
        BusinessSlot? slot = message.Slot is { } gameSlot
            ? slotMappings.ToBusinessSlot(gameSlot)
            : null;
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null) return false;
        if (!sessions.IsInWorld(session)) return false;
        var automatic = message.AutoRepair == 1 && message.Slot is null
            && message.RepairAllSpace == InventoryContainerType.Main;
        EquipmentRepairResult result;
        // Only the verified automatic all-repair shape may bypass the ordinary gold path.
        if (message.MaterialSlot != -1 || (message.AutoRepair != 0 && !automatic) || message.Reserved != 0
            || message.QuickRepair != 0)
            result = new(EquipmentRepairRejection.InvalidRequest);
        else if (!onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null)
            result = new(EquipmentRepairRejection.NoSession);
        else
            result = await units.WriteAsync(execution, new("RepairItem", []),
                (_, token) => automatic
                    ? repairs.AutoRepairAsync(session, token)
                    : repairs.RepairAsync(session, slot, token),
                cancellationToken).ConfigureAwait(false);

        if (automatic)
            logger.LogInformation("Automatic repair character={CharacterId} result={Result}", session.CharacterId, result.Rejection);
        if (result.Receipt is { } receipt)
            await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new EquipmentRepairCompleted(receipt, message.RepairAllSpace, result.Entitlements, result.Usage, clock.GetUtcNow()), cancellationToken)
                .ConfigureAwait(false);
        else
            await outbox.RegisterCmdAsync(context.Connection.ConnectionId,
                new RepairItemResponse(null, ErrorCode: ErrorCode(result.Rejection)),
                cancellationToken).ConfigureAwait(false);
        return result.Receipt is not null;
    }

    // The upstream ordinary-repair path answers its failed commit with 0x0A. Individual A21
    // error branches still need client-EXE verification; avoid inventing separate meanings here.
    private static byte ErrorCode(EquipmentRepairRejection reason) => reason switch
    {
        EquipmentRepairRejection.NoSession or EquipmentRepairRejection.InvalidRequest
            or EquipmentRepairRejection.ItemUnavailable or EquipmentRepairRejection.CannotRepair
            or EquipmentRepairRejection.InsufficientGold => 10,
        EquipmentRepairRejection.DailyLimitReached => 0x73,
        EquipmentRepairRejection.MissingEntitlement or EquipmentRepairRejection.NoBrokenEquipment => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };
}
