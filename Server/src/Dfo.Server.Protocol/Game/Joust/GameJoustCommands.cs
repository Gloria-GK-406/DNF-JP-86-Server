using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Joust;

public sealed record GameJoustQueryCommand(NetworkDispatchContext Dispatch, JoustQueryRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed record GameJoustBetCommand(NetworkDispatchContext Dispatch, JoustBetRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameJoustQueryCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    IJoustDomainService joust, TimeProvider clock, ICommandWorkUnits units, IGameCommandOutbox outbox)
    : ICommandHandler<GameJoustQueryCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameJoustQueryCommand command, CancellationToken ct)
    {
        var dispatch = command.Dispatch; var message = command.Input;
        if (sessions.FindByConnection(dispatch.Connection.ConnectionId) is not { } session)
        { await outbox.RegisterCmdAsync(dispatch.Connection.ConnectionId, new JoustResponse(message.Metadata.Type, 6), ct).ConfigureAwait(false); return false; }
        if (!sessions.IsInWorld(session) || !users.TryGetByConnection(session.ConnectionId, out var online)
            || online is null || !ReferenceEquals(online.Connection, dispatch.Connection)) return false;
        var recipient = new SelectedSessionRecipient(session, dispatch.Connection, message.Metadata);
        if (message.Metadata.Type == 0x04E8)
        {
            var history = await units.ReadOnlyAsync(execution, new("QueryJoustHistory", [recipient]),
                token => joust.GetHistoryAsync(token), ct).ConfigureAwait(false);
            await outbox.RegisterCmdAsync(dispatch.Connection.ConnectionId, new JoustResponse(0x04E8, 0, history), ct).ConfigureAwait(false);
        }
        else
        {
            var state = await units.ReadOnlyAsync(execution, new("QueryJoustState", [recipient]),
                token => joust.GetStateAsync(session.CharacterId, clock.GetUtcNow(), token), ct).ConfigureAwait(false);
            if (!state.Active) await outbox.RegisterCmdAsync(dispatch.Connection.ConnectionId, new JoustResponse(0x04E6, 6), ct).ConfigureAwait(false);
            else await outbox.RegisterBatchAsync(dispatch.Connection, [new JoustResponse(0x04E6, 0), .. JoustPayloadCodec.Snapshot(state)], ct).ConfigureAwait(false);
        }
        return true;
    }
}
public sealed class GameJoustBetCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    IJoustDomainService joust, TimeProvider clock, IInventorySlotMappingRuleService mapping,
    ICommandWorkUnits units, IGameCommandOutbox outbox, ICommandProjections projections,
    IReplayStateDomainService replay, IReplayStateProjectionSink replaySink, ISelectedSessionSnapshotQuery selectedSessions)
    : ICommandHandler<GameJoustBetCommand, bool>
{
    private sealed record BetOutcome(JoustBetResult? Result);
    private sealed class PhysicalIdentity { public string Id { get; } = Guid.NewGuid().ToString("N"); }
    private static readonly ConditionalWeakTable<NetworkConnectionContext, PhysicalIdentity> Identities = new();
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameJoustBetCommand command, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(GameJoustBetCommandHandler) + "." + nameof(HandleAsync));
        var dispatch = command.Dispatch; var message = command.Input;
        ValueTask Refuse() => outbox.RegisterCmdAsync(dispatch.Connection.ConnectionId, new JoustResponse(0x04E7, 6), ct);
        var request = execution.Request;
        if (sessions.FindByConnection(dispatch.Connection.ConnectionId) is not { } session
            || !users.TryGetByConnection(dispatch.Connection.ConnectionId, out var user) || user is null
            || message.Metadata.InboundOrdinal is not > 0 || request is CommandRequestContext.ProtocolEffectRejected)
        { await Refuse().ConfigureAwait(false); return false; }
        if (!ReferenceEquals(user.Connection, dispatch.Connection) || !sessions.IsInWorld(session)) return false;
        if (request is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != session)
            throw new InvalidOperationException("Joust requires its originally selected actor.");
        request.DemandCurrent();
        // Invalid wire addresses were refused before opening the old settlement transaction.
        BusinessSlot slot;
        try { slot = mapping.ToBusinessSlot(new(InventoryContainerType.Main, message.MaterialSlotIndex)); }
        catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
        { await Refuse().ConfigureAwait(false); return false; }
        var requestId = $"joust:{Identities.GetOrCreateValue(dispatch.Connection).Id}:{session.ConnectionGeneration}:{message.Metadata.InboundOrdinal}";
        var outcome = await units.WriteResultAsync(execution, new("JoustBet", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            async (unit, token) =>
            {
                unit.Footprint.DemandActive(session);
                JoustBetResult result;
                try { result = await joust.BetAsync(session, requestId, message.KnightIndex, slot, message.Amount, clock.GetUtcNow(), token).ConfigureAwait(false); }
                catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
                { unit.RequireRollback(failure); return new BetOutcome(null); }
                unit.Footprint.RecordInventorySlots(session, result.Debits.Select(debit => debit.Slot).ToArray());
                return new BetOutcome(result);
            }, ct).ConfigureAwait(false);
        if (outcome is not CommandWriteResult<BetOutcome>.Returned { Value.Result: { } bet })
        { await Refuse().ConfigureAwait(false); return false; }
        if (bet.Succeeded)
        {
            await projections.ProjectAsync(new NotificationAudience.TheseSessions(selectedSessions.Snapshot()), new JoustDetailsChanged(bet.State.ServerTime), ct).ConfigureAwait(false);
            var snapshot = await units.WriteAsync(execution, new("ReadReplayState", [selected.Recipient]),
                (_, token) => replay.ReadAsync(session, new(ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.Joust,
                    bet.Debits.Select(debit => debit.Slot).ToImmutableArray()), message.Metadata, token), ct).ConfigureAwait(false);
            await replaySink.ProjectAsync(dispatch.Connection, session, message.Metadata, snapshot, null, ct).ConfigureAwait(false);
        }
        request.DemandCurrent();
        await outbox.RegisterBatchAsync(dispatch.Connection, [new JoustResponse(0x04E7, bet.Succeeded ? 0u : 6u)], ct).ConfigureAwait(false);
        return bet.Succeeded;
    }
}
