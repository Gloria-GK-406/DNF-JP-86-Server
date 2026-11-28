using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.EquipmentCompound;

public sealed record GameEquipmentCompoundCommand(NetworkDispatchContext Dispatch, EquipmentCompoundRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameEquipmentCompoundCommandHandler(ISelectedSessionDomainService sessions,
    IEquipmentCompoundDomainService equipment, ICommandWorkUnits units, IGameCommandOutbox outbox,
    ICommandProjections projections, ILogger<GameEquipmentCompoundCommandHandler> logger)
    : ICommandHandler<GameEquipmentCompoundCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameEquipmentCompoundCommand command, CancellationToken ct)
    {
        var request = context.Request ?? throw new InvalidOperationException("Equipment compound requires its explicit request.");
        var input = command.Input;
        var connection = command.Dispatch.Connection.ConnectionId;
        var session = sessions.FindByConnection(connection);
        if (session is null || request is CommandRequestContext.ProtocolEffectRejected || !input.Valid || input.Mode > 1)
            return await RejectAsync().ConfigureAwait(false);
        if (request is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != session)
            throw new InvalidOperationException("Equipment compound requires its originally selected actor.");

        BusinessSlot source;
        try { source = OpeningWireSlots.FromMain(input.SourceSlot); }
        catch (OpeningRejectedException) { return await RejectAsync().ConfigureAwait(false); }

        var outcome = await units.WriteResultAsync(context,
            new("equipment-compound", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            async (unit, token) =>
            {
                try
                {
                    return new Settlement(await equipment.CompoundAsync(session, source, input.Mode == 0, input.Part,
                        unit.Footprint, token).ConfigureAwait(false), null);
                }
                catch (Exception failure) when (failure is not OperationCanceledException
                    && !TransactionOutcomeUncertainException.Contains(failure))
                {
                    unit.RequireRollback(failure);
                    return new Settlement(null, failure);
                }
            }, ct).ConfigureAwait(false);
        var settled = outcome switch
        {
            CommandWriteResult<Settlement>.Returned returned => returned.Value,
            CommandWriteResult<Settlement>.CommitRolledBack rollback => new Settlement(null, rollback.Cause),
            _ => throw new InvalidOperationException("Missing equipment compound settlement."),
        };
        if (settled.Receipt is not { } receipt)
        {
            if (settled.Failure is OpeningRejectedException or InventoryEffectException)
                logger.LogDebug("Equipment compound refused for {Character}: {Reason}", session.CharacterId, settled.Failure.Message);
            else logger.LogError(settled.Failure, "Equipment compound failed for {Character}", session.CharacterId);
            return await RejectAsync().ConfigureAwait(false);
        }

        await projections.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
            new EquipmentCompoundCompleted(receipt), ct).ConfigureAwait(false);
        return true;

        async ValueTask<bool> RejectAsync()
        {
            await outbox.RegisterCmdAsync(connection, new EquipmentCompoundResponse(null), ct).ConfigureAwait(false);
            return false;
        }
    }

    private sealed record Settlement(EquipmentCompoundReceipt? Receipt, Exception? Failure);
}
