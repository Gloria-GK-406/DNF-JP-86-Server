using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Scheduling;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildMonthlyContributionTask(GuildMonthlyContributionDomainService contributions, ICommandWorkUnits units)
    : IScheduledGameplayTask
{
    internal const string Id = "guild.monthly-contribution";
    private const int BatchSize = 64;

    public async ValueTask<ScheduledTaskBusinessResult> RunAsync(
        CommandExecutionContext execution, ScheduledTaskContext scheduled, CancellationToken ct)
    {
        int count;
        do
        {
            count = await units.WriteAsync(execution, new(Id, []),
                (_, token) => contributions.SettleBatchAsync(scheduled.ScheduledAt, BatchSize, token), ct).ConfigureAwait(false);
        } while (count == BatchSize);
        return ScheduledTaskBusinessResult.Success;
    }
}
