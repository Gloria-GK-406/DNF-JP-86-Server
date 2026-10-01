using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// Resolves the static content of a character's entry into answers that need no further
/// combination with progress.
/// </summary>
public interface IWorldContentEntryRuleService
{
    ValueTask<AvailableQuestEntryState> ReadOffersAsync(WorldContentSubject subject, CancellationToken cancellationToken) =>
        ValueTask.FromException<AvailableQuestEntryState>(new NotSupportedException("Bounded quest offer read is unavailable."));
    ValueTask<WorldContentEntryState> LoadAsync(
        WorldContentSubject subject,
        CancellationToken cancellationToken);
}
