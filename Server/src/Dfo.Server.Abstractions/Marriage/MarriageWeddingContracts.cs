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
    ValueTask<bool> FinishAsync(Guid ceremony, MarriageWeddingPhase phase, DateTimeOffset at, CancellationToken ct);
    ValueTask QueueCleanupAsync(Guid ceremony, MarriageWeddingCleanup cleanup, CancellationToken ct);
    ValueTask<IReadOnlyList<MarriageWeddingCleanup>> CleanupAsync(Guid ceremony, CancellationToken ct);
    ValueTask CompleteCleanupAsync(Guid ceremony, CharacterId character, CancellationToken ct);
}
public interface IMarriageWeddingGuestDomainStore
{
    ValueTask<IReadOnlyList<MarriageWeddingInvitation>> InvitationsAsync(Guid ceremony, CancellationToken ct);
    ValueTask InviteAsync(MarriageWeddingInvitation invitation, CancellationToken ct);
    ValueTask<bool> PayAsync(Guid ceremony, CharacterId guest, int gold, CancellationToken ct);
}
