using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Quests;

/// <summary>Reads unmet item requirements of currently carried quests; never changes progress.</summary>
public interface IQuestMaterialRequirementDomainService
{
    ValueTask<IReadOnlySet<int>> GetOutstandingMaterialIdsAsync(
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken);
}
