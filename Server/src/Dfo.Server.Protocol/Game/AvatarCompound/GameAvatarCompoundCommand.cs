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

namespace Dfo.Server.Protocol.Game.AvatarCompound;

public sealed record GameAvatarCompoundCommand(NetworkDispatchContext Dispatch, AvatarCompoundRequest Input) : IProtocolCommand<AvatarCompoundResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameAvatarCompoundCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    IInventorySlotMappingRuleService mappings, IAvatarCompoundDomainService avatars, ICommandWorkUnits units, ICommandProjections projections,
    IGameCommandOutbox outbox, ILogger<GameAvatarCompoundCommandHandler> logger) : ICommandHandler<GameAvatarCompoundCommand, AvatarCompoundResult>
{
    public async ValueTask<AvatarCompoundResult> HandleAsync(CommandExecutionContext context, GameAvatarCompoundCommand command, CancellationToken ct)
    {
        var requestContext = context.Request ?? throw new InvalidOperationException("Avatar compound requires its explicit request.");
        var request = command.Input;
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        if (!request.Valid || session is null || !onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var online)
            || online is null || requestContext is CommandRequestContext.ProtocolEffectRejected)
        { await Reject(0x17).ConfigureAwait(false); return new(AvatarCompoundRejection.NoSession); }
        AvatarCompoundInput? input = null;
        try
        {
            input = new(mappings.ToBusinessSlot(new(InventoryContainerType.Main, request.MaterialSlot)),
                mappings.ToBusinessSlot(new(InventoryContainerType.Avatar, request.FirstSlot)), request.FirstItemId,
                mappings.ToBusinessSlot(new(InventoryContainerType.Avatar, request.SecondSlot)), request.SecondItemId,
                request.TargetItemId, request.Ability, request.RandomTarget, request.Category);
        }
        catch (InventorySlotMappingException) { }
        if (input is null)
        { await Reject(0x17).ConfigureAwait(false); return new(AvatarCompoundRejection.InvalidRequest); }
        if (requestContext is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != session)
            throw new InvalidOperationException("Avatar compound requires its originally selected actor.");
        var outcome = await units.WriteResultAsync(context, new("AvatarCompound", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            async (unit, token) =>
            {
                var footprint = unit.Footprint;
                try { return new ActionOutcome(await avatars.CompoundAsync(session, input, footprint, token).ConfigureAwait(false), null); }
                catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
                {
                    unit.RequireRollback(failure);
                    return new ActionOutcome(null, failure);
                }
            }, ct).ConfigureAwait(false);
        AvatarCompoundResult result;
        switch (outcome)
        {
            case CommandWriteResult<ActionOutcome>.Returned { Value: { Result: { } value } }: result = value; break;
            case CommandWriteResult<ActionOutcome>.Returned { Value: { Failure: { } failure } }: return await Failed(failure).ConfigureAwait(false);
            case CommandWriteResult<ActionOutcome>.CommitRolledBack rollback: return await Failed(rollback.Cause).ConfigureAwait(false);
            default: throw new InvalidOperationException("Avatar compound returned no outcome.");
        }
        logger.LogInformation("Avatar compound character={Character} material={Material} first={First}/{FirstId} second={Second}/{SecondId} target={Target} category={Category} result={Result} outputs={Outputs} rate={Rate}",
            session.CharacterId, request.MaterialSlot, request.FirstSlot, request.FirstItemId, request.SecondSlot,
            request.SecondItemId, request.TargetItemId, request.Category, result.Rejection,
            result.Receipt?.Outputs.Length ?? 0, result.Receipt?.RareRate ?? 0);
        if (result.Receipt is { } receipt)
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]), new AvatarCompoundCompleted(receipt), ct).ConfigureAwait(false);
        else await Reject(result.Rejection switch
        {
            AvatarCompoundRejection.MissingMaterial => 0x16,
            AvatarCompoundRejection.UnsupportedLifetime => 0x13,
            AvatarCompoundRejection.Locked => 0xD2,
            _ => 0x17,
        }).ConfigureAwait(false);
        return result;
        ValueTask Reject(byte error) => outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId, new AvatarCompoundResponse(null, error), ct);
        async ValueTask<AvatarCompoundResult> Failed(Exception failure)
        {
            logger.LogError(failure, "Avatar compound failed character={Character}", session.CharacterId);
            await Reject(0x17).ConfigureAwait(false); return new(AvatarCompoundRejection.InvalidRequest);
        }
    }
    private sealed record ActionOutcome(AvatarCompoundResult? Result, Exception? Failure);
}
