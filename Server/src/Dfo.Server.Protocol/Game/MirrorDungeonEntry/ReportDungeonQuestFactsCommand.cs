using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Protocol.Game.MirrorDungeonEntry;

public sealed record ReportDungeonQuestFactsCommand(AccountId AccountId, CharacterId CharacterId,
    DungeonRunId RunId, IReadOnlyList<QuestProgressFact> Facts) : ICommand<bool>, IAccountCommand;

public sealed class ReportDungeonQuestFactsCommandHandler(IDungeonRunDomainService runs, IQuestProgressDomainService quests)
    : ICommandHandler<ReportDungeonQuestFactsCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, ReportDungeonQuestFactsCommand command,
        CancellationToken cancellationToken)
    {
        if (runs.Find(command.CharacterId)?.RunId != command.RunId) return false;
        await quests.ReportAsync(command.AccountId, command.CharacterId, command.Facts, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
