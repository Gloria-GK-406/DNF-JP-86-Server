using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Data.Guilds;

public readonly record struct GuildId(uint Value);

public sealed record Guild(GuildId Id, string Name, string Promotion, int Level, int RecommendedChannel = 0, int GmPoints = 0);

public sealed record GuildPublicIdentity(GuildId Id, string Name, int Level);

public sealed record GuildMember(GuildId Guild, CharacterId Character, bool IsLeader, DateTimeOffset JoinedAt,
    int DonationPoints = 0);

public sealed record GuildMembership(Guild Guild, GuildMember Member);

public sealed record GuildRosterEntry(GuildMember Membership, AccountId Account, byte[] Name,
    int Level, int Job, int GrowType);

public sealed record GuildPresenceAudience(CharacterId Character, byte[] Name, IReadOnlyList<CharacterId> OtherMembers)
{
    public static GuildPresenceAudience? FromRoster(CharacterId character, IReadOnlyList<GuildRosterEntry> roster)
    {
        if (roster.Count < 2 || roster.FirstOrDefault(row => row.Membership.Character == character) is not { } member)
            return null;

        return new(character, member.Name,
            [.. roster.Select(row => row.Membership.Character).Where(id => id != character)]);
    }
}

public sealed record GuildApplication(GuildId Guild, CharacterId Character, string Message, DateTimeOffset AppliedAt);
public sealed record GuildDonationEntry(byte[] DonorName, int Gold, int Points, DateTimeOffset DonatedAt);

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
    InvalidDonationAmount,
    DonationTooLarge,
    DonationUnavailable,
    ContributionLimit,
    InvalidRankSetting,
}

public sealed record GuildMutationResult(
    GuildMutationStatus Status,
    GuildId? Guild = null,
    int? RemainingGold = null,
    IReadOnlyList<CharacterId>? AffectedCharacters = null);
