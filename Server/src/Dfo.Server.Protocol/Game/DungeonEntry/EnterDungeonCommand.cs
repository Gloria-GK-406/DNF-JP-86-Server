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
    IHellDungeonEntryDomainService? hell = null, ICycleDungeonEntryDomainService? cycles = null,
    IMirrorDungeonEntryDomainService? mirrors = null, IDimensionDungeonDomainService? dimensions = null, IDespairTowerDomainService? despair = null,
    ITournamentDungeonDomainService? tournaments = null, IAltarDungeonDomainService? altars = null, ISaoTowerDomainService? sao = null,
    ITauKingdomDomainService? tau = null, IHerasDungeonDomainService? heras = null, ICoupleDungeonDomainService? couples = null,
    ISouthernDaleDomainService? southern = null, ISealedTreasureDomainService? sealedTreasure = null, IGoldSilverHornDomainService? goldSilverHorn = null) : ICommandHandler<EnterDungeonCommand, DungeonEntryAttempt>
{
    public ValueTask<DungeonEntryAttempt> HandleAsync(CommandExecutionContext context, EnterDungeonCommand command,
        CancellationToken cancellationToken) => couples?.Supports(command.Entry.DungeonId) == true
        ? couples.EnterAsync(command.Entry, cancellationToken)
        : goldSilverHorn?.Supports(command.Entry.DungeonId) == true
        ? goldSilverHorn.EnterAsync(command.Entry, cancellationToken)
        : sealedTreasure?.Supports(command.Entry.DungeonId) == true
        ? sealedTreasure.EnterAsync(command.Entry, cancellationToken)
        : southern?.Supports(command.Entry.DungeonId) == true
        ? southern.EnterAsync(command.Entry, cancellationToken)
        : heras?.Supports(command.Entry.DungeonId) == true
        ? heras.EnterAsync(command.Entry, cancellationToken)
        : tau?.Supports(command.Entry.DungeonId) == true
        ? tau.EnterAsync(command.Entry, cancellationToken)
        : altars?.Supports(command.Entry.DungeonId) == true
        ? altars.EnterAsync(command.Entry, cancellationToken)
        : tournaments?.Supports(command.Entry.DungeonId) == true
        ? tournaments.EnterAsync(command.Entry, cancellationToken)
        : sao?.Supports(command.Entry.DungeonId) == true
        ? sao.EnterAsync(command.Entry, cancellationToken)
        : despair?.IsTower(command.Entry.DungeonId) == true
        ? despair.EnterAsync(command.Entry, cancellationToken)
        : command.Entry.MirrorQuestId is not null
        ? mirrors?.EnterAsync(command.Entry, cancellationToken)
            ?? ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite))
        : command.Entry.Facts.Intent.Kind == DungeonEntryIntentKind.Cycle
        ? cycles?.EnterAsync(command.Entry, cancellationToken)
            ?? ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite))
        : decisions.ResolveEntryKind(command.Entry) switch
        {
            DungeonEntryKind.Normal when dimensions is not null => dimensions.EnterAsync(command.Entry, cancellationToken),
            DungeonEntryKind.Normal => visits.TryEnterPartyAsync(command.Entry.Participants, command.Entry.DungeonId,
                command.Entry.Facts, cancellationToken),
            DungeonEntryKind.ManualHell => hell?.EnterManualAsync(command.Entry, cancellationToken)
                ?? ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.HellUnavailable)),
            DungeonEntryKind.RandomHell => hell?.EnterRandomAsync(command.Entry, cancellationToken)
                ?? ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.HellUnavailable)),
            _ => throw new InvalidOperationException("Unknown dungeon entry kind."),
        };
}
