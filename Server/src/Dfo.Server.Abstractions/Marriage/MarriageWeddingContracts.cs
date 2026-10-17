using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Marriage;

public interface IMarriageWeddingDomainStore
{
    ValueTask<MarriageWedding?> ReadAsync(Guid ceremony, CancellationToken ct);
    ValueTask<IReadOnlyList<MarriageWedding>> PendingAsync(CancellationToken ct);
    ValueTask CreateAsync(MarriageWedding wedding, CancellationToken ct);
    ValueTask<bool> FinishAsync(Guid ceremony, MarriageWeddingPhase phase, CancellationToken ct);
    ValueTask<bool> IsMarriedAsync(Guid relation, CancellationToken ct);
    ValueTask<IReadOnlyList<MarriageWeddingInvitation>> InvitationsAsync(Guid ceremony, CancellationToken ct);
    ValueTask InviteAsync(MarriageWeddingInvitation invitation, CancellationToken ct);
    ValueTask<bool> PayAsync(Guid ceremony, CharacterId guest, int gold, CancellationToken ct);
    ValueTask QueueCleanupAsync(Guid ceremony, MarriageWeddingCleanup cleanup, CancellationToken ct);
    ValueTask<IReadOnlyList<MarriageWeddingCleanup>> CleanupAsync(Guid ceremony, CancellationToken ct);
    ValueTask CompleteCleanupAsync(Guid ceremony, CharacterId character, CancellationToken ct);
}
public interface IMarriageWeddingDomainService
{
    ValueTask<bool> IsMarriedAsync(Guid relation, CancellationToken ct);
    ValueTask<bool> CanLeaveAsync(SelectedSessionIdentity actor, byte town, byte area, CancellationToken ct);
    ValueTask<MarriageWeddingPhase?> PhaseAsync(Guid ceremony, CancellationToken ct);
    ValueTask CompleteCleanupAsync(Guid ceremony, CharacterId character, CancellationToken ct);
    ValueTask<MarriageChurchSnapshot?> AppearanceAsync(SelectedSessionIdentity actor, CancellationToken ct);
    ValueTask<IReadOnlyList<MarriageWeddingAdmission>> PrepareAsync(MarriageCeremony ceremony, CancellationToken ct);
    ValueTask<IReadOnlyList<MarriageWedding>> PendingAsync(CancellationToken ct);
    ValueTask<IReadOnlyList<AccountId>> AccountsAsync(Guid ceremony, CancellationToken ct);
    ValueTask<MarriageWeddingTransition?> AdvanceAsync(Guid ceremony, CancellationToken ct);
    ValueTask<MarriageWeddingLetterResult> InviteAsync(SelectedSessionIdentity actor, byte mode, uint target, CancellationToken ct);
    ValueTask<int> PayAsync(SelectedSessionIdentity actor, uint key, int gold, CancellationToken ct);
    ValueTask<MarriageChurchEntryResult> EnterGuestAsync(SelectedSessionIdentity actor, uint key, uint area, CancellationToken ct);
    ValueTask<IReadOnlyList<uint>> InvitedAsync(Guid ceremony, CancellationToken ct);
    ValueTask<ushort> RemainingAsync(Guid ceremony, CharacterId actor, CancellationToken ct);
}
