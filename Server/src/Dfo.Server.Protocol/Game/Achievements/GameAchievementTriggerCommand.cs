using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Achievements;

public sealed record GameAchievementTriggerCommand(NetworkDispatchContext Dispatch, AchievementTriggerRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameAchievementTriggerCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    IAchievementDomainService achievements, ICommandWorkUnits units, IGameCommandOutbox outbox, ILogger<GameAchievementTriggerCommandHandler> logger)
    : ICommandHandler<GameAchievementTriggerCommand, bool>
{
    private sealed record Reconciliation(CharacterAchievementProgressState? Progress);
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameAchievementTriggerCommand command, CancellationToken ct)
    {
        var context = command.Dispatch; var message = command.Input;
        CharacterAchievementProgressState? progress = null;
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (message.Complete && session is not null && users.TryGetByConnection(context.Connection.ConnectionId, out var user) && user is not null)
        {
            Exception? actionFailure = null;
            var write = await units.WriteResultAsync(execution, new("ReconcileAchievement", [new(session, context.Connection, message.Metadata)]),
                async (unit, token) => {
                    try
                    {
                        // All three deltas are observations. Only authoritative facts may advance progress.
                        return new Reconciliation(await achievements.ReconcileAsync(session.CharacterId, message.AchievementId, token).ConfigureAwait(false));
                    }
                    catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
                    {
                        actionFailure = failure; unit.RequireRollback(failure); return new Reconciliation(null);
                    }
                }, ct).ConfigureAwait(false);
            var cause = write is CommandWriteResult<Reconciliation>.CommitRolledBack rollback ? rollback.Cause : actionFailure;
            if (cause is not null) logger.LogWarning(cause, "Achievement trigger refused character={CharacterId} achievement={AchievementId}", session.CharacterId, message.AchievementId);
            else progress = ((CommandWriteResult<Reconciliation>.Returned)write).Value.Progress;
        }
        await outbox.RegisterCmdAsync(context.Connection.ConnectionId, new AchievementTriggerResponse(message.AchievementId, progress), ct).ConfigureAwait(false);
        return progress is not null;
    }
}
