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
internal sealed class RecordingCharacterStore : ICharacterStore
{
    public Dictionary<(int Character, short Slot), StoredActiveQuest> Quests { get; } = [];

    public Dictionary<(int Character, long Key), StoredCreature> Creatures { get; } = [];

    public Dictionary<int, CharacterPositionState> Positions { get; } = [];

    public List<StoredActiveQuest> QuestWrites { get; } = [];

    public bool Holds { get; set; } = true;

    public object StoreIdentity => this;

    public bool IsDurable => true;

    public ICharacterStoreSession SessionIn(IDomainTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return transaction.Enlist(this.StoreIdentity, () => new Session(this));
    }

    private sealed class Session(RecordingCharacterStore store)
        : ICharacterStoreSession, ITransactionParticipant
    {
        public IStoreSession<OwnedCharacterId, short, StoredActiveQuest> Quests { get; } =
            new QuestRows(store);

        public IStoreSession<OwnedCharacterId, long, StoredCreature> Creatures { get; } =
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

    private sealed class QuestRows(RecordingCharacterStore store)
        : IStoreSession<OwnedCharacterId, short, StoredActiveQuest>
    {
        public ValueTask<IReadOnlyList<StoredActiveQuest>> ReadAllAsync(
            OwnedCharacterId place, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<StoredActiveQuest>>(
                [.. store.Quests.Where(row => row.Key.Character == place.Character.Value)
                    .OrderBy(row => row.Key.Slot).Select(row => row.Value)]);

        public ValueTask<StoredActiveQuest?> ReadAsync(
            OwnedCharacterId place, short key, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                store.Quests.TryGetValue((place.Character.Value, key), out var held) ? held : null);

        public void Write(OwnedCharacterId place, StoredActiveQuest value)
        {
            store.Quests[(place.Character.Value, value.Slot)] = value;
            store.QuestWrites.Add(value);
        }

        public void Clear(OwnedCharacterId place, short key) =>
            store.Quests.Remove((place.Character.Value, key));
    }

    private sealed class CreatureRows(RecordingCharacterStore store)
        : IStoreSession<OwnedCharacterId, long, StoredCreature>
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
