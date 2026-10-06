using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Initialization;

namespace Dfo.Server.Abstractions.Initialization;

public interface IInitializationPersistence
{
    ValueTask AcquireWriteAsync(CancellationToken ct);
    ValueTask<InitializationStoredDocument?> ReadAsync(string kind, CancellationToken ct);
    ValueTask<bool> SaveAsync(string kind, long expectedRevision, string input, DateTimeOffset at, string actor, CancellationToken ct);
}
public interface IInitializationRuleService : IItemCatalogSelectionRuleService
{
    ValueTask<ItemCatalogDetails> ItemAsync(int itemId, CancellationToken ct);
    ValueTask<InitializationOptions> OptionsAsync(CancellationToken ct);
    ValueTask ValidateAsync(AccountInitialization input, CancellationToken ct);
    ValueTask ValidateAsync(CharacterInitialization input, CancellationToken ct);
}
public interface IInitializationDomainService
{
    ValueTask<InitializationDocument<AccountInitialization>> AccountAsync(CancellationToken ct);
    ValueTask<InitializationDocument<CharacterInitialization>> CharacterAsync(CancellationToken ct);
    ValueTask<InitializationOptions> OptionsAsync(CancellationToken ct);
    ValueTask<InitializationDocument<AccountInitialization>> SaveAsync(SaveInitializationRequest<AccountInitialization> request, string actor, CancellationToken ct);
    ValueTask<InitializationDocument<CharacterInitialization>> SaveAsync(SaveInitializationRequest<CharacterInitialization> request, string actor, CancellationToken ct);
    ValueTask ApplyAccountAsync(AccountId account, CancellationToken ct);
    ValueTask ApplyCharacterAsync(CharacterId character, CancellationToken ct);
}

public sealed class InitializationConflictException() : Exception("配置已被其他管理员修改，请重新读取后编辑。");
