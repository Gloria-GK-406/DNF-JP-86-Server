using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Data.Guilds;

public sealed record GuildMonthlyContributionRule(int RecommendedClearPoints, int Target, int ItemId, int Count);

public enum GuildContributionSource
{
    Attendance = 2,
    GoldDonation = 3,
    RecommendedClear = 4,
    LeaderAttendance = 6,
}

public sealed record GuildContributionEntry(byte[] CharacterName, int Points, DateTimeOffset EarnedAt,
    GuildContributionSource Source);

public sealed record GuildContributionAmounts(uint Lifetime, uint Monthly);

public sealed record GuildContributionTotals(
    GuildContributionAmounts Guild, IReadOnlyDictionary<CharacterId, GuildContributionAmounts> Members);

public sealed record GuildMonthlyContributionCandidate(AccountId Account, CharacterId Character, GuildId Guild, long Points);
