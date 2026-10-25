using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

using Dfo.Server.Application.Dispatching;

namespace Dfo.Server.Protocol.Game.EpicCraft;

public sealed record GameEpicCraftCommand(NetworkDispatchContext Dispatch, EpicCraftRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameEpicCraftCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    IEpicCraftDomainService crafting, TimeProvider clock, ICommandWorkUnits units, ICommandProjections projections, IGameCommandOutbox outbox,
    ILogger<GameEpicCraftCommandHandler> logger) : ICommandHandler<GameEpicCraftCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameEpicCraftCommand command, CancellationToken ct)
    {
        var request = context.Request ?? throw new InvalidOperationException("Opening requires its explicit request.");
        var message = command.Input;
        var connection = command.Dispatch.Connection.ConnectionId;
        var session = sessions.FindByConnection(connection);
        if (session is null || request is CommandRequestContext.ProtocolEffectRejected || !message.Valid || message.EquipmentId <= 0
            || !users.TryGetByConnection(connection, out var user) || user is null)
        { await outbox.RegisterCmdAsync(connection, new EpicCraftResponse(false, 2), ct); return false; }
        if (request is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != session)
            throw new InvalidOperationException("Opening requires its originally selected actor.");
        var outcome = await units.WriteResultAsync(context, new("EpicCraft", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            async (unit, token) =>
            {
                var footprint = unit.Footprint;
                try
                {
                    return new ActionOutcome(await crafting.CraftAsync(session, message.EquipmentId, clock.GetUtcNow(), footprint, token).ConfigureAwait(false), null);
                }
                catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
                {
                    unit.RequireRollback(failure);
                    return new ActionOutcome(null, failure);
                }
            }, ct).ConfigureAwait(false);
        var action = outcome switch
        {
            CommandWriteResult<ActionOutcome>.Returned returned => returned.Value,
            CommandWriteResult<ActionOutcome>.CommitRolledBack rollback => new ActionOutcome(null, rollback.Cause),
            _ => throw new InvalidOperationException("Opening returned no action outcome."),
        };
        var result = action.Result;
        byte error = 2;
        if (action.Failure is { } failure)
        {
            if (failure is OpeningRejectedException rejected)
            {
                error = rejected.Reason switch { OpeningRejection.MissingMaterial => 0x16, OpeningRejection.NoRoom => 4, _ => (byte)2 };
                logger.LogInformation("Epic craft rejected character={Character} equipment={Equipment} reason={Reason}", session.CharacterId, message.EquipmentId, rejected.Reason);
            }
            else logger.LogError(failure, "Epic craft transaction failed character={Character}", session.CharacterId);
        }
        if (result is null) await outbox.RegisterCmdAsync(connection, new EpicCraftResponse(false, error), ct).ConfigureAwait(false);
        else
        {
            logger.LogInformation("Epic craft committed character={Character} equipment={Equipment} piece={Piece} balance={Balance}", session.CharacterId, message.EquipmentId, result.Pieces.PieceId, result.Pieces.Count);
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]), new EpicCraftCompleted(result), ct).ConfigureAwait(false);
        }
        return result is not null;
    }
    private sealed record ActionOutcome(EpicCraftReceipt? Result, Exception? Failure);
}
