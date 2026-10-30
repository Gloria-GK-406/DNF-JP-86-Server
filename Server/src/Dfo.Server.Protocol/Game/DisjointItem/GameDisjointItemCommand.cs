using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.DisjointItem;

public sealed record GameDisjointItemCommand(NetworkDispatchContext Dispatch, DisjointItemRequest Input) : IProtocolCommand<EquipmentDisjointResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameDisjointItemCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    IEquipmentDisjointDomainService disjoint, ICommandWorkUnits units, ICommandProjections projections,
    IGameCommandOutbox outbox, ILogger<GameDisjointItemCommandHandler> logger)
    : ICommandHandler<GameDisjointItemCommand, EquipmentDisjointResult>
{
    public async ValueTask<EquipmentDisjointResult> HandleAsync(CommandExecutionContext context, GameDisjointItemCommand command, CancellationToken ct)
    {
        var requestContext = context.Request ?? throw new InvalidOperationException("Equipment requires its explicit request.");
        var message = command.Input;
        var targetSlot = A21InventoryLayout.ToBusinessSlot(message.TargetSlot);
        var invalidToolSlot = message.DisjointToolSlotIndex < -1;
        BusinessSlot? disjointToolSlot = message.DisjointToolSlotIndex >= 0
            ? A21InventoryLayout.ToBusinessSlot(new GameSlot(
                InventoryContainerType.Main, message.DisjointToolSlotIndex))
            : null;
        logger.LogDebug(
            "DISJOINT_ITEM target={TargetSpace}/{TargetIndex} toolSlot={ToolSlot} providerId={ProviderId}",
            message.TargetSlot.Space,
            message.TargetSlot.Index,
            message.DisjointToolSlotIndex,
            message.DisjointProviderId);
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        EquipmentDisjointResult result;
        if (session is null || requestContext is CommandRequestContext.ProtocolEffectRejected
            || !onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var online)
            || online is null)
            result = new(EquipmentDisjointRejection.NoSession);
        else if (invalidToolSlot || disjointToolSlot is null && message.DisjointProviderId <= 0)
            result = new(EquipmentDisjointRejection.InvalidRequest);
        else if (disjointToolSlot is not null && message.DisjointProviderId != 0)
            result = new(EquipmentDisjointRejection.InvalidRequest);
        else
        {
            if (requestContext is not CommandRequestContext.ProtocolEffectSelected selected || session != selected.Recipient.Session)
                throw new InvalidOperationException("Disjoint requires its originally selected actor.");
            result = await units.WriteAsync(context,
                new("disjoint-item", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
                (unit, token) => disjointToolSlot is { } portableSlot
                    ? disjoint.DisjointWithPortableMachineAsync(session, targetSlot, portableSlot, unit.Footprint, token)
                    : disjoint.DisjointAtNpcAsync(session, targetSlot, unit.Footprint, token), ct).ConfigureAwait(false);
        }

        if (result.Receipt is { } receipt)
            await projections.ProjectAsync(
                new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new EquipmentDisjointCompleted(receipt),
                ct).ConfigureAwait(false);
        else
            await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId,
                new DisjointItemResponse(null, ErrorCode: ErrorCode(result.Rejection)),
                ct).ConfigureAwait(false);
        return result;
    }

    internal static byte ErrorCode(EquipmentDisjointRejection rejection) => rejection switch
    {
        EquipmentDisjointRejection.InventoryFull => 0x04,
        EquipmentDisjointRejection.NoSession
            or EquipmentDisjointRejection.InvalidRequest
            or EquipmentDisjointRejection.ItemUnavailable
            or EquipmentDisjointRejection.CannotDisjoint
            or EquipmentDisjointRejection.UnsupportedMethod => 0x13,
        _ => throw new ArgumentOutOfRangeException(nameof(rejection)),
    };
}
