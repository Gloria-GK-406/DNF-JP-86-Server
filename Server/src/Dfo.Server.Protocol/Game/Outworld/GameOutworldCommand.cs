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

namespace Dfo.Server.Protocol.Game.Outworld;

public sealed record GameOutworldCommand(NetworkDispatchContext Dispatch, OutworldRequest Input) : IProtocolCommand<EquipmentOutworldResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameOutworldCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    IEquipmentOutworldDomainService outworld, ICommandWorkUnits units, ICommandProjections projections,
    IGameCommandOutbox outbox, ILogger<GameOutworldCommandHandler> logger)
    : ICommandHandler<GameOutworldCommand, EquipmentOutworldResult>
{
    public async ValueTask<EquipmentOutworldResult> HandleAsync(CommandExecutionContext context, GameOutworldCommand command, CancellationToken ct)
    {
        var requestContext = context.Request ?? throw new InvalidOperationException("Equipment option requires its explicit request.");
        var request = command.Input;
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        if (session is null || requestContext is CommandRequestContext.ProtocolEffectRejected)
        {
            logger.LogDebug("OUTWORLD no selected session purify={Purify}", request.Purify);
            await Reject(command.Dispatch, request, ct).ConfigureAwait(false);
            return new(EquipmentOutworldRejection.NoSession);
        }
        if (request.TargetSlot < 0
            || request.MaterialSlot < 0 || request.TargetSlot == request.MaterialSlot
            || request.TargetItemId <= 0 || request.MaterialItemId <= 0
            || !onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var online)
            || online is null)
        {
            await Reject(command.Dispatch, request, ct).ConfigureAwait(false);
            return new(EquipmentOutworldRejection.InvalidRequest);
        }
        // Action 2 appends a polymorphic string in A21. Its material and result semantics
        // are not closed, so never route it to a transaction.
        if (!request.Purify && request.WireAction is not (0 or 1))
        {
            await Reject(command.Dispatch, request, ct, 0x08).ConfigureAwait(false);
            return new(EquipmentOutworldRejection.InvalidRequest);
        }
        BusinessSlot target;
        BusinessSlot material;
        try
        {
            target = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, request.TargetSlot));
            material = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, request.MaterialSlot));
        }
        catch (InventorySlotMappingException)
        {
            await Reject(command.Dispatch, request, ct).ConfigureAwait(false);
            return new(EquipmentOutworldRejection.InvalidRequest);
        }
        var action = request.Purify ? EquipmentOutworldAction.Purify
            : request.WireAction == 0 ? EquipmentOutworldAction.Invest
            : EquipmentOutworldAction.Twist;
        if (requestContext is not CommandRequestContext.ProtocolEffectSelected selected || session != selected.Recipient.Session)
            throw new InvalidOperationException("Outworld requires its originally selected actor.");
        var result = await units.WriteAsync(context,
            new("outworld", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            (unit, token) => outworld.ApplyAsync(session, action, target, request.TargetItemId,
                material, request.MaterialItemId, request.SelectedOption, unit.Footprint, token), ct).ConfigureAwait(false);
        if (result.Receipt is not { } receipt)
        {
            var errorCode = ErrorCode(result.Rejection);
            logger.LogInformation("OUTWORLD rejected character={Character} action={Action} targetSlot={TargetSlot} targetItem={TargetItem} material={Material} selectedOption={SelectedOption} reason={Reason} errorCode={ErrorCode}",
                session.CharacterId.Value, action, request.TargetSlot, request.TargetItemId,
                request.MaterialItemId, request.SelectedOption, result.Rejection, errorCode);
            await Reject(command.Dispatch, request, ct, errorCode).ConfigureAwait(false);
            return result;
        }
        logger.LogInformation("OUTWORLD character={Character} action={Action} target={Target} material={Material} seed={Seed}",
            receipt.Session.CharacterId.Value, action, request.TargetSlot, request.MaterialSlot, receipt.Seed);
        await projections.ProjectAsync(
            new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
            new OutworldCompleted(receipt, request.Purify, request.WireAction), ct)
            .ConfigureAwait(false);
        return result;
    }

    private static byte ErrorCode(EquipmentOutworldRejection rejection) => rejection switch
    {
        EquipmentOutworldRejection.UnsupportedContent => 0x08,
        EquipmentOutworldRejection.SameOption => 0x17,
        EquipmentOutworldRejection.AlreadyUpgraded => 0x12,
        EquipmentOutworldRejection.AlreadyHasAmplifyOption => 0x14,
        EquipmentOutworldRejection.NoAmplifyOption => 0x15,
        _ => 0x11,
    };

    private ValueTask Reject(NetworkDispatchContext context, OutworldRequest request,
        CancellationToken ct, byte errorCode = 0x11) => request.Purify
        ? outbox.RegisterCmdAsync(context.Connection.ConnectionId,
            new PurifyItemResponse(false), ct)
        : outbox.RegisterCmdAsync(context.Connection.ConnectionId,
            new InvestAmplifyOptionResponse(false, request.WireAction, ErrorCode: errorCode), ct);
}
