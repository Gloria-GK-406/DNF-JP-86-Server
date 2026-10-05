using System.Text.Json;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Initialization;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Initialization;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Initialization;

internal sealed class InitializationDomainService(IInitializationPersistence persistence, Func<IInitializationRuleService> rules,
    Func<InitializationAssetGrants> grants, IDomainTransactionAccessor transactions, TimeProvider clock) : IInitializationDomainService
{
    public ValueTask<InitializationDocument<AccountInitialization>> AccountAsync(CancellationToken ct) => ReadAsync("account", InitializationRuleService.DefaultAccount(), ct);
    public ValueTask<InitializationDocument<CharacterInitialization>> CharacterAsync(CancellationToken ct) => ReadAsync("character", InitializationRuleService.DefaultCharacter(), ct);
    public ValueTask<InitializationOptions> OptionsAsync(CancellationToken ct) => rules().OptionsAsync(ct);
    private async ValueTask<InitializationDocument<T>> ReadAsync<T>(string kind, T fallback, CancellationToken ct)
    {
        var row = await persistence.ReadAsync(kind, ct);
        return row is null ? new(0, fallback) : new(row.Revision, JsonSerializer.Deserialize<T>(row.Input)
            ?? throw new InvalidDataException("Invalid initialization settings."), row.UpdatedAt, row.UpdatedBy);
    }
    public async ValueTask<InitializationDocument<AccountInitialization>> SaveAsync(SaveInitializationRequest<AccountInitialization> request, string actor, CancellationToken ct)
    {
        RequireTransaction();
        await rules().ValidateAsync(request.Input, ct);
        return await SaveAsync("account", request.ExpectedRevision, request.Input with { Contracts = [.. request.Input.Contracts.OrderBy(c => c.Type)] }, actor, ct);
    }
    public async ValueTask<InitializationDocument<CharacterInitialization>> SaveAsync(SaveInitializationRequest<CharacterInitialization> request, string actor, CancellationToken ct)
    {
        RequireTransaction();
        await rules().ValidateAsync(request.Input, ct);
        return await SaveAsync("character", request.ExpectedRevision, request.Input with { Items = [.. request.Input.Items] }, actor, ct);
    }
    private async ValueTask<InitializationDocument<T>> SaveAsync<T>(string kind, long revision, T input, string actor, CancellationToken ct)
    {
        if (revision < 0) throw new ArgumentException("配置版本无效。");
        await persistence.AcquireWriteAsync(ct);
        var at = clock.GetUtcNow();
        if (!await persistence.SaveAsync(kind, revision, JsonSerializer.Serialize(input), at, actor, ct))
            throw new InitializationConflictException();
        return new(checked(revision + 1), input, at, actor);
    }
    public async ValueTask ApplyAccountAsync(AccountId account, CancellationToken ct)
    {
        RequireTransaction();
        var input = (await AccountAsync(ct)).Input;
        if (input.Cera != 0 || input.TokenCera != 0 || input.HappyTokenCera != 0 || input.Contracts.Any(c => c.Days > 0) || input.AccountCargoLevel != 0)
            await grants().AccountAsync(account, input, ct);
    }
    public async ValueTask ApplyCharacterAsync(CharacterId character, CancellationToken ct)
    {
        RequireTransaction();
        var input = (await CharacterAsync(ct)).Input;
        if (input.Level != 1 || input.PersonalCargoLevel != 8 || input.Items.Length != 0) await grants().CharacterAsync(character, input, ct);
    }
    private void RequireTransaction()
    { if (transactions.Current is null) throw new InvalidOperationException("Initialization requires a domain transaction."); }
}
