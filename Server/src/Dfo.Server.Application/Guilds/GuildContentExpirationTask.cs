using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Scheduling;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

public sealed record GuildAttributeBenefitExpired(int BuffId) : INotification<GuildAttributeBenefitExpired>;

internal sealed class GuildContentExpirationTask(
    GuildBenefitDomainService benefits, IGuildMembershipQueryDomainService guilds, ICommandWorkUnits units, ICommandProjections projections, TimeProvider clock)
    : IScheduledGameplayTask
{
    public async ValueTask<ScheduledTaskBusinessResult> RunAsync(
        CommandExecutionContext execution, ScheduledTaskContext scheduled, CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var affected = await units.WriteAsync(execution, new("guild.content.expiration", []), async (_, token) =>
        {
            var entries = await benefits.RemoveExpiredAsync(now, 64, token).ConfigureAwait(false);
            var recipients = new List<(int BuffId, IReadOnlyList<CharacterId> Members)>();
            foreach (var expired in entries)
            {
                if (expired.Kind != GuildContentKind.TemporaryAttributes)
                    continue;
                var members = await guilds.ReadMemberIdsAsync(expired.Guild, token).ConfigureAwait(false);
                recipients.Add((expired.Value, members));
            }
            return recipients;
        }, ct).ConfigureAwait(false);

        foreach (var entry in affected)
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters(entry.Members),
                new GuildAttributeBenefitExpired(entry.BuffId), ct).ConfigureAwait(false);
        return ScheduledTaskBusinessResult.Success;
    }
}
