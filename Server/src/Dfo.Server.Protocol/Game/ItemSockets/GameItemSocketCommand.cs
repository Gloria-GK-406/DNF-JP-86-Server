using System.Collections.Immutable;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.ItemSockets;

public sealed record GameItemSocketCommand(NetworkDispatchContext Dispatch, ItemSocketRequest Input) : IProtocolCommand<ItemSocketResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameItemSocketCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers, IItemSocketDomainService sockets, ICommandWorkUnits units, ICommandProjections projections,
    IGameCommandOutbox outbox, ILogger<GameItemSocketCommandHandler> logger) : ICommandHandler<GameItemSocketCommand, ItemSocketResult>
{
    public async ValueTask<ItemSocketResult> HandleAsync(CommandExecutionContext context, GameItemSocketCommand command, CancellationToken ct)
    {
        var requestContext = context.Request ?? throw new InvalidOperationException("Socket requires its explicit request.");
        var request = command.Input;
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        if (!request.Valid || session is null || !onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var online)
            || online is null || requestContext is CommandRequestContext.ProtocolEffectRejected)
        { await Reject(0x04).ConfigureAwait(false); return new(ItemSocketRejection.NoSession); }
        if (request.Avatar && request.TargetList is not (InventoryContainerType.Avatar or InventoryContainerType.Equipment))
        { await Reject(0x04).ConfigureAwait(false); return new(ItemSocketRejection.InvalidRequest); }
        ItemSocketInput input;
        try
        {
            input = new(request.Avatar, request.Open, A21InventoryLayout.ToBusinessSlot(new(request.TargetList, request.TargetSlot)), request.ExpectedItemId,
                request.Open ? A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, request.DrillSlot)) : null,
                request.Emblems.Select(x => new EmblemPlacement(A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, x.MaterialSlot)), x.ItemId, x.SocketIndex)).ToImmutableArray());
        }
        catch (InventorySlotMappingException)
        { await Reject(0x04).ConfigureAwait(false); return new(ItemSocketRejection.InvalidRequest); }
        if (requestContext is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != session)
            throw new InvalidOperationException("Socket requires its originally selected actor.");
        var outcome = await units.WriteResultAsync(context, new("ItemSocket", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            async (unit, token) =>
            {
                var footprint = unit.Footprint;
                try { return new ActionOutcome(await sockets.ApplyAsync(session, input, footprint, token).ConfigureAwait(false), null); }
                catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
                {
                    unit.RequireRollback(failure);
                    return new ActionOutcome(null, failure);
                }
            }, ct).ConfigureAwait(false);
        ItemSocketResult result;
        switch (outcome)
        {
            case CommandWriteResult<ActionOutcome>.Returned { Value: { Result: { } value } }: result = value; break;
            case CommandWriteResult<ActionOutcome>.Returned { Value: { Failure: { } failure } }: return await Failed(failure).ConfigureAwait(false);
            case CommandWriteResult<ActionOutcome>.CommitRolledBack rollback: return await Failed(rollback.Cause).ConfigureAwait(false);
            default: throw new InvalidOperationException("Socket returned no outcome.");
        }
        if (result.Receipt is not { } receipt)
        {
            logger.LogInformation("Socket refused type={Type:X4} character={Character} target={List}/{Slot}/{Item} reason={Reason}",
                request.Metadata.Type, session.CharacterId, request.TargetList, request.TargetSlot, request.ExpectedItemId, result.Rejection);
            await Reject(result.Rejection switch { ItemSocketRejection.Locked => (byte)0x16,
                ItemSocketRejection.MissingMaterial => 0x11, ItemSocketRejection.WrongEmblem => 0x08, _ => 0x04 }).ConfigureAwait(false);
            return result;
        }
        logger.LogInformation("Socket committed type={Type:X4} character={Character} target={List}/{Slot}/{Item} materials={Count}",
            request.Metadata.Type, session.CharacterId, request.TargetList, request.TargetSlot, request.ExpectedItemId, receipt.Materials.Length);
        await projections.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]), new ItemSocketCompleted(request.Metadata.Type, receipt), ct).ConfigureAwait(false);
        return result;
        ValueTask Reject(byte error) => outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId, new ItemSocketResponse(request.Metadata.Type, false, Error: error), ct);
        async ValueTask<ItemSocketResult> Failed(Exception failure)
        {
            logger.LogError(failure, "Socket transaction failed type={Type:X4} character={Character}", request.Metadata.Type, session.CharacterId);
            await Reject(0x04).ConfigureAwait(false); return new(ItemSocketRejection.InvalidRequest);
        }
    }
    private sealed record ActionOutcome(ItemSocketResult? Result, Exception? Failure);
}
