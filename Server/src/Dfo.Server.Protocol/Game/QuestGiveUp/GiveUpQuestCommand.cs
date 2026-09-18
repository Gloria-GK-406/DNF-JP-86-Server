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

public sealed class GiveUpQuestCommandHandler(ICharacterQuestLogDomainService logs)
    : ICommandHandler<GiveUpQuestCommand, GiveUpQuestOutcome>
{
    public async ValueTask<GiveUpQuestOutcome> HandleAsync(
        CommandExecutionContext context, GiveUpQuestCommand command, CancellationToken cancellationToken)
    {
        if (context.ExecutionKind is not CommandExecutionKind.Transactional)
            throw new InvalidOperationException("Giving up a carried quest requires a transaction.");

        var log = logs.LogOf(command.AccountId, command.CharacterId);
        if (!await log.GiveUpAsync(command.QuestId, cancellationToken).ConfigureAwait(false))
            return GiveUpQuestOutcome.Refused(command.QuestId);

        return new GiveUpQuestOutcome(true, command.QuestId,
            new QuestProgressSnapshot(await log.CarriedAsync(cancellationToken).ConfigureAwait(false)));
    }
}
