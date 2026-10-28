using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.Persistence;
namespace Dfo.Server.Abstractions.Accounts;

public sealed record DoublePotCounterState(PremiumServiceUsageRow? Round, PremiumServiceUsageRow? Total);
public sealed record DoublePotUsageFacts(bool Eligible, DoublePotCounterState Counters, int RoundUsed, int TotalUsed,
    int RoundLimit, int TotalLimit, DateTimeOffset At);
public sealed record DoublePotUsageDecision(DoublePotCounterState Expected, int NextRound, int NextTotal, DateTimeOffset At);

public interface IPremiumServiceUsageDomainService
{
    ValueTask<IReadOnlyList<CharacterPremiumUsageSlotState>> ReadAtAsync(CharacterId id, DateTimeOffset at, CancellationToken token) =>
        ValueTask.FromException<IReadOnlyList<CharacterPremiumUsageSlotState>>(new NotSupportedException("Explicit-time premium usage read is unavailable."));
    ValueTask<DoublePotUsageFacts> DoublePotFactsAsync(AccountId accountId, CharacterId characterId, DateTimeOffset at, CancellationToken token);
    ValueTask ApplyDoublePotAsync(CharacterId characterId, DoublePotUsageDecision decision, CancellationToken token);
    ValueTask RenewDoublePotAsync(CharacterId characterId, CancellationToken token);
    ValueTask<bool> TryUseFreeGoldCardAsync(AccountId accountId, CharacterId characterId, CancellationToken token);
    ValueTask<bool> TryUseDungeonBuffAsync(AccountId accountId, CharacterId characterId, CancellationToken token);
    DateTimeOffset CurrentPeriodStart { get; }
    ValueTask<bool> TryUseAutoRepairAsync(AccountId accountId, CharacterId characterId, CancellationToken token);
    ValueTask<IReadOnlyList<CharacterPremiumUsageSlotState>> ReadAsync(CharacterId characterId, CancellationToken token);
    ValueTask<bool> TryUseFreeWeaknessRecoveryAsync(AccountId accountId, CharacterId characterId, CancellationToken token);
}
