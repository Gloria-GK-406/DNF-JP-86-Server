using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Transactions;

namespace Dfo.Server.Persistence.Database;

/// <summary>One of a character's sections that holds many rows, staged and flushed as a batch.</summary>
/// <remarks>
/// The staging, the overlay a read applies to what the database says, and letting go afterwards
/// are the same for every such section; only the rows and the statements differ.
/// </remarks>
internal abstract class SqliteOwnedCharacterSection<TKey, TValue>(SqliteDatabaseParticipant owner)
    : IStoreSession<OwnedCharacterId, TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly Dictionary<OwnedCharacterId, Dictionary<TKey, TValue?>> staged = [];

    public bool HasStagedWork => this.staged.Count > 0;

    public IEnumerable<OwnedCharacterId> Touched => this.staged.Keys;

    protected SqliteDatabaseParticipant Owner { get; } = owner;

    public async ValueTask<IReadOnlyList<TValue>> ReadAllAsync(
        OwnedCharacterId place, CancellationToken cancellationToken)
    {
        var rows = new SortedDictionary<TKey, TValue>(
            Comparer<TKey>.Default);
        foreach (var value in await this.ReadRowsAsync(place, cancellationToken).ConfigureAwait(false))
        {
            rows[this.KeyOf(value)] = value;
        }

        if (this.staged.TryGetValue(place, out var held))
        {
            foreach (var (key, value) in held)
            {
                if (value is null)
                {
                    rows.Remove(key);
                }
                else
                {
                    rows[key] = value;
                }
            }
        }

        return [.. rows.Values];
    }

    public async ValueTask<TValue?> ReadAsync(
        OwnedCharacterId place, TKey key, CancellationToken cancellationToken)
    {
        if (this.staged.TryGetValue(place, out var held) && held.TryGetValue(key, out var value))
        {
            return value;
        }

        foreach (var row in await this.ReadRowsAsync(place, cancellationToken).ConfigureAwait(false))
        {
            if (this.KeyOf(row).Equals(key))
            {
                return row;
            }
        }

        return null;
    }

    public virtual void Write(OwnedCharacterId place, TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        this.Staged(place)[this.KeyOf(value)] = value;
    }

    public virtual void Clear(OwnedCharacterId place, TKey key) => this.Staged(place)[key] = null;

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        foreach (var (place, rows) in this.staged)
        {
            foreach (var (key, value) in rows)
            {
                if (value is null)
                {
                    await this.EmptyAsync(place, key, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await this.FillAsync(place, value, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    public void Discard() => this.staged.Clear();

    protected abstract TKey KeyOf(TValue value);

    protected abstract ValueTask<IReadOnlyList<TValue>> ReadRowsAsync(
        OwnedCharacterId place, CancellationToken cancellationToken);

    protected abstract ValueTask FillAsync(
        OwnedCharacterId place, TValue value, CancellationToken cancellationToken);

    protected abstract ValueTask EmptyAsync(
        OwnedCharacterId place, TKey key, CancellationToken cancellationToken);

    private Dictionary<TKey, TValue?> Staged(OwnedCharacterId place)
    {
        if (!this.staged.TryGetValue(place, out var rows))
        {
            rows = [];
            this.staged.Add(place, rows);
        }

        return rows;
    }
}
