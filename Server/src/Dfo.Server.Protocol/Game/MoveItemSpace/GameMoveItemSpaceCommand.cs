using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.MoveItemSpace;

public sealed record GameMoveItemSpaceCommand(NetworkDispatchContext Dispatch, MoveItemSpaceRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

/// <summary>
/// Turns the request into a command and the command's answer into something the client can read.
/// </summary>
/// <remarks>
/// The source echoes the request. The destination normally does too, except that an automatic
/// cargo deposit reports the actual empty slot selected by the domain service.
/// </remarks>
public sealed class GameMoveItemSpaceCommandHandler(
    ISelectedSessionDomainService selectedSessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    IInventorySlotMappingRuleService slotMappings,
    IInventoryMoveDomainService inventoryMoves,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    ILogger<GameMoveItemSpaceCommandHandler> logger,
    ICharacterInventoryEntryDomainService characterInventories,
    IAccountInventoryEntryDomainService accountInventories,
    TimeProvider time,
    Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService housing) : ICommandHandler<GameMoveItemSpaceCommand, bool>
{
    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext execution,
        GameMoveItemSpaceCommand command,
        CancellationToken cancellationToken)
    {
        var context = command.Dispatch;
        var message = command.Input;
        var identity = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        var (response, result) = await ResolveAsync(execution, context, message, cancellationToken)
            .ConfigureAwait(false);
        List<IGameServerMessage> frames = [response];
        if (result is not null && (IsCargo(response.SourceSlot) || IsCargo(response.DestinationSlot)))
        {
            try
            {
                await AppendCargoStateAsync(context, response, frames, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(failure, "Move item space {ConnectionId}: cargo snapshot failed after settlement",
                    context.Connection.ConnectionId);
            }
        }
        if (result?.Status is ItemMoveStatus.Moved or ItemMoveStatus.MirrorRemoved)
        {
            // Nothing raised while working out the appearance may cost the player the
            // acknowledgement. The move is committed by this point and the client holds the
            // inventory shut until it is answered -- which is the very symptom this frame exists to
            // remove, and it would come back wearing an unrelated cause. So the appearance is
            // attempted, and a failure to build it leaves the answer as it was before this work:
            // the acknowledgement, alone.
            try
            {
                if (await this.RefreshSessionAsync(
                        context,
                        ChangesWhatTheCharacterLooksLike(
                            response.SourceSlot, response.DestinationSlot),
                        cancellationToken)
                        .ConfigureAwait(false)
                    is { } appearance)
                {
                    frames.AddRange(appearance);
                    if (identity is not null) frames.AddRange(await HousingCurrentProjection.ReadAsync(housing, identity, cancellationToken).ConfigureAwait(false));
                }
            }
            catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(
                    failure,
                    "Move item space {ConnectionId}: the move stands; inventory refresh or appearance failed",
                    context.Connection.ConnectionId);
            }
        }

        await outbox.RegisterExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(frames, []),
            cancellationToken).ConfigureAwait(false);
        return result?.Status is ItemMoveStatus.Moved or ItemMoveStatus.Unchanged or ItemMoveStatus.MirrorRemoved;
    }

    private static bool IsCargo(GameSlot slot) =>
        slot.Space is InventoryContainerType.PersonalCargo or InventoryContainerType.AccountCargo;

    private async ValueTask AppendCargoStateAsync(NetworkDispatchContext context, MoveItemSpaceResponse response,
        List<IGameServerMessage> frames, CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null) return;
        var character = await characterInventories.ReadCurrentAsync(session.AccountId, session.CharacterId,
            cancellationToken).ConfigureAwait(false);
        var account = await accountInventories.LoadAsync(session.AccountId, session.CharacterId,
            cancellationToken).ConfigureAwait(false);
        if (!selectedSessions.IsInWorld(session)) return;
        var preparedAt = time.GetUtcNow();
        // A full container snapshot also removes phantom entries after a refused stale move.
        // The CMD must arrive first: it applies the client's local movement before reconciliation.
        foreach (var type in new[] { response.SourceSlot.Space, response.DestinationSlot.Space }.Distinct())
        {
            var container = type == InventoryContainerType.AccountCargo
                ? account.Cargo.Container
                : character.Containers.SingleOrDefault(value => value.ContainerType == type);
            if (container is not null)
                frames.Add(new InventoryFullSnapshotMessage(container, character, account, preparedAt));
        }
    }

    /// <summary>
    /// Whether this request changes the equipment represented by the complete USERINFO pair.
    /// </summary>
    /// <remarks>
    /// Decided from the request's two ends rather than from what the move turned out to do. The
    /// appearance is drawn from the equipment list, so a move that changed it named that list at one
    /// of its ends -- and reversing a request exchanges its two ends without changing either list,
    /// so this holds whichever end the item was actually on.
    /// </remarks>
    private static bool ChangesWhatTheCharacterLooksLike(
        GameSlot source, GameSlot destination) =>
        source.Space == InventoryContainerType.Equipment || destination.Space == InventoryContainerType.Equipment;

    /// <summary>Reads the post-move equipment from its owner for the optional appearance frame.</summary>
    private async ValueTask<IReadOnlyList<IGameServerMessage>?> RefreshSessionAsync(
        NetworkDispatchContext context, bool draw, CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null || !draw)
        {
            return null;
        }

        var state = await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (state is null)
            return null;

        return CharacterUserStateProjection.Compose(state);
    }

    private async ValueTask<(MoveItemSpaceResponse Response, ItemMoveResult? Result)> ResolveAsync(
        CommandExecutionContext execution,
        NetworkDispatchContext context,
        MoveItemSpaceRequest message,
        CancellationToken cancellationToken)
    {
        var source = slotMappings.ToBusinessSlot(message.SourceSlot);
        var destination = slotMappings.ToBusinessSlot(message.DestinationSlot);
        var responseSource = slotMappings.ToGameSlot(source);
        var responseDestination = slotMappings.ToGameSlot(destination);
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogWarning(
                "Move item space {ConnectionId}: no published session",
                context.Connection.ConnectionId);
            return (MoveItemSpaceResponse.Failure(
                responseSource, responseDestination, MoveItemSpaceResponse.Silent), null);
        }

        var result = await units.WriteAsync(execution, new("MoveItemSpace", []),
            (_, token) => inventoryMoves.MoveAsync(new InventoryMoveRequest(
                session.CharacterId,
                source,
                message.MoveCount,
                destination,
                message.SourceInstanceValue,
                message.DestinationInstanceValue) { Session = session }, token),
            cancellationToken).ConfigureAwait(false);

        if (result.Status is ItemMoveStatus.Moved or ItemMoveStatus.Unchanged or ItemMoveStatus.MirrorRemoved)
        {
            if (result.ActualDestinationSlot is { } actualDestination)
                responseDestination = slotMappings.ToGameSlot(actualDestination);
            logger.LogInformation(
                "Move item space {ConnectionId} character={CharacterId}: item {ItemId} "
                    + "from list {FromList} slot {From} to list {ToList} slot {To} count={Count}",
                context.Connection.ConnectionId,
                session.CharacterId.Value,
                result.ItemId,
                (byte)responseSource.Space,
                responseSource.Index,
                (byte)responseDestination.Space,
                responseDestination.Index,
                result.MoveValue);
            return (
                MoveItemSpaceResponse.Success(
                    responseSource, result.MoveValue, responseDestination),
                result);
        }

        // `{ItemId}` goes in beside the two ends the request already named. That pair is what a
        // slot map was read from, and the costume map was read from it: while
        // `PvfEquipmentSlots.ByName` carried no name ending in `avatar]`, a costume reaching
        // `PutOnAsync` got nothing back from the archive and was refused as `NotWearable`, and this
        // line carried its template beside the slot the client had asked for. Eleven costumes
        // dragged one at a time gave eleven slots that way, and those are the numbers now in the
        // table -- so a costume aimed at the wrong slot is the ordinary `ItemCannotOccupySlot`
        // refusal today, and `NotWearable` is left for a template the archive names no slot for at
        // all. The cases are in `RefusedEquipIsRecordedWithItsTemplateTests`. The server has no
        // opinion about which slot an avatar belongs in and the client does, so `{To}` is the
        // client's answer and `{ItemId}` says what it was answering about. The one-argument `ItemMoveResult.Refused`
        // puts a zero here, for a refusal decided before any slot was read.
        logger.LogInformation(
            "Move item space {ConnectionId} character={CharacterId}: item {ItemId} "
                + "from list {FromList} slot {From} to list {ToList} slot {To} refused as {Status}",
            context.Connection.ConnectionId,
            session.CharacterId.Value,
            result.ItemId,
            (byte)responseSource.Space,
            responseSource.Index,
            (byte)responseDestination.Space,
            responseDestination.Index,
            result.Status);
        return (MoveItemSpaceResponse.Failure(
            responseSource, responseDestination, ErrorCodeFor(result.Status)), result);
    }

    /// <summary>
    /// The client sentence that is true of this refusal, or silence when it owns none.
    /// </summary>
    /// <remarks>
    /// Two empty slots, a missing session and a missing binding each take silence. None of them is
    /// something the player did, and the client has no sentence for "we disagree about your
    /// inventory" -- so the drag simply springs back, which is what it would do if the frame had
    /// been lost.
    /// <para>
    /// A lost race used to be on that list. It is not any more, because it can no longer happen:
    /// a move reads and writes inside one use case boundary and has no second read to disagree with
    /// the first. Two moves arriving at once now both go through, the later settling over the
    /// earlier, which `MoveItemSpaceHandlerTests` records as the attempt to construct it.
    /// </para>
    /// <para>
    /// Every refusal is listed rather than falling through, so the next one someone adds cannot
    /// become silence by default while the test that walks this enum stays green.
    /// </para>
    /// <para>
    /// Raising is not free: the dispatcher does not catch, so the plugin logs and rethrows and the
    /// player's connection drops. That is the right trade only because the raise is unreachable
    /// except through a coding mistake, and because the test that walks this enum is what keeps it
    /// unreachable.
    /// </para>
    /// </remarks>
    internal static byte ErrorCodeFor(ItemMoveStatus status) => status switch
    {
        ItemMoveStatus.NotWearable => MoveItemSpaceResponse.NotMovable,
        ItemMoveStatus.ListNotServed => MoveItemSpaceResponse.NotMovable,
        ItemMoveStatus.ItemCannotOccupySlot => MoveItemSpaceResponse.NotMovable,
        ItemMoveStatus.PartialSwapNotAllowed => MoveItemSpaceResponse.NotMovable,
        ItemMoveStatus.DisplacedCannotOccupySlot => MoveItemSpaceResponse.NotMovable,
        ItemMoveStatus.MergeExceedsStackLimit => MoveItemSpaceResponse.StackLimitReached,
        ItemMoveStatus.ContainerFull => MoveItemSpaceResponse.ContainerFull,
        ItemMoveStatus.NoSession => MoveItemSpaceResponse.Silent,
        ItemMoveStatus.NoBoundContainer => MoveItemSpaceResponse.Silent,
        ItemMoveStatus.BothSlotsEmpty => MoveItemSpaceResponse.Silent,
        ItemMoveStatus.MoveAlreadyMade => MoveItemSpaceResponse.Silent,
        ItemMoveStatus.Moved or ItemMoveStatus.Unchanged => throw new ArgumentOutOfRangeException(
            nameof(status), status, "A move that happened has no error code."),
        _ => throw new ArgumentOutOfRangeException(
            nameof(status), status, "No client code was decided for this refusal."),
    };
}
