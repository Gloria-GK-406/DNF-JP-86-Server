using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public interface IEpicBookDomainService
{
    EpicPieceDefinition? FindPiece(int pieceId);
    ValueTask<EpicPieceAddition> AddPiecesAsync(AccountId account, int pieceId, int count, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<EpicPieceBalance>> GetBalancesAsync(AccountId account, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<EpicBookEntry>> GetBookAsync(AccountId account, CancellationToken cancellationToken);
    ValueTask<EpicPieceBalance?> TrySpendPiecesAsync(AccountId account, int pieceId, int count, CancellationToken cancellationToken);
    /// <summary>Converts legacy inventory custody before players can load their bags.</summary>
    ValueTask InitializeAsync(CancellationToken cancellationToken);
}

public interface IEpicBookRuleService
{
    IReadOnlyList<EpicPieceDefinition> Catalog();
    EpicPieceDefinition? FindPiece(int pieceId);
    EpicCraftRecipe? FindRecipe(int equipmentId);
}

public interface IEpicBookDefinitionSource
{
    IReadOnlyList<EpicPieceDefinition> Load();
    IReadOnlyList<EpicCraftRecipe> LoadRecipes() => [];
}

public interface IEpicBookDomainStore
{
    ValueTask<IReadOnlyList<EpicPieceBalance>> ReadAsync(AccountId account, CancellationToken cancellationToken);
    ValueTask<EpicPieceAddition> AddAsync(AccountId account, int pieceId, int count, CancellationToken cancellationToken);
    ValueTask<EpicPieceBalance?> TrySpendAsync(AccountId account, int pieceId, int count, CancellationToken cancellationToken);
    ValueTask MigrateLegacyAsync(IReadOnlyList<EpicPieceDefinition> catalog, CancellationToken cancellationToken);
}
