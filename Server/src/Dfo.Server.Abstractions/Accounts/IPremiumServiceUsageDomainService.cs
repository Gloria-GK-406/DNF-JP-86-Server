using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
namespace Dfo.Server.Abstractions.Accounts;

public interface IPremiumServiceUsageDomainService
{
    string CurrentPeriod { get; }
    ValueTask<IReadOnlyList<CharacterPremiumUsageSlotState>> ReadAsync(CharacterId characterId, CancellationToken token);
    ValueTask<bool> TryUseFreeWeaknessRecoveryAsync(AccountId accountId, CharacterId characterId, CancellationToken token);
}
