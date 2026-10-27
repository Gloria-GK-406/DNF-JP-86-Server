using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Scheduling;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterNameTagExpirationJob(ISelectedSessionSnapshotQuery online, ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    ICharacterNameTagDomainService tags, ICommandWorkUnits units) : IScheduledGameplayTask
{
    public async ValueTask<ScheduledTaskBusinessResult> RunAsync(CommandExecutionContext execution, ScheduledTaskContext scheduled, CancellationToken ct)
    {
        var failures = new List<Exception>();
        foreach (var session in online.Snapshot())
        {
            if (!sessions.IsInWorld(session)) continue;
            Exception? actionFailure = null;
            var result = await units.WriteResultAsync(execution, new("character.name-tag.expiration", [ScheduledSessionRecipient.Capture(session, users)]), async (unit, token) =>
            {
                try { return await tags.ExpireAsync(session, token).ConfigureAwait(false); }
                catch (Exception error) when (error is not OperationCanceledException
                    && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(error)
                    && !Dfo.Server.Application.DomainEvents.DomainEventScopeCleanupException.Contains(error))
                {
                    execution.DemandRequest();
                    unit.RequireRollback(error);
                    actionFailure = error;
                    return false;
                }
            }, ct).ConfigureAwait(false);
            var error = result is CommandWriteResult<bool>.CommitRolledBack rolledBack ? rolledBack.Cause : actionFailure;
            if (error is not null && sessions.IsInWorld(session))
            {
                failures.Add(error);
            }
        }
        return ScheduledTaskBusinessResult.Handled(execution, "Online name tag expiration failed for some characters.", failures);
    }
}

internal static class CharacterNameTagRegistration
{
    internal static void AddCharacterNameTags(this IServiceCollection services)
    {
        services.TryAddSingleton<ICharacterNameTagRuleService, CharacterNameTagRuleService>();
        services.TryAddSingleton<ICharacterNameTagDomainService, CharacterNameTagDomainService>();
        services.AddDomainEvent<CharacterNameTagExpired>("character.name-tag-expired", 1);
        services.AddScoped<CharacterNameTagExpirationJob>();
        services.AddScheduledTaskFactory(sp => new("character.name-tag.expiration", new IntervalTaskSchedule(TimeSpan.FromMinutes(1)), typeof(CharacterNameTagExpirationJob)));

    }
}
