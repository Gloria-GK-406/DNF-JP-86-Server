using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// Work a run kind owns when the kill that clears its boss room settles.
/// </summary>
/// <remarks>
/// Runs inside the authoritative, multi-account actor-death transaction, so a kind's clear reward
/// commits or rolls back with the kill itself. <see cref="DungeonMapCleared"/> subscribers run in
/// a later job and cannot give that guarantee. At most one hook exists per kind.
/// </remarks>
public interface IDungeonBossClearHook
{
    DungeonRunKind Kind { get; }

    ValueTask CompleteClearAsync(DungeonActorDeathClaim death,
        IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken ct);
}
