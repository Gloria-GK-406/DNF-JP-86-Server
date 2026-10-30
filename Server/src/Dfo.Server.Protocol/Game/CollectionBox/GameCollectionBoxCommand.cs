using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.CollectionBox;

public sealed record GameCollectionBoxCommand(NetworkDispatchContext Dispatch, CollectionBoxRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameCollectionBoxCommandHandler(ISelectedSessionDomainService sessions, ICollectionBoxDomainService boxes, TimeProvider clock, ICommandWorkUnits units, IGameCommandOutbox outbox)
    : ICommandHandler<GameCollectionBoxCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameCollectionBoxCommand command, CancellationToken ct)
    {
        var request = context.Request ?? throw new InvalidOperationException("Collection box requires its explicit request.");
        var actor = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        if (actor is null || request is CommandRequestContext.ProtocolEffectRejected) return false;
        if (request is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != actor)
            throw new InvalidOperationException("Collection box requires its originally selected actor.");
        request.DemandCurrent();
        BusinessSlot? source = null; byte? mappingError = null;
        try { if (command.Input.Action == CollectionBoxAction.Put) source = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, command.Input.SourceSlot)); }
        catch (InventorySlotMappingException) { mappingError = 0x15; }
        catch (ArgumentOutOfRangeException) { mappingError = 0x15; }
        if (mappingError is { } early) { await Reject(early).ConfigureAwait(false); return false; }
        var outcome = await units.WriteAsync(context, new("CollectionBox", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            async (unit, token) =>
            {
                unit.Footprint.DemandActive(actor);
                CollectionBoxMutation? mutation = null; Exception? cause = null; byte? error = null;
                try { mutation = await boxes.ExecuteAsync(actor.AccountId, actor.CharacterId, command.Input.Action, command.Input.BoxId, source, command.Input.ItemId, token).ConfigureAwait(false); }
                catch (CollectionBoxRejectedException e) { cause = e; error = ErrorCode(command.Input.Action, e.Reason); }
                catch (ContainerAdditionException e) when (e.Reason == ContainerAdditionFailure.NoRoom) { cause = e; error = 0x04; }
                catch (ArgumentOutOfRangeException e) { cause = e; error = 0x15; }
                catch (InventorySlotMappingException e) { cause = e; error = 0x15; }
                if (cause is not null) { unit.RequireRollback(cause); return new Outcome(null, error); }
                if (mutation is null) throw new InvalidOperationException("Collection box returned no mutation.");
                if (mutation.Inventory.Count > 0) unit.Footprint.RecordInventorySlots(actor, mutation.Inventory.Select(x => x.Slot).ToArray());
                return new Outcome(mutation, null);
            }, ct).ConfigureAwait(false);
        request.DemandCurrent();
        if (outcome.Mutation is not { } result) { await Reject(outcome.Error ?? 0x15).ConfigureAwait(false); return false; }
        var frames = new List<IGameServerMessage>();
        foreach (var change in result.Inventory)
        {
            var physical = A21InventoryLayout.ToGameSlot(change.Slot);
            frames.Add(change.Item is { } item ? GameInventoryDelivery.Notification(new(physical.Space,
                [new(item.ItemUid, physical.Index, item.ToCore(), item.AvatarDetail)], clock.GetUtcNow()))
                : GameInventoryDelivery.ClearedSlotNotification(physical.Space, physical.Index, clock.GetUtcNow()));
        }
        frames.AddRange(CollectionBoxContentsMessage.Full(result.State));
        frames.Add(new CollectionBoxAck(command.Input.Action, command.Input.BoxId));
        var attempt = request.BeginRegistrationAttempt(CommandRegistrationExit.SettledInitialBatch, command.Dispatch.Connection);
        await outbox.RegisterBatchAsync(command.Dispatch.Connection, frames, attempt, ct).ConfigureAwait(false);
        return true;
        ValueTask Reject(byte error) => outbox.RegisterBatchAsync(command.Dispatch.Connection,
            [new CollectionBoxAck(command.Input.Action, command.Input.BoxId, error)], ct);
    }
    private sealed record Outcome(CollectionBoxMutation? Mutation, byte? Error);
    // Wire reasons belong here: their native UI meanings still await real-client verification.
    private static byte ErrorCode(CollectionBoxAction action, CollectionBoxRejection reason) => action switch
    {
        CollectionBoxAction.Select or CollectionBoxAction.Extend => 0x5F,
        CollectionBoxAction.Withdraw => reason == CollectionBoxRejection.MissingItem ? (byte)0x01 : (byte)0x15,
        _ => reason switch
        {
            CollectionBoxRejection.UnknownBox => 0x5F,
            CollectionBoxRejection.MissingItem => 0x12,
            CollectionBoxRejection.LockedOrTimed => 0x17,
            _ => 0x15,
        },
    };
}
