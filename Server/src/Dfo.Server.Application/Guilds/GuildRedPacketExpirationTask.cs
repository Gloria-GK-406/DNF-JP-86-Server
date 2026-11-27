using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Scheduling;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildRedPacketExpirationTask(IGuildRedPacketDomainService packets, ICommandWorkUnits units)
    : IScheduledGameplayTask
{
    public async ValueTask<ScheduledTaskBusinessResult> RunAsync(
        CommandExecutionContext execution, ScheduledTaskContext scheduled, CancellationToken ct)
    {
        _ = await units.WriteAsync(execution, new("guild.red-packet.expiration", []), async (_, token) =>
        {
            await packets.ExpireAsync(token).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
        return ScheduledTaskBusinessResult.Success;
    }
}
