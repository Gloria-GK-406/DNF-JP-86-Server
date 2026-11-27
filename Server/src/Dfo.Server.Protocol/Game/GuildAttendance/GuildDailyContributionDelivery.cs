using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.GuildAttendance;

internal sealed class GuildDailyContributionDelivery(IGuildContributionDomainService contributions, ICommandWorkUnits units,
    ISelectedSessionDomainService sessions, IGameCommandOutbox outbox, TimeProvider clock,
    ILogger logger)
{
    public async ValueTask AwardAsync(CommandExecutionContext execution, SelectedSessionIdentity actor,
        GuildDailyContributionKind kind, CancellationToken ct)
    {
        if (!sessions.IsInWorld(actor)) return;
        Exception? failure = null;
        var result = await units.WriteResultAsync(execution, new("GuildDailyContribution", []), async (unit, token) =>
        {
            try
            {
                var receipt = await contributions.AwardDailyAsync(actor, kind, token).ConfigureAwait(false);
                return ItemGrantWireProjection.InventoryFrames(receipt, clock.GetUtcNow());
            }
            catch (Exception error) when (error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error))
            {
                failure = error;
                unit.RequireRollback(error);
                return (IReadOnlyList<IGameServerMessage>)[];
            }
        }, ct).ConfigureAwait(false);
        if (result is CommandWriteResult<IReadOnlyList<IGameServerMessage>>.CommitRolledBack rollback)
            failure = rollback.Cause;
        if (failure is not null)
            logger.LogWarning(failure, "Could not award guild {Kind} contribution to {Character}", kind, actor.CharacterId.Value);
        if (result is CommandWriteResult<IReadOnlyList<IGameServerMessage>>.Returned { Value.Count: > 0 } awarded
            && sessions.IsInWorld(actor))
            await outbox.RegisterExplicitAsync(actor.ConnectionId, new(awarded.Value, []), ct).ConfigureAwait(false);
    }
}
