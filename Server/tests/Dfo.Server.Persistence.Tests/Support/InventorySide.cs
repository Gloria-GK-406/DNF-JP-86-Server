using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Accounts;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;

namespace Dfo.Server.Persistence.Tests.Support;

/// <summary>
/// The inventory side of one database, put together the way the server puts it together.
/// </summary>
/// <remarks>
/// Nine tables go into the entry loads and five into a refill, and naming all of them at every
/// call site would bury what each test is about. This is the same wiring the composition root does,
/// in one place, so a test says which database and gets an inventory.
///
/// It is compiled into more than one test assembly by reference from their project files rather
/// than copied, because three of them reach this seam and three copies would drift.
/// </remarks>
internal sealed class InventorySide
{
    private readonly SqliteDatabase database;

    public InventorySide(string databasePath)
    {
        this.Scope = new DomainTransactionScope();
        this.database = new SqliteDatabase(databasePath, this.Scope);
    }

    public DomainTransactionScope Scope { get; }

    public ICharacterInventoryEntryRepository CharacterEntries => this.Assembly;

    public IAccountInventoryEntryRepository AccountEntries => this.Assembly;

    public IEntryInventoryLeasePersistence Leases => this.Assembly;

    public IEntryInventoryMutationRepository Mutations => new DailyRefillService(
        new SqliteCharacterTable(this.database),
        new SqliteCharacterItemTable(this.database),
        new SqliteCharacterDailyCounterTable(this.database),
        new DailyRefillRuleService(),
        new ItemArrivalRuleService());

    /// <summary>
    /// The inventory with a use case around every call, which is how the server dispatches these.
    /// </summary>
    /// <remarks>
    /// Loading and saving are separate commands, so a test that does several in a row is doing
    /// several use cases. A call that throws is never committed, and the lease takes back whatever
    /// it had done -- which is what the rollback these tests assert actually is.
    /// </remarks>
    public PerCallUseCase PerCall => new(this.Scope, this.Assembly);

    /// <summary>The refill, likewise one use case per call.</summary>
    public IEntryInventoryMutationRepository MutationsPerCall =>
        new PerCallMutations(this.Scope, this.Mutations);

    internal sealed class PerCallUseCase(DomainTransactionScope scope, InventoryEntryAssembly inner)
        : ICharacterInventoryEntryRepository, IAccountInventoryEntryRepository,
            IEntryInventoryLeasePersistence
    {
        public ValueTask<CharacterInventoryEntryStorageRecord> LoadCharacterAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) =>
            Settled(scope, () => inner.LoadCharacterAsync(accountId, characterId, cancellationToken),
                cancellationToken);

        public ValueTask<AccountInventoryEntryStorageRecord> LoadAccountAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) =>
            Settled(scope, () => inner.LoadAccountAsync(accountId, characterId, cancellationToken),
                cancellationToken);

        public async ValueTask SaveAsync(
            EntryInventoryLeaseSnapshot lease, CancellationToken cancellationToken) =>
            await Settled(scope, async () =>
            {
                await inner.SaveAsync(lease, cancellationToken).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
    }

    private sealed class PerCallMutations(
        DomainTransactionScope scope, IEntryInventoryMutationRepository inner)
        : IEntryInventoryMutationRepository
    {
        public ValueTask<DailyRefillMutationResult> ApplyDailyRefillAsync(
            AccountId accountId,
            CharacterId characterId,
            IReadOnlyList<DailyRefillRule> rules,
            DateTimeOffset preparedAt,
            CancellationToken cancellationToken) =>
            Settled(scope, () => inner.ApplyDailyRefillAsync(
                accountId, characterId, rules, preparedAt, cancellationToken), cancellationToken);

        public ValueTask<IReadOnlyList<InventoryItemState>> ReadMainItemsAsync(
            CharacterId characterId, CancellationToken cancellationToken) =>
            Settled(scope, () => inner.ReadMainItemsAsync(characterId, cancellationToken),
                cancellationToken);
    }

    private static async ValueTask<TResult> Settled<TResult>(
        DomainTransactionScope scope,
        Func<ValueTask<TResult>> work,
        CancellationToken cancellationToken)
    {
        await using var lease = scope.Begin();
        var result = await work().ConfigureAwait(false);
        await lease.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Runs one call inside a use case and settles it, the way one command would.</summary>
    public async Task<TResult> InUseCaseAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> work)
    {
        await using var lease = this.Scope.Begin();
        var result = await work(CancellationToken.None).ConfigureAwait(false);
        await lease.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return result;
    }

    /// <summary>The same, for work that answers with nothing.</summary>
    public async Task InUseCaseAsync(Func<CancellationToken, ValueTask> work)
    {
        await using var lease = this.Scope.Begin();
        await work(CancellationToken.None).ConfigureAwait(false);
        await lease.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private InventoryEntryAssembly Assembly => new(
        new SqliteCharacterTable(this.database),
        new SqliteAccountTable(this.database),
        new SqliteCharacterItemTable(this.database),
        new SqliteAccountCargoItemTable(this.database),
        new SqliteCharacterContainerStateTable(this.database),
        new SqliteAccountCargoStateTable(this.database),
        new SqliteCharacterGoldLimitTable(this.database),
        new SqliteCharacterItemCooldownTable(this.database),
        new SqliteCharacterItemEffectTable(this.database),
        new SqliteCharacterItemLockTable(this.database),
        new SqliteCharacterRentalTable(this.database));
}
