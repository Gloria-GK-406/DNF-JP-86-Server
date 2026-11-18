using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.AvatarCompound;

public sealed record GameAvatarCompoundPlusCommand(NetworkDispatchContext Dispatch, AvatarCompoundPlusRequest Input)
    : IProtocolCommand<AvatarCompoundResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameAvatarCompoundPlusCommandHandler(ISelectedSessionDomainService sessions,
    IAvatarCompoundPlusDomainService avatars, ICommandWorkUnits units, ICommandProjections projections,
    IGameCommandOutbox outbox, ILogger<GameAvatarCompoundPlusCommandHandler> logger)
    : ICommandHandler<GameAvatarCompoundPlusCommand, AvatarCompoundResult>
{
    public async ValueTask<AvatarCompoundResult> HandleAsync(CommandExecutionContext context, GameAvatarCompoundPlusCommand command,
        CancellationToken ct)
    {
        var connection = command.Dispatch.Connection.ConnectionId;
        if (context.Request is not CommandRequestContext.ProtocolEffectSelected selected
            || sessions.FindByConnection(connection) is not { } session || selected.Recipient.Session != session)
            return await Reject(AvatarCompoundRejection.NoSession).ConfigureAwait(false);
        if (command.Input.Input is not { } input)
            return await Reject(AvatarCompoundRejection.InvalidRequest).ConfigureAwait(false);

        var settlement = await units.WriteResultAsync(context,
            new("AvatarCompoundPlus", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            async (unit, token) =>
            {
                try
                {
                    return await avatars.CompoundPlusAsync(session, input, unit.Footprint, token).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is not OperationCanceledException
                    && !TransactionOutcomeUncertainException.Contains(failure))
                {
                    unit.RequireRollback(failure);
                    logger.LogError(failure, "Bind Plus failed for character {Character}", session.CharacterId);
                    return new AvatarCompoundResult(AvatarCompoundRejection.StorageFailure);
                }
            }, ct).ConfigureAwait(false);

        if (settlement is CommandWriteResult<AvatarCompoundResult>.CommitRolledBack rollback)
        {
            logger.LogError(rollback.Cause, "Bind Plus commit rolled back for character {Character}", session.CharacterId);
            return await Reject(AvatarCompoundRejection.StorageFailure).ConfigureAwait(false);
        }
        if (settlement is not CommandWriteResult<AvatarCompoundResult>.Returned returned)
            throw new InvalidOperationException("Bind Plus returned no outcome.");
        var result = returned.Value;
        if (result.Receipt is not { } receipt)
            return await Reject(result.Rejection).ConfigureAwait(false);
        await projections.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
            new AvatarCompoundPlusCompleted(receipt), ct).ConfigureAwait(false);
        logger.LogInformation("Bind Plus character={Character} target={ItemId} option={Ability}",
            session.CharacterId, input.TargetItemId, input.Ability);
        return result;

        async ValueTask<AvatarCompoundResult> Reject(AvatarCompoundRejection reason)
        {
            await outbox.RegisterCmdAsync(connection, new AvatarCompoundPlusResponse(null), ct).ConfigureAwait(false);
            return new(reason);
        }
    }
}
