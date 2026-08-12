using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

/// <summary>One collection box and the items this character has already collected into it.</summary>
public sealed record CollectionBoxProgressSubject(int BoxIndex, IReadOnlyCollection<int> CollectedItemTemplateIds);

/// <summary>
/// The character facts that static content must be resolved against before it becomes an
/// entry answer.
/// </summary>
/// <remarks>
/// This carries progress inward so the content service can produce results rather than
/// catalogs. It deliberately names no storage and no archive concept: it is the question,
/// expressed in domain terms.
/// </remarks>
/// <param name="EquippedCreatureItemId">
/// The equipment identifier sitting in the character's creature slot, or zero when the slot
/// is empty. This is an <em>equipment</em> identifier, not a creature one: the two spaces
/// are disjoint, and reading it as a creature identifier finds nothing at all. Zero is not
/// an error; it is why the welcome is absent rather than empty.
/// </param>
public sealed record WorldContentSubject(
    int Level,
    byte Job,
    byte GrowType,
    IReadOnlyCollection<int> ClearedQuestIds,
    IReadOnlyCollection<int> ClaimedDailyChallengeGroupIds,
    IReadOnlyList<CollectionBoxProgressSubject> CollectionBoxes,
    int EquippedCreatureItemId);

/// <summary>
/// Resolves the static content of a character's entry into answers that need no further
/// combination with progress.
/// </summary>
public interface IWorldContentEntryService
{
    ValueTask<WorldContentEntryState> LoadAsync(
        WorldContentSubject subject,
        CancellationToken cancellationToken);
}
