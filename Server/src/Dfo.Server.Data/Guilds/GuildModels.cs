using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Data.Guilds;

public readonly record struct GuildId(uint Value);

public sealed record Guild(GuildId Id, string Name, string Promotion, int Level, int RecommendedChannel = 0,
    int GmPoints = 0, string Announcement = "", byte Mark = 1, int SpentGmPoints = 0)
{
    public int ContributionPoints => checked(GmPoints + SpentGmPoints);
}

public sealed record GuildPublicIdentity(GuildId Id, string Name, int Level, byte Mark = 1);

public sealed record GuildMarkPricing(int FirstPaidMark, int PointCost)
{
    public const int MarkCount = 16;
    public int Price(byte mark) => mark >= FirstPaidMark ? PointCost : 0;
}

public sealed record GuildMember(GuildId Guild, CharacterId Character, bool IsLeader, DateTimeOffset JoinedAt,
    int DonationPoints = 0, byte MemberRank = GuildRankSettings.Member)
{
    public byte Rank => IsLeader ? GuildRankSettings.Leader : MemberRank;
}

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

public sealed record GuildRankAssignmentResult(CharacterId Character, byte Rank, bool Changed);
public sealed record GuildDiscoveryDetails(GuildId Guild, string Name, int MemberCount, string Promotion);
public sealed record GuildAccountEntry(byte[] CharacterName, string GuildName);
public sealed record GuildRecommendation(GuildId Guild, string Name, byte[] LeaderName);

public sealed record GuildApplication(GuildId Guild, CharacterId Character, string Message, DateTimeOffset AppliedAt);
public sealed record GuildPendingApplication(GuildApplication Application, string GuildName);
public sealed record GuildApplicationEntry(GuildApplication Application, byte[] Name, int Job, int GrowType, int Level);
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
    InvalidMark,
    MarkPricingUnavailable,
    InsufficientGuildPoints,
}

public sealed record GuildMutationResult(
    GuildMutationStatus Status,
    GuildId? Guild = null,
    int? RemainingGold = null,
    IReadOnlyList<CharacterId>? AffectedCharacters = null);
