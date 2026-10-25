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
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Dispatching;

namespace Dfo.Server.Protocol.Game.VendingMachine;

public sealed record GameVendingMachineCommand(NetworkDispatchContext Dispatch, VendingMachineRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameVendingMachineCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    IVendingMachineDomainService vending, ICommandWorkUnits units, ICommandProjections projections, IGameCommandOutbox outbox,
    ILogger<GameVendingMachineCommandHandler> logger) : ICommandHandler<GameVendingMachineCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameVendingMachineCommand command, CancellationToken ct)
    {
        var request = context.Request ?? throw new InvalidOperationException("Opening requires its explicit request.");
        var message = command.Input;
        var connection = command.Dispatch.Connection.ConnectionId;
        var session = sessions.FindByConnection(connection);
        if (session is null || request is CommandRequestContext.ProtocolEffectRejected || !sessions.IsInWorld(session)
            || !users.TryGetByConnection(connection, out var user) || user is null) return false;
        if (request is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != session)
            throw new InvalidOperationException("Opening requires its originally selected actor.");
        ActionOutcome action;
        if (message.MachineId > int.MaxValue || message.GroupId > int.MaxValue || message.Slot < 3)
            action = new(null, new OpeningRejectedException(OpeningRejection.InvalidRequest, "Invalid vending input."));
        else
        {
            var outcome = await units.WriteResultAsync(context, new("VendingMachine", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
                async (unit, token) =>
                {
                    var footprint = unit.Footprint;
                    try
                    {
                        return new ActionOutcome(await vending.UseAsync(session, (int)message.MachineId, (int)message.GroupId, OpeningWireSlots.FromMain(message.Slot), footprint, token).ConfigureAwait(false), null);
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
        byte error = 0;
        if (action.Failure is { } failure)
        {
            if (failure is OpeningRejectedException rejected)
            {
                error = rejected.Reason switch { OpeningRejection.NoRoom => 1, OpeningRejection.MissingMaterial => 22, _ => (byte)0 };
                logger.LogInformation("Vending rejected character={Character}: {Reason} {Detail}", session.CharacterId, rejected.Reason, rejected.Message);
            }
            else logger.LogError(failure, "Vending transaction failed character={Character}", session.CharacterId);
        }
        logger.LogInformation("Vending character={Character} machine={Machine} group={Group} slot={Slot} seq={Sequence} committed={Committed} reward={Reward} count={Count} remaining={Remaining}",
            session.CharacterId, message.MachineId, message.GroupId, message.Slot, message.Metadata.Sequence, result is not null, result?.Reward.ItemId, result?.Reward.Count, result?.Remaining);
        var response = new VendingMachineResponse(result, error);
        if (result is null) await outbox.RegisterCmdAsync(connection, response, ct).ConfigureAwait(false);
        else await projections.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]), new OpeningCompleted(result.Receipt, [response]), ct).ConfigureAwait(false);
        return result is not null;
    }
    private sealed record ActionOutcome(VendingMachineResult? Result, Exception? Failure);
}
