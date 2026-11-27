using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Data.Guilds;

public readonly record struct GuildId(uint Value);

public sealed record Guild(GuildId Id, string Name, string Promotion, int Level, int RecommendedChannel = 0,
    int GmPoints = 0, string Announcement = "", byte Mark = 1, int SpentGmPoints = 0)
{
    public int ContributionPoints => checked(GmPoints + SpentGmPoints);
    public GuildMedalAura MedalAura { get; init; } = GuildMedalAura.None;
}

public sealed record GuildPublicIdentity(GuildId Id, string Name, int Level, byte Mark = 1);

public readonly record struct GuildMedalAura(int Symbol, int Frame)
{
    public static GuildMedalAura None { get; } = new(-1, -1);
}

public sealed record GuildMedalAuraCatalog(int SymbolCount, int FrameCount)
{
    public bool Contains(GuildMedalAura aura) => aura.Symbol >= 0 && aura.Symbol < SymbolCount
        && aura.Frame >= 0 && aura.Frame < FrameCount;
}

public sealed record GuildMarkPricing(int FirstPaidMark, int PointCost)
{
    public const int MarkCount = 16;
    public int Price(byte mark) => mark >= FirstPaidMark ? PointCost : 0;
}

public sealed record GuildMember(GuildId Guild, CharacterId Character, bool IsLeader, DateTimeOffset JoinedAt,
    int DonationPoints = 0, byte MemberRank = GuildRankSettings.Member, string Memo = "")
{
    public const int MaximumMemoCharacters = 30;
    public const int MaximumMemoBytes = 60;

    public long MembershipId { get; init; }

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
public sealed record GuildLeadershipTransfer(GuildMember FormerLeader, GuildMember NewLeader);
public sealed record GuildDiscoveryDetails(GuildId Guild, string Name, int MemberCount, string Promotion);
public sealed record GuildAccountEntry(byte[] CharacterName, string GuildName);
public sealed record GuildRecommendation(GuildId Guild, string Name, byte[] LeaderName);

public sealed record GuildApplication(GuildId Guild, CharacterId Character, string Message, DateTimeOffset AppliedAt);
public sealed record GuildPendingApplication(GuildApplication Application, string GuildName);
public sealed record GuildApplicationEntry(GuildApplication Application, byte[] Name, int Job, int GrowType, int Level);
public sealed record GuildDonationEntry(byte[] DonorName, int Gold, int Points, DateTimeOffset DonatedAt);

public enum GuildContentKind : byte
{
    Experience = 1,
    TemporaryAttributes = 3,
    WarehouseExpansion = 4,
    PermanentAttributes = 5,
    PartyTeleport = 7,
}

public sealed record GuildContentDefinition(uint ProductId, GuildContentKind Kind, int Value, int Price,
    int DurationDays = 0, bool CanGift = false);
public sealed record GuildPurchasedContent(
    GuildContentKind Kind, uint ProductId, int Value, int RemainingUses = 0, long ExpiresAtUnixSeconds = 0)
{
    public bool IsActive(long now) => ExpiresAtUnixSeconds == 0 || ExpiresAtUnixSeconds > now;
}
public sealed record GuildAttributeBonus(int BuffId, int PhysicalAttack, int PhysicalDefense, int MagicalAttack, int MagicalDefense);
public sealed record GuildExpiredContent(GuildId Guild, GuildContentKind Kind, int Value);
public sealed record GuildGoldRenameOffer(uint ProductId, int GoldCost)
{
    public const int VoucherItemId = 490001375;
}

public sealed record GuildContentCatalog(
    IReadOnlyDictionary<uint, GuildContentDefinition> Products, GuildAttributeBonus? Attributes,
    GuildAttributeBonus? TemporaryAttributes = null, int? WarehouseCapacityLimit = null,
    GuildGoldRenameOffer? GoldRename = null);

public enum GuildMutationStatus
{
    Success,
    InvalidName,
    InvalidText,
    InvalidMedalAura,
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
    UnsupportedContent,
    ContentAlreadyActive,
    ContentDurationLimit,
    WarehouseCapacityLimit,
    WarehouseNotEmpty,
    GuildHasOtherMembers,
}

public sealed record GuildMutationResult(
    GuildMutationStatus Status,
    GuildId? Guild = null,
    int? RemainingGold = null,
    IReadOnlyList<CharacterId>? AffectedCharacters = null,
    int? WarehouseCapacity = null);
