using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildContributionDomainService
{
    ValueTask<ItemDeliveryReceipt?> AwardDailyAsync(SelectedSessionIdentity actor, GuildDailyContributionKind kind, CancellationToken ct);
    ValueTask<ItemDeliveryReceipt?> AwardClearAsync(AccountId account, CharacterId character, DungeonRunState run,
        DateTimeOffset now, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildContributionMember>> ReadLeaderboardAsync(CharacterId character, CancellationToken ct);
}

public interface IGuildContributionStore
{
    ValueTask<bool> TryRecordDailyAsync(GuildId guild, CharacterId character, GuildDailyContributionKind kind,
        int day, long earnedAt, int points, CancellationToken ct);
    ValueTask<long> CountClearsAsync(CharacterId character, GuildClearContributionKind kind, int day, CancellationToken ct);
    ValueTask<bool> TryRecordClearAsync(GuildId guild, CharacterId character, Guid run, GuildClearContributionKind kind,
        int day, long earnedAt, int points, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildContributionMember>> ReadLeaderboardAsync(GuildId guild, CancellationToken ct);
}
