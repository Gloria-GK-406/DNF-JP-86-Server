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

namespace Dfo.Server.Protocol.Game.MagicBoxSingleOpening;

public sealed record GameMagicBoxSingleOpeningCommand(NetworkDispatchContext Dispatch, MagicBoxSingleOpeningRequest Input) : IProtocolCommand<MagicBoxOpeningResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameMagicBoxSingleOpeningCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    IMagicBoxOpeningDomainService boxes, TimeProvider clock, ICommandWorkUnits units, ICommandProjections projections,
    IGameCommandOutbox outbox, ILogger<GameMagicBoxSingleOpeningCommandHandler> logger) : ICommandHandler<GameMagicBoxSingleOpeningCommand, MagicBoxOpeningResult>
{
    public async ValueTask<MagicBoxOpeningResult> HandleAsync(CommandExecutionContext context, GameMagicBoxSingleOpeningCommand command, CancellationToken ct)
    {
        var request = context.Request ?? throw new InvalidOperationException("Opening requires its explicit request.");
        var message = command.Input;
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        MagicBoxOpeningResult result;
        if (message.Input is null) result = new(OpeningRejection.InvalidRequest);
        else if (session is null || request is CommandRequestContext.ProtocolEffectRejected
            || !onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var online) || online is null)
            result = new(OpeningRejection.NoSession);
        else
        {
            if (request is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != session)
                throw new InvalidOperationException("Opening requires its originally selected actor.");
            var outcome = await units.WriteResultAsync(context, new("MagicBoxSingleOpening", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
                async (unit, token) =>
                {
                    var footprint = unit.Footprint;
                    try { return new ActionOutcome(await boxes.OpenAsync(session, OpeningWireSlots.Business(message.Input), clock.GetUtcNow(), footprint, token).ConfigureAwait(false), null); }
                    catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
                    {
                        unit.RequireRollback(failure);
                        return new ActionOutcome(null, failure);
                    }
                }, ct).ConfigureAwait(false);
            result = outcome switch
            {
                CommandWriteResult<ActionOutcome>.Returned { Value: { Failure: { } failure } } => Refused(failure, command.Dispatch),
                CommandWriteResult<ActionOutcome>.Returned { Value: { Result: { } value } } => value,
                CommandWriteResult<ActionOutcome>.CommitRolledBack rollback => Refused(rollback.Cause, command.Dispatch),
                _ => throw new InvalidOperationException("Opening returned no action outcome."),
            };
        }
        logger.LogInformation("MagicBoxSingleOpening connection={ConnectionId} slot={Slot} count={Count} result={Result} detail={Detail} luck={Before}->{After}",
            command.Dispatch.Connection.ConnectionId, message.Input?.SourceSlot, message.Input?.Count, result.Rejection, result.Detail, result.LuckBefore, result.LuckAfter);
        var response = new MagicBoxSingleOpeningResponse(message.Input, result);
        if (result.Receipt is { } receipt)
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new OpeningCompleted(receipt, [response]), ct).ConfigureAwait(false);
        else await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId, response, ct).ConfigureAwait(false);
        return result;
    }
    private MagicBoxOpeningResult Refused(Exception failure, NetworkDispatchContext dispatch)
    {
        if (failure is OpeningRejectedException rejected) return new(rejected.Reason, Detail: rejected.Message);
        logger.LogError(failure, "Box opening storage failure for {ConnectionId}", dispatch.Connection.ConnectionId);
        return new(OpeningRejection.StorageFailure);
    }
    private sealed record ActionOutcome(MagicBoxOpeningResult? Result, Exception? Failure);
}
