using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.UsageQuotas;

/// <summary>One explicit-time, read-only projection of a quota family. Each domain keeps its reset rules.</summary>
public sealed record UsageQuotaSnapshot(DateTimeOffset At, IReadOnlyList<ItemUsageState> Items,
    AccountEntitlementEntryState? Entitlements, IReadOnlyList<CharacterPremiumUsageSlotState> PremiumUsage,
    MirrorDailyQuota? Mirror, DimensionQuotaSnapshot? Dimension = null, DespairTowerStatus? Despair = null, IReadOnlyList<AltarQuotaSnapshot>? Altars = null,
    IReadOnlyList<TournamentQuotaSnapshot>? Tournaments = null, HerasQuotaSnapshot? Heras = null, GoldSilverHornQuotaSnapshot? GoldSilverHorn = null, bool? ReviveCoinClaimed = null, LostLandQuotaSnapshot? LostLand = null);

public interface IUsageQuotaSnapshotDomainService
{
    ValueTask<UsageQuotaSnapshot> ReadAsync(AccountId accountId, CharacterId characterId,
        UsageQuotaFamily family, CancellationToken cancellationToken);
}

public sealed record UsageQuotaViewChanged(CharacterId CharacterId, UsageQuotaFamily Family)
    : INotification<UsageQuotaViewChanged>;
