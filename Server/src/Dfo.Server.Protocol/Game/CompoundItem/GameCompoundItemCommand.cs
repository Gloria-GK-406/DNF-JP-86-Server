using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Dispatching;

namespace Dfo.Server.Protocol.Game.CompoundItem;

public sealed record GameCompoundItemCommand(NetworkDispatchContext Dispatch, CompoundItemRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameCompoundItemCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    IRecipeCraftDomainService recipes, TimeProvider clock, ICommandWorkUnits units, ICommandProjections projections, IGameCommandOutbox outbox,
    ILogger<GameCompoundItemCommandHandler> logger) : ICommandHandler<GameCompoundItemCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameCompoundItemCommand command, CancellationToken ct)
    {
        var request = context.Request ?? throw new InvalidOperationException("Opening requires its explicit request.");
        var message = command.Input;
        var connection = command.Dispatch.Connection.ConnectionId;
        var session = sessions.FindByConnection(connection);
        if (session is null || request is CommandRequestContext.ProtocolEffectRejected || !message.Valid || message.SourceMode != 0
            || message.Count is < 1 or > 1000 || !users.TryGetByConnection(connection, out var user) || user is null)
        { await outbox.RegisterCmdAsync(connection, new CompoundItemResponse(null, 0x11), ct); return false; }
        if (request is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != session)
            throw new InvalidOperationException("Opening requires its originally selected actor.");
        BusinessSlot drawing = default;
        OpeningRejectedException? invalid = null;
        try { drawing = OpeningWireSlots.FromMain(message.Source); }
        catch (OpeningRejectedException invalidFailure) { invalid = invalidFailure; }
        ActionOutcome action;
        if (invalid is not null) action = new(null, invalid);
        else
        {
            var outcome = await units.WriteResultAsync(context, new("CompoundItem", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
                async (unit, token) =>
                {
                    var footprint = unit.Footprint;
                    try
                    {
                        return new ActionOutcome(await recipes.CraftAsync(session, drawing, message.Count, clock.GetUtcNow(), footprint, token).ConfigureAwait(false), null);
                    }
                    catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
                    {
                        unit.RequireRollback(failure);
                        return new ActionOutcome(null, failure);
                    }
                }, ct).ConfigureAwait(false);
            action = outcome switch
            {
                CommandWriteResult<ActionOutcome>.Returned returned => returned.Value,
                CommandWriteResult<ActionOutcome>.CommitRolledBack rollback => new ActionOutcome(null, rollback.Cause),
                _ => throw new InvalidOperationException("Opening returned no action outcome."),
            };
        }
        var result = action.Result;
        byte error = 0x11;
        if (action.Failure is { } failure)
        {
            if (failure is OpeningRejectedException rejected)
            {
                logger.LogInformation("Compound rejected: {Reason} {Detail}", rejected.Reason, rejected.Message);
                error = OpeningPayloads.Error(rejected.Reason);
            }
            else logger.LogError(failure, "Compound transaction failed for {Character}", session.CharacterId);
        }
        if (result is null) await outbox.RegisterCmdAsync(connection, new CompoundItemResponse(null, error), ct).ConfigureAwait(false);
        else
        {
            logger.LogInformation("Compound committed character={Character} drawingSlot={Slot} count={Count}", session.CharacterId, message.Source, message.Count);
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]), new OpeningCompleted(result.Inventory, [new CompoundItemResponse(result)]), ct).ConfigureAwait(false);
        }
        return result is not null;
    }
    private sealed record ActionOutcome(RecipeCraftReceipt? Result, Exception? Failure);
}
