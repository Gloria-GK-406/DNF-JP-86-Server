using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
namespace Dfo.Server.Abstractions.Accounts;

public interface IPremiumServiceUsageDomainService
{
    ValueTask RenewDoublePotAsync(AccountId accountId, CharacterId characterId, CancellationToken token);
    ValueTask<bool> TryUseDoublePotAsync(AccountId accountId, CharacterId characterId, CancellationToken token);
    ValueTask<bool> TryUseFreeGoldCardAsync(AccountId accountId, CharacterId characterId, CancellationToken token);
    ValueTask<bool> TryUseDungeonBuffAsync(AccountId accountId, CharacterId characterId, CancellationToken token);
    DateTimeOffset CurrentPeriodStart { get; }
    ValueTask<bool> TryUseAutoRepairAsync(AccountId accountId, CharacterId characterId, CancellationToken token);
    ValueTask<IReadOnlyList<CharacterPremiumUsageSlotState>> ReadAsync(CharacterId characterId, CancellationToken token);
    ValueTask<bool> TryUseFreeWeaknessRecoveryAsync(AccountId accountId, CharacterId characterId, CancellationToken token);
}
