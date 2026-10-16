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

public sealed record MarriagePartnerProfile(int CharacterId, byte[] Name, byte Job, int Level,
    byte GrowType, IReadOnlyList<int> Equipment);

public sealed record MarriageEngagementSnapshot(Guid? RelationId, MarriagePartnerProfile? Partner)
{
    public bool IsEngaged => RelationId is not null && Partner is not null;
    public static MarriageEngagementSnapshot Empty { get; } = new(null, null);
}

public sealed record MarriageQuestBinding(Guid RelationId, DateTimeOffset AcceptedAt);
