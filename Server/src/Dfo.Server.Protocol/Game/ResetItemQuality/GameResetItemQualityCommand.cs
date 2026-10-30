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

namespace Dfo.Server.Protocol.Game.ResetItemQuality;

public sealed record GameResetItemQualityCommand(NetworkDispatchContext Dispatch, ResetItemQualityRequest Input) : IProtocolCommand<EquipmentQualityResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameResetItemQualityCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers, IEquipmentQualityDomainService quality, IEquipmentResealDomainService reseal,
    ICommandWorkUnits units, ICommandProjections projections, IGameCommandOutbox outbox, ILogger<GameResetItemQualityCommandHandler> logger)
    : ICommandHandler<GameResetItemQualityCommand, EquipmentQualityResult>
{
    public async ValueTask<EquipmentQualityResult> HandleAsync(CommandExecutionContext context, GameResetItemQualityCommand command, CancellationToken ct)
    {
        var request = context.Request ?? throw new InvalidOperationException("Quality reset requires its explicit request.");
        var message = command.Input;
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        if (session is null || !message.Valid || !onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var online)
            || online is null || request is CommandRequestContext.ProtocolEffectRejected)
        {
            await Reject(0x13).ConfigureAwait(false);
            return new(EquipmentQualityRejection.NoSession);
        }
        BusinessSlot target;
        BusinessSlot material;
        try
        {
            target = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, message.TargetSlot));
            material = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, message.MaterialSlot));
        }
        catch (InventorySlotMappingException)
        {
            await Reject(0x13).ConfigureAwait(false);
            return new(EquipmentQualityRejection.InvalidRequest);
        }
        if (request is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != session)
            throw new InvalidOperationException("Quality reset requires its originally selected actor.");
        var outcome = await units.WriteResultAsync(context, new("ResetItemQuality", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            async (unit, token) =>
            {
                try { return new ActionOutcome(await reseal.TryResealAsync(session, target, message.ExpectedItemId, material, token).ConfigureAwait(false) ?? await quality.RerollAsync(session, target, message.ExpectedItemId, material, token).ConfigureAwait(false), null); }
                catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
                {
                    unit.RequireRollback(failure);
                    return new ActionOutcome(null, failure);
                }
            }, ct).ConfigureAwait(false);
        EquipmentQualityResult result;
        switch (outcome)
        {
            case CommandWriteResult<ActionOutcome>.Returned { Value: { Result: { } value } }: result = value; break;
            case CommandWriteResult<ActionOutcome>.Returned { Value: { Failure: { } failure } }:
                return await Failed(failure).ConfigureAwait(false);
            case CommandWriteResult<ActionOutcome>.CommitRolledBack rollback:
                return await Failed(rollback.Cause).ConfigureAwait(false);
            default: throw new InvalidOperationException("Quality reset returned no outcome.");
        }
        if (result.Receipt is not { } receipt)
        {
            logger.LogInformation("Equipment attribute reset rejected character={Character} reason={Reason}", session.CharacterId, result.Rejection);
            await Reject(result.Rejection switch
            {
                EquipmentQualityRejection.MaterialUnavailable => 0x11,
                EquipmentQualityRejection.AlreadySealed => 0x12,
                EquipmentQualityRejection.ResealLimitReached => 0x0D,
                _ => (byte)0x13,
            }).ConfigureAwait(false);
            return result;
        }
        logger.LogInformation("Equipment attribute reset committed character={Character} target={Slot} item={Item} operation={Operation} resealCount={ResealCount} seed={Seed} materialRemaining={Count}",
            session.CharacterId, message.TargetSlot, message.ExpectedItemId, receipt.Operation, receipt.Equipment.ResealCount,
            receipt.Equipment.InstanceValue, receipt.MaterialRemaining);
        await projections.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
            new EquipmentQualityCompleted(receipt), ct).ConfigureAwait(false);
        return result;

        ValueTask Reject(byte error) => outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId, new ResetItemQualityResponse(false, Error: error), ct);
        async ValueTask<EquipmentQualityResult> Failed(Exception failure)
        {
            logger.LogError(failure, "Equipment attribute reset transaction failed character={Character}", session.CharacterId);
            await Reject(0x13).ConfigureAwait(false);
            return new(EquipmentQualityRejection.InvalidRequest);
        }
    }
    private sealed record ActionOutcome(EquipmentQualityResult? Result, Exception? Failure);
}
