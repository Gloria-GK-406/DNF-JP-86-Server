using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Marriage;

public enum MarriageProposalState { Pending, Engaged, Rejected, Expired }

public sealed record MarriageProposal(
    Guid Id, SelectedSessionIdentity Proposer, SelectedSessionIdentity Recipient,
    MarriageProposalState State, long Revision, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    byte? Response = null);

public enum MarriageProposalFailure { None, Invalid, Unavailable, Ineligible, Occupied, Stale }

public sealed record MarriageProposalResult(
    MarriageProposalFailure Failure, MarriageProposal? Proposal = null, bool Changed = false)
{
    public bool Succeeded => Failure == MarriageProposalFailure.None;
}

public sealed record MarriageProposalContent(int MinimumLevel, int TrialQuestId = 20737, int MaterialQuestId = 20738);

public sealed record MarriageCeremonyTier(int Tier, int Gold, int GiftTier, int OpaqueValue)
{
    public int GiftItemId { get; init; }
}

public sealed record MarriageCeremonyContent(
    IReadOnlyList<MarriageCeremonyTier> Tiers,
    IReadOnlyList<int> WaitDurations)
{
    public int GiftMinimum { get; init; } = 10000;
    public int GiftMaximum { get; init; } = 400000000;
    public int GiftChargePercent { get; init; } = 50;
    public int InvitationCutoffSeconds { get; init; } = 60;
    public IReadOnlyList<MarriageGuestReward> GuestRewards { get; init; } = [];
}

public enum MarriageCeremonyState { Pending, Reserved, Rejected, Expired }

public sealed record MarriageCeremony(
    Guid Id,
    Guid RelationId,
    SelectedSessionIdentity Applicant,
    SelectedSessionIdentity Partner,
    int Tier,
    int WaitIndex,
    MarriageCeremonyState State,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    byte? Response = null);

public enum MarriageCeremonyFailure
{
    None,
    Invalid,
    Unavailable,
    Ineligible,
    Occupied,
    InsufficientFunds,
    Stale,
}

public sealed record MarriageCeremonyResult(
    MarriageCeremonyFailure Failure,
    MarriageCeremony? Ceremony = null,
    bool Changed = false)
{
    public bool Succeeded => Failure == MarriageCeremonyFailure.None;
    public IReadOnlyList<MarriageWeddingAdmission> Admissions { get; init; } = [];
}

public sealed record MarriagePartnerProfile(int CharacterId, byte[] Name, byte Job, int Level,
    byte GrowType, IReadOnlyList<int> Equipment);

public sealed record MarriageEngagementSnapshot(Guid? RelationId, MarriagePartnerProfile? Partner)
{
    public bool IsEngaged => RelationId is not null && Partner is not null;
    public bool IsCeremonyReserved { get; init; }
    public bool IsMarried { get; init; }
    public static MarriageEngagementSnapshot Empty { get; } = new(null, null);
}

public sealed record MarriageQuestBinding(Guid RelationId, DateTimeOffset AcceptedAt);

public sealed record MarriageGuestReward(int MaximumGold, int Tier, int ItemId, int Count);
