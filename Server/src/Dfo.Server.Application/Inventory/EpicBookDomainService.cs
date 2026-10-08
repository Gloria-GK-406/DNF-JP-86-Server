using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EpicBookRuleService(IEpicBookDefinitionSource source) : IEpicBookRuleService
{
    private readonly Lazy<IReadOnlyList<EpicPieceDefinition>> catalog = new(() =>
    {
        var entries = source.Load().ToArray();
        if (entries.Any(x => x.PieceId <= 0 || x.EquipmentId <= 0)
            || entries.Select(x => x.PieceId).Distinct().Count() != entries.Length
            || entries.Select(x => x.EquipmentId).Distinct().Count() != entries.Length)
            throw new InvalidDataException("Invalid epic book catalog.");
        return Array.AsReadOnly(entries);
    });
    public IReadOnlyList<EpicPieceDefinition> Catalog() => catalog.Value;
    public EpicPieceDefinition? FindPiece(int pieceId) => Catalog().FirstOrDefault(x => x.PieceId == pieceId);
}

internal sealed class EpicBookDomainService(IEpicBookRuleService rules, IEpicBookDomainStore store,
    DomainTransactionScope transactions) : IEpicBookDomainService
{
    public EpicPieceDefinition? FindPiece(int pieceId) => rules.FindPiece(pieceId);
    public ValueTask<EpicPieceAddition> AddPiecesAsync(AccountId account, int pieceId, int count, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(account.Value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (FindPiece(pieceId) is null) throw new ArgumentException("Unknown epic piece.", nameof(pieceId));
        return store.AddAsync(account, pieceId, count, cancellationToken);
    }
    public ValueTask<IReadOnlyList<EpicPieceBalance>> GetBalancesAsync(AccountId account, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(account.Value);
        return store.ReadAsync(account, cancellationToken);
    }
    public async ValueTask<IReadOnlyList<EpicBookEntry>> GetBookAsync(AccountId account, CancellationToken cancellationToken)
    {
        var balances = (await GetBalancesAsync(account, cancellationToken).ConfigureAwait(false)).ToDictionary(x => x.PieceId, x => x.Count);
        return rules.Catalog().Select(x => new EpicBookEntry(x, balances.GetValueOrDefault(x.PieceId))).ToArray();
    }
    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        var catalog = rules.Catalog();
        await using var transaction = transactions.Begin();
        await store.MigrateLegacyAsync(catalog, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
