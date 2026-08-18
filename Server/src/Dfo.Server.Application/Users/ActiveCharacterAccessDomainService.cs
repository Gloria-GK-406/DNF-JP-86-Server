using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Application.Users;

/// <summary>
/// Mirrors of the active characters, and exclusive write access over a set of them.
/// </summary>
/// <remarks>
/// A fourth facade over the one store, alongside the account, online-user and active-character
/// services, for the reason the store already states: these are readings of a single invariant and
/// giving them separate state would let them disagree.
///
/// Two different exclusions are in play and they are not interchangeable. The store's own lock is
/// synchronous and is held for the length of a dictionary operation; it keeps the collections
/// consistent with each other. The leases taken here are asynchronous and are held for the length
/// of a business transition, across whatever awaits it contains. A transition cannot hold the
/// store's lock -- it would have to be released at the first await -- so the two exist together
/// rather than one replacing the other.
/// </remarks>
internal sealed class ActiveCharacterAccessDomainService(
    OnlineStateStore state,
    UserExecutionGate<CharacterId> gate,
    TimeSpan acquisitionTimeout) : IActiveCharacterAccessDomainService
{
    public ActiveCharacterState? Read(CharacterId characterId) => state.ReadCharacter(characterId);

    public IReadOnlyDictionary<CharacterId, ActiveCharacterState> Read(
        IReadOnlyCollection<CharacterId> characterIds) => state.ReadCharacters(characterIds);

    public async ValueTask<IActiveCharacterWriteScope> AcquireAsync(
        IReadOnlyCollection<CharacterId> characterIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(characterIds);
        if (characterIds.Count == 0)
        {
            throw new ArgumentException(
                "A write scope must name at least one character.", nameof(characterIds));
        }

        // One fixed order, chosen here rather than asked of the caller. Two transitions naming the
        // same characters in opposite orders is the ordinary way to deadlock a multi-key lock, and
        // a convention the caller must remember is a convention that will eventually be forgotten.
        // De-duplication belongs here for the same reason: this gate is not reentrant, so a set
        // that repeats a character would otherwise wait for itself.
        var ordered = characterIds.Distinct().OrderBy(value => value.Value).ToArray();

        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(acquisitionTimeout);
        var leases = new List<IAsyncDisposable>(ordered.Length);
        var granted = false;
        try
        {
            foreach (var characterId in ordered)
            {
                try
                {
                    leases.Add(await gate
                        .AcquireAsync(characterId, UserAccessMode.Exclusive, attempt.Token)
                        .ConfigureAwait(false));
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Deliberately an exception rather than an unbounded wait. The gate queues
                    // waiters, so a transition that asks for a character it already holds would
                    // otherwise wait for itself forever, and a hang gives an investigator nothing
                    // to read. Refusing and letting the client ask again is the outcome this
                    // server prefers over proceeding with an uncertain hold.
                    throw new TimeoutException(
                        $"Exclusive access to character '{characterId}' was not granted within "
                        + $"{acquisitionTimeout}. Requested: "
                        + $"{string.Join(", ", ordered.Select(value => value.Value))}; "
                        + $"already held: {leases.Count}.");
                }
            }

            var current = state.ReadCharacters(ordered);
            foreach (var characterId in ordered)
            {
                if (!current.ContainsKey(characterId))
                {
                    throw new KeyNotFoundException(
                        $"Character '{characterId}' is not active, so it cannot be written. A write "
                        + "scope covers characters that are online; an absent one is a refusal.");
                }
            }

            var scope = new ActiveCharacterWriteScope(state, ordered, current, leases);
            granted = true;
            return scope;
        }
        finally
        {
            if (!granted)
            {
                for (var index = leases.Count - 1; index >= 0; index--)
                    await leases[index].DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class ActiveCharacterWriteScope : IActiveCharacterWriteScope
    {
        private readonly OnlineStateStore state;
        private readonly IReadOnlyList<IAsyncDisposable> leases;
        private readonly Dictionary<CharacterId, ActiveCharacterState> staged;
        private bool completed;
        private bool disposed;

        public ActiveCharacterWriteScope(
            OnlineStateStore state,
            IReadOnlyList<CharacterId> held,
            IReadOnlyDictionary<CharacterId, ActiveCharacterState> current,
            IReadOnlyList<IAsyncDisposable> leases)
        {
            this.state = state;
            this.leases = leases;
            Held = held;
            staged = new Dictionary<CharacterId, ActiveCharacterState>(current);
        }

        public IReadOnlyList<CharacterId> Held { get; }

        public ActiveCharacterState this[CharacterId characterId]
        {
            get
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return staged.TryGetValue(characterId, out var character)
                    ? character
                    : throw NotHeld(characterId);
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);
                ObjectDisposedException.ThrowIf(disposed, this);
                if (completed)
                {
                    throw new InvalidOperationException(
                        "This write scope has been applied and cannot stage further changes.");
                }

                if (!staged.ContainsKey(characterId))
                    throw NotHeld(characterId);
                if (value.Identity.CharacterId != characterId)
                {
                    throw new InvalidDataException(
                        $"A write scope slot for character '{characterId}' cannot be given the state "
                        + $"of character '{value.Identity.CharacterId}'.");
                }

                staged[characterId] = value;
            }
        }

        public void Complete()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (completed)
                throw new InvalidOperationException("This write scope has already been applied.");
            if (!state.TryApplyCharacters(staged, out var rejected))
            {
                throw new InvalidOperationException(
                    $"Character '{rejected}' is no longer the active character for its account, so "
                    + "no part of this scope was applied.");
            }

            completed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed)
                return;
            disposed = true;
            // Released in reverse, and released whether or not the transition succeeded. Disposal
            // returns the leases; it never applies anything -- see IActiveCharacterWriteScope.
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
        }

        private static KeyNotFoundException NotHeld(CharacterId characterId) =>
            new($"Character '{characterId}' is not held by this write scope.");
    }
}
