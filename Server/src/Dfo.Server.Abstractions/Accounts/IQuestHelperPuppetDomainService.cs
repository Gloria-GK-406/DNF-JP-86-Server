using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Accounts;

public interface IQuestHelperPuppetDomainService
{
    /// <summary>Checks an eligible character once per business day, mailing only missing puppets.</summary>
    ValueTask<bool> EnsureDailyAsync(AccountId accountId, CharacterId characterId, CancellationToken token);
}

public interface IQuestHelperPuppetRuleService
{
    IReadOnlyList<int> PuppetItemIds { get; }
}
