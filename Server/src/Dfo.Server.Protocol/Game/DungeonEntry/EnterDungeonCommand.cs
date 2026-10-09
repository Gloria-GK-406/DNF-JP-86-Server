using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Protocol.Game.DungeonEntry;

public sealed record EnterDungeonCommand(DungeonEntryContext Entry) : ICommand<DungeonEntryAttempt>, IMultiAccountCommand
{
    public IReadOnlyList<AccountId> AccountIds => Entry.Participants.Select(x => x.AccountId).Distinct().ToArray();
}

/// <summary>Route all entry kinds here; packet and projection layers do not choose game modes.</summary>
public sealed class EnterDungeonCommandHandler(
    IDungeonEntryDecisionDomainService decisions, IDungeonVisitDomainService visits,
    IHellDungeonEntryDomainService? hell = null) : ICommandHandler<EnterDungeonCommand, DungeonEntryAttempt>
{
    public ValueTask<DungeonEntryAttempt> HandleAsync(CommandExecutionContext context, EnterDungeonCommand command,
        CancellationToken cancellationToken) => decisions.ResolveEntryKind(command.Entry) switch
        {
            DungeonEntryKind.Normal => visits.TryEnterPartyAsync(command.Entry.Participants, command.Entry.DungeonId,
                command.Entry.Facts, cancellationToken),
            DungeonEntryKind.ManualHell => hell?.EnterManualAsync(command.Entry, cancellationToken)
                ?? ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.HellUnavailable)),
            DungeonEntryKind.RandomHell => hell?.EnterRandomAsync(command.Entry, cancellationToken)
                ?? ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.HellUnavailable)),
            _ => throw new InvalidOperationException("Unknown dungeon entry kind."),
        };
}
