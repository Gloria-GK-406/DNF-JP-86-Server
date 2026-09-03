using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>
/// A character store held in memory, for tests about what a service decides rather than about
/// what a database does with it.
/// </summary>
/// <remarks>
/// Writes land as soon as they are made rather than when a transaction settles. That keeps these
/// tests about the service under test; the settling itself is proven against the real store.
/// </remarks>
internal sealed class RecordingCharacterDomainStore : ICharacterDomainStore
{
    public Dictionary<(int Character, long Key), StoredCreature> Creatures { get; } = [];

    public Dictionary<int, CharacterPositionState> Positions { get; } = [];

    public bool Holds { get; set; } = true;

    public object StoreIdentity => this;

    public bool IsDurable => true;

    public ICharacterDomainStoreSession SessionIn(IDomainTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return transaction.Enlist(this.StoreIdentity, () => new Session(this));
    }

    private sealed class Session(RecordingCharacterDomainStore store)
        : ICharacterDomainStoreSession, ITransactionParticipant
    {
        public IDomainStoreSession<OwnedCharacterId, long, StoredCreature> Creatures { get; } =
            new CreatureRows(store);

        public bool CommitCanFail => false;

        public ValueTask<bool> HoldsAsync(
            OwnedCharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(store.Holds);

        public ValueTask<CharacterPositionState?> ReadPositionAsync(
            OwnedCharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                store.Positions.TryGetValue(characterId.Character.Value, out var held) ? held : null);

        public void WritePosition(OwnedCharacterId characterId, CharacterPositionState position) =>
            store.Positions[characterId.Character.Value] = position;

        public ValueTask PrepareAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask CommitAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class CreatureRows(RecordingCharacterDomainStore store)
        : IDomainStoreSession<OwnedCharacterId, long, StoredCreature>
    {
        public ValueTask<IReadOnlyList<StoredCreature>> ReadAllAsync(
            OwnedCharacterId place, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<StoredCreature>>(
                [.. store.Creatures.Where(row => row.Key.Character == place.Character.Value)
                    .OrderBy(row => row.Key.Key).Select(row => row.Value)]);

        public ValueTask<StoredCreature?> ReadAsync(
            OwnedCharacterId place, long key, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                store.Creatures.TryGetValue((place.Character.Value, key), out var held) ? held : null);

        public void Write(OwnedCharacterId place, StoredCreature value) =>
            store.Creatures[(place.Character.Value, value.CreatureKey)] =
                new StoredCreature(value.CreatureKey, value.Field04);

        public void Clear(OwnedCharacterId place, long key) =>
            store.Creatures.Remove((place.Character.Value, key));
    }
}

/// <summary>Stands in for the pipeline's ambient transaction, always open.</summary>
internal sealed class OpenTransactionAccessor : IDomainTransactionAccessor
{
    private readonly Dfo.Server.Application.Transactions.DomainTransaction transaction = new();

    public IDomainTransaction? Current => this.transaction;
}
