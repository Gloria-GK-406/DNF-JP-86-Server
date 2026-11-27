using Dfo.Server.Data.Characters;

namespace Dfo.Server.Data.Guilds;

public enum GuildDailyContributionKind : byte { Attendance = 1, Chat = 2 }
public enum GuildClearContributionKind : byte { GuildDungeon = 1, PartyDungeon = 2 }

public sealed record GuildContributionMember(CharacterId Character, string Name, int Rank, int Contribution);
public sealed record GuildClearContributionRule(int Required, int Points);
