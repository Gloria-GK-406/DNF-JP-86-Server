using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public interface IEpicBookDomainService
{
    EpicPieceDefinition? FindPiece(int pieceId);
    ValueTask<EpicPieceAddition> AddPiecesAsync(AccountId account, int pieceId, int count, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<EpicPieceBalance>> GetBalancesAsync(AccountId account, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<EpicBookEntry>> GetBookAsync(AccountId account, CancellationToken cancellationToken);
    /// <summary>Converts legacy inventory custody before players can load their bags.</summary>
    ValueTask InitializeAsync(CancellationToken cancellationToken);
}

public interface IEpicBookRuleService
{
    IReadOnlyList<EpicPieceDefinition> Catalog();
    EpicPieceDefinition? FindPiece(int pieceId);
}

public interface IEpicBookDefinitionSource
{
    IReadOnlyList<EpicPieceDefinition> Load();
}

public interface IEpicBookDomainStore
{
    ValueTask<IReadOnlyList<EpicPieceBalance>> ReadAsync(AccountId account, CancellationToken cancellationToken);
    ValueTask<EpicPieceAddition> AddAsync(AccountId account, int pieceId, int count, CancellationToken cancellationToken);
    ValueTask MigrateLegacyAsync(IReadOnlyList<EpicPieceDefinition> catalog, CancellationToken cancellationToken);
}
