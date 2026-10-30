using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Forging;

public sealed record GameForgingCommand(NetworkDispatchContext Dispatch, ForgingRequest Input) : IProtocolCommand<EquipmentForgingResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameForgingCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    IEquipmentForgingDomainService forging, ICommandWorkUnits units, ICommandProjections projections,
    IGameCommandOutbox outbox, ILogger<GameForgingCommandHandler> logger)
    : ICommandHandler<GameForgingCommand, EquipmentForgingResult>
{
    public async ValueTask<EquipmentForgingResult> HandleAsync(CommandExecutionContext context, GameForgingCommand command, CancellationToken ct)
    {
        var requestContext = context.Request ?? throw new InvalidOperationException("Equipment option requires its explicit request.");
        var request = command.Input;
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        if (session is null || requestContext is CommandRequestContext.ProtocolEffectRejected)
        {
            logger.LogDebug("FORGING no selected session");
            await Reject(command.Dispatch, ct, 0x04).ConfigureAwait(false);
            return new(EquipmentForgingRejection.NoSession);
        }
        // Variant 1/2 requires a separate ticket rule; never assume the ordinary PVF material path.
        if (request.Variant != 0)
        {
            await Reject(command.Dispatch, ct, 0x0D).ConfigureAwait(false);
            return new(EquipmentForgingRejection.InvalidRequest);
        }
        if (request.TargetListType is not ((byte)InventoryContainerType.Main)
                and not ((byte)InventoryContainerType.Equipment)
            || request.TargetSlot < 0 || request.MaterialSlot < 0
            || request.TargetItemId <= 0 || request.ReportedNameBytes.Length is < 1 or > 128
            || !onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var online)
            || online is null)
        {
            await Reject(command.Dispatch, ct, 0x04).ConfigureAwait(false);
            return new(EquipmentForgingRejection.InvalidRequest);
        }
        BusinessSlot target;
        BusinessSlot material;
        try
        {
            target = A21InventoryLayout.ToBusinessSlot(new(
                (InventoryContainerType)request.TargetListType, request.TargetSlot));
            material = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main,
                request.MaterialSlot));
        }
        catch (InventorySlotMappingException)
        {
            await Reject(command.Dispatch, ct, 0x04).ConfigureAwait(false);
            return new(EquipmentForgingRejection.InvalidRequest);
        }
        if (requestContext is not CommandRequestContext.ProtocolEffectSelected selected || session != selected.Recipient.Session)
            throw new InvalidOperationException("Forging requires its originally selected actor.");
        var result = await units.WriteAsync(context,
            new("forging", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            (unit, token) => forging.ForgeAsync(session, target, request.TargetItemId, material, unit.Footprint, token), ct).ConfigureAwait(false);
        if (result.Receipt is not { } receipt)
        {
            var errorCode = ErrorCode(result.Rejection);
            await Reject(command.Dispatch, ct, errorCode).ConfigureAwait(false);
            return result;
        }
        logger.LogInformation("FORGING character={Character} target={Target} old={Old} new={New} roll={Roll}",
            receipt.Session.CharacterId.Value, request.TargetSlot, receipt.OldLevel,
            receipt.NewLevel, receipt.Roll);
        await projections.ProjectAsync(
            new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
            new ForgingCompleted(receipt), ct).ConfigureAwait(false);
        return result;
    }

    internal static byte ErrorCode(EquipmentForgingRejection rejection) => rejection switch
    {
        EquipmentForgingRejection.Durability => 0x07,
        EquipmentForgingRejection.UnsupportedContent => 0x0D,
        EquipmentForgingRejection.NotWeapon => 0x13,
        EquipmentForgingRejection.MaterialUnavailable => 0x16,
        EquipmentForgingRejection.MaxLevel => 0x5F,
        EquipmentForgingRejection.Locked => 0xD5,
        _ => 0x04,
    };

    private ValueTask Reject(NetworkDispatchContext context, CancellationToken ct,
        byte errorCode) => outbox.RegisterCmdAsync(context.Connection.ConnectionId,
            new ForgingResponse(false, errorCode), ct);
}
