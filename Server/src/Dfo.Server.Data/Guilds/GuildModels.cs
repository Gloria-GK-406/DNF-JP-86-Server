using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Data.Guilds;

public readonly record struct GuildId(uint Value);

public sealed record Guild(GuildId Id, string Name, string Promotion, int Level, int RecommendedChannel = 0);

public sealed record GuildPublicIdentity(GuildId Id, string Name, int Level);

public sealed record GuildMember(GuildId Guild, CharacterId Character, bool IsLeader, DateTimeOffset JoinedAt);

public sealed record GuildMembership(Guild Guild, GuildMember Member);

public sealed record GuildRosterEntry(GuildMember Membership, AccountId Account, byte[] Name,
    int Level, int Job, int GrowType);

public sealed record GuildApplication(GuildId Guild, CharacterId Character, string Message, DateTimeOffset AppliedAt);

public enum GuildMutationStatus
{
    Success,
    InvalidName,
    InvalidText,
    CreationUnavailable,
    NameTaken,
    InsufficientGold,
    CharacterUnavailable,
    GuildNotFound,
    AlreadyMember,
    NotMember,
    LeaderRequired,
    LeaderCannotLeave,
    ApplicationPending,
    ApplicationNotFound,
}

public sealed record GuildMutationResult(
    GuildMutationStatus Status,
    GuildId? Guild = null,
    int? RemainingGold = null,
    IReadOnlyList<CharacterId>? AffectedCharacters = null);
