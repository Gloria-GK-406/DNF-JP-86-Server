using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Protocol.Game.QuestGiveUp;

public sealed record GiveUpQuestCommand(AccountId AccountId, CharacterId CharacterId, ushort QuestId)
    : ICommand<GiveUpQuestOutcome>;

public sealed record GiveUpQuestOutcome(bool GivenUp, ushort QuestId, QuestProgressSnapshot? QuestProgress)
{
    public static GiveUpQuestOutcome Refused(ushort questId) => new(false, questId, null);
}

public sealed class GiveUpQuestCommandHandler(ICharacterQuestLogDomainService logs,
    IQuestDefinitionDomainService? definitions = null, IMirrorAradQuestDomainService? mirror = null)
    : ICommandHandler<GiveUpQuestCommand, GiveUpQuestOutcome>
{
    public async ValueTask<GiveUpQuestOutcome> HandleAsync(
        CommandExecutionContext context, GiveUpQuestCommand command, CancellationToken cancellationToken)
    {
        if (context.ExecutionKind is not CommandExecutionKind.Transactional)
            throw new InvalidOperationException("Giving up a carried quest requires a transaction.");

        var definition = definitions is null ? null : await definitions.FindAsync(command.QuestId, cancellationToken).ConfigureAwait(false);
        if (definition?.Grade == Dfo.Server.Abstractions.Content.QuestGrade.Crack)
        {
            if (mirror is null || !await mirror.GiveUpAsync(command.AccountId, command.CharacterId, command.QuestId, cancellationToken).ConfigureAwait(false))
                return GiveUpQuestOutcome.Refused(command.QuestId);
            return new(true, command.QuestId, await mirror.SnapshotAsync(command.AccountId, command.CharacterId, cancellationToken).ConfigureAwait(false));
        }
        var log = logs.LogOf(command.AccountId, command.CharacterId);
        if (!await log.GiveUpAsync(command.QuestId, cancellationToken).ConfigureAwait(false))
            return GiveUpQuestOutcome.Refused(command.QuestId);

        var snapshot = new QuestProgressSnapshot(await log.CarriedAsync(cancellationToken).ConfigureAwait(false));
        if (mirror is not null) snapshot = await mirror.MergeAsync(command.CharacterId, snapshot, cancellationToken).ConfigureAwait(false);
        return new GiveUpQuestOutcome(true, command.QuestId, snapshot);
    }
}
