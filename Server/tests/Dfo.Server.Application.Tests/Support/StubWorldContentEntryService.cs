using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>
/// Stands in for content resolution in tests whose subject is something else.
/// </summary>
/// <remarks>
/// It records the subject it was asked about, so a test that cares can assert the character
/// facts the entry composition passed inward without also asserting content rules.
/// </remarks>
internal sealed class StubWorldContentEntryService(WorldContentEntryState? content = null)
    : IWorldContentEntryService
{
    /// <summary>Content with nothing in it, which is a legitimate answer for a fresh character.</summary>
    public static WorldContentEntryState EmptyContent { get; } = new(
        AvailableQuestEntryState.Empty,
        DailyChallengeEntryState.Empty,
        Array.Empty<CollectionBoxEntryState>(),
        PetWelcome: null);

    public WorldContentSubject? LastSubject { get; private set; }

    public ValueTask<WorldContentEntryState> LoadAsync(
        WorldContentSubject subject,
        CancellationToken cancellationToken)
    {
        LastSubject = subject;
        return ValueTask.FromResult(content ?? EmptyContent);
    }
}

/// <summary>A content service that fails, for proving entry does not publish on failure.</summary>
internal sealed class FailingWorldContentEntryService : IWorldContentEntryService
{
    public ValueTask<WorldContentEntryState> LoadAsync(
        WorldContentSubject subject,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<WorldContentEntryState>(
            new InvalidDataException("world content is unavailable"));
}
