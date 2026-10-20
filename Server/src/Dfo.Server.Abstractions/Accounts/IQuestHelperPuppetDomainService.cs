using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Accounts;

public interface IQuestHelperPuppetDomainService
{
    /// <summary>Checks an eligible character once per business day, mailing only missing puppets.</summary>
    ValueTask<bool> EnsureDailyAsync(AccountId accountId, CharacterId characterId, CancellationToken token);
    ValueTask<PuppetDailyReconciliation> ReconcileAsync(AccountId accountId, CharacterId characterId, CancellationToken token);
}

public sealed record PuppetDailyReconciliation(bool Checked, IReadOnlyList<InventoryContainerType> ChangedContainers, bool MailboxChanged);

public interface IQuestHelperPuppetRuleService
{
    IReadOnlyList<int> PuppetItemIds { get; }
}
