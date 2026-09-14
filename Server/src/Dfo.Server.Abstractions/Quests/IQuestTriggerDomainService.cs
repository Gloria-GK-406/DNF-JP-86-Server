using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Quests;

/// <summary>Applies one client quest-progress report under its domain authorization.</summary>
public interface IQuestTriggerDomainService
{
    ValueTask<QuestAdvance> ApplyClientReportAsync(
        AccountId accountId,
        CharacterId characterId,
        QuestDefinition definition,
        byte selector,
        bool increment,
        CancellationToken cancellationToken);
}
