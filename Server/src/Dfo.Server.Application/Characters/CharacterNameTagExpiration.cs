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

public sealed record ExpireCharacterNameTagCommand(SelectedSessionIdentity Session)
    : ICommand<bool>, IAccountCommand, ISelectedSessionCommand
{
    public AccountId AccountId => Session.AccountId;
}

internal sealed class ExpireCharacterNameTagCommandHandler(ICharacterNameTagDomainService tags)
    : ICommandHandler<ExpireCharacterNameTagCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, ExpireCharacterNameTagCommand command, CancellationToken ct) =>
        tags.ExpireAsync(command.Session, ct);
}

internal static class CharacterNameTagRegistration
{
    internal static void AddCharacterNameTags(this IServiceCollection services)
    {
        services.TryAddSingleton<ICharacterNameTagRuleService, CharacterNameTagRuleService>();
        services.TryAddSingleton<ICharacterNameTagDomainService, CharacterNameTagDomainService>();
        services.AddCommandHandler<ExpireCharacterNameTagCommand, bool, ExpireCharacterNameTagCommandHandler>(
            CommandExecutionKind.Transactional, accountAccessMode: UserAccessMode.Exclusive);
        services.AddDomainEvent<CharacterNameTagExpired>("character.name-tag-expired", 1);
        services.AddScheduledTaskFactory(_ => new("character.name-tag.expiration",
            new IntervalTaskSchedule(TimeSpan.FromMinutes(1)), async (context, ct) =>
            {
                var online = context.Services.GetRequiredService<ISelectedSessionSnapshotQuery>();
                var commands = context.Services.GetRequiredService<ICommandBus>();
                var sessions = context.Services.GetRequiredService<ISelectedSessionDomainService>();
                var failures = new List<Exception>();
                foreach (var session in online.Snapshot())
                {
                    try { await commands.SendAsync(new ExpireCharacterNameTagCommand(session), ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception) when (!sessions.IsInWorld(session)) { }
                    catch (Exception error) { failures.Add(error); }
                }
                if (failures.Count > 0) throw new AggregateException("Online name tag expiration failed for some characters.", failures);
            }));
    }
}
