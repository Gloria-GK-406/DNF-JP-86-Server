using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Marriage;

// The four faces below are implemented by one marriage entity; callers inject only the face they use.

public interface IMarriageQueryDomainService
{
    /// <summary>
    /// The actor's relation with its effective stage: a proposal or reservation that has lapsed but is not yet
    /// settled shows the stage it lapses to. Nothing is written.
    /// </summary>
    ValueTask<MarriageView?> ViewAsync(SelectedSessionIdentity actor, CancellationToken ct);
    ValueTask<MarriageEngagementSnapshot> SnapshotAsync(AccountId account, CharacterId character, CancellationToken ct);
    ValueTask<MarriageProposal?> ReceivedProposalAsync(CharacterId character, CancellationToken ct);
    ValueTask<MarriageCeremony?> ReceivedReservationAsync(CharacterId character, CancellationToken ct);
    ValueTask<bool> IsMarriedAsync(Guid relation, CancellationToken ct);
    ValueTask<MarriageWeddingPhase?> WeddingPhaseAsync(Guid ceremony, CancellationToken ct);
    ValueTask<MarriageChurchSnapshot?> AppearanceAsync(SelectedSessionIdentity actor, CancellationToken ct);
    ValueTask<bool> CanCrossAsync(SelectedSessionIdentity actor, int channelId, byte townId, byte areaId, CancellationToken ct);
    ValueTask<bool> CanLeaveAsync(SelectedSessionIdentity actor, byte town, byte area, CancellationToken ct);
    ValueTask<ushort> RemainingInvitationsAsync(Guid ceremony, CharacterId actor, CancellationToken ct);
    ValueTask<IReadOnlyList<uint>> InvitedAsync(Guid ceremony, CancellationToken ct);
    ValueTask<IReadOnlyList<AccountId>> CoupleAccountsAsync(SelectedSessionIdentity actor, CancellationToken ct);
    ValueTask<IReadOnlyList<AccountId>> WeddingAccountsAsync(Guid ceremony, CancellationToken ct);
    ValueTask<IReadOnlyList<Guid>> ActiveWeddingsAsync(CancellationToken ct);
}

public interface IMarriageCoupleDomainService
{
    ValueTask<MarriageProposalResult> ProposeAsync(SelectedSessionIdentity proposer, SelectedSessionIdentity recipient, CancellationToken ct);
    ValueTask<MarriageProposalResult> RespondProposalAsync(SelectedSessionIdentity recipient, SelectedSessionIdentity proposer,
        byte response, CancellationToken ct);
    ValueTask<MarriageCeremonyResult> RequestReservationAsync(SelectedSessionIdentity applicant, SelectedSessionIdentity partner,
        int tier, int waitIndex, CancellationToken ct);
    ValueTask<MarriageCeremonyResult> RespondReservationAsync(SelectedSessionIdentity partner, SelectedSessionIdentity applicant,
        byte response, CancellationToken ct);
    /// <summary>Allocates the church, admits both, charges the applicant and opens the wedding of an accepted reservation.</summary>
    /// <exception cref="MarriageWeddingUnavailableException">Any step failed; the caller's transaction must roll back.</exception>
    ValueTask<IReadOnlyList<MarriageWeddingAdmission>> OpenWeddingAsync(MarriageCeremony ceremony, CancellationToken ct);
    ValueTask<MarriageLovePointResult> UseLovePointAsync(SelectedSessionIdentity actor, BusinessSlot slot,
        IReadOnlyList<AccountId> accounts, CancellationToken ct);
}

public interface IMarriageVenueDomainService
{
    /// <summary>The actor's church; allocates one when the reserved pair has none yet.</summary>
    ValueTask<MarriageChurchSnapshot?> PrepareChurchAsync(SelectedSessionIdentity actor, CancellationToken ct);
    /// <param name="flag">0 enters a newlywed, 1 a guest; anything else is refused.</param>
    ValueTask<MarriageChurchEntryResult> EnterChurchAsync(SelectedSessionIdentity actor, uint key, uint area, byte flag, CancellationToken ct);
    ValueTask<MarriageWeddingLetterResult> InviteAsync(SelectedSessionIdentity actor, byte mode, uint target, CancellationToken ct);
    ValueTask<int> GiftAsync(SelectedSessionIdentity actor, uint key, int gold, CancellationToken ct);
    ValueTask<MarriageWeddingTransition?> AdvanceAsync(Guid ceremony, CancellationToken ct);
    ValueTask CompleteCleanupAsync(Guid ceremony, CharacterId character, CancellationToken ct);
    ValueTask<MarriageGuestbookResult> ReadGuestbookAsync(SelectedSessionIdentity actor, uint key, CancellationToken ct);
    ValueTask<int> PostGuestbookAsync(SelectedSessionIdentity actor, uint key, byte[] text, CancellationToken ct);
    ValueTask<int> DeleteGuestbookAsync(SelectedSessionIdentity actor, uint key, uint id, CancellationToken ct);
}

public interface IMarriageQuestDomainService
{
    ValueTask<IReadOnlyCollection<int>> EligibleQuestIdsAsync(AccountId account, CharacterId character, CancellationToken ct);
    ValueTask<bool> CanAcceptAsync(AccountId account, CharacterId character, int quest, CancellationToken ct);
    ValueTask BindAsync(AccountId account, CharacterId character, int quest, CancellationToken ct);
    ValueTask<bool> CanCompleteAsync(AccountId account, CharacterId character, int quest, CancellationToken ct);
}
