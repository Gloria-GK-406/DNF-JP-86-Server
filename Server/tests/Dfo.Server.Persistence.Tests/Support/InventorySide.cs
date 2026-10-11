using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Characters;
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

    private readonly string databasePath;

    /// <summary>
    /// What the archive says about the items in this database.
    /// </summary>
    /// <remarks>
    /// The load asks it what sort of thing each row holds, because that is what decides whether the
    /// row carries a name. The default says every item is an ordinary consumable, which is what
    /// most tests here are about; a test that needs a thing with a name passes its own.
    /// </remarks>
    private readonly IItemPlacementDefinitionSource placements;

    public InventorySide(string databasePath, IItemPlacementDefinitionSource? placements = null)
    {
        this.databasePath = databasePath;
        this.placements = placements ?? new MainBandPlacements();
        this.Scope = new DomainTransactionScope();
        this.database = new SqliteDatabase(databasePath, this.Scope);
    }

    public DomainTransactionScope Scope { get; }

    /// <summary>
    /// The containers characters are bound to, as entry leaves them.
    /// </summary>
    /// <remarks>
    /// A refill places through the character's bag, and entry binds that bag before it dispatches
    /// the refill. A fixture that skipped the binding would be exercising a state the server never
    /// reaches, so tests call <see cref="BindMain"/> for the character they are about to refill --
    /// which is the same one line entry does.
    /// </remarks>
    public CharacterContainerDomainService Containers { get; } = new();

    public ValueTask<ItemMoveOutcome> MoveAsync(CharacterId characterId,
        BusinessSlot from, BusinessSlot into, int count, CancellationToken cancellationToken)
    {
        var rules = new ItemPlacementRuleService(this.placements, new NoWearables());
        var inventory = new CharacterBusinessInventoryDomainService(this.Containers, rules);
        var source = inventory.GetStore(characterId, BusinessInventoryOwnership.StoreOf(from.Space))
            .GetContainer(from.Space);
        var destination = inventory.GetStore(characterId, BusinessInventoryOwnership.StoreOf(into.Space))
            .GetContainer(into.Space);
        return new ItemMovementDomainService(rules, new ItemSlotRuleService(), new NoWearables())
            .MoveWithinBackingAsync(new(from, source), new(into, destination), count,
                cancellationToken);
    }

    public IItemGrantDomainService Grants => new ItemGrantDomainService(
        new CharacterBusinessInventoryDomainService(Containers,
            new ItemPlacementRuleService(placements, new NoWearables()),
            new ItemArrivalRuleService(), lifecycle: Lifecycle),
        new ItemPlacementRuleService(placements, new NoWearables()), Lifecycle, Store, Scope, new SqliteCharacterTable(database), null!, null!, new InventoryCapacityDomainService(new SqliteCharacterContainerStateTable(database), new SqliteAccountCargoStateTable(database), new InventoryCapacityRuleService()));

    public async ValueTask<IReadOnlyList<InventoryItemState>> ReadMainItemsAsync(
        AccountId account, CharacterId character, CancellationToken ct)
    {
        var source = Scope.Current is null ? (ICharacterInventoryEntryRepository)PerCall : CharacterEntries;
        var inventory = await new CharacterInventoryEntryDomainService(source).ReadCurrentAsync(account, character, ct);
        return inventory.Containers.Single(container => container.ContainerType == InventoryContainerType.Main).Items;
    }

    public ICharacterInventoryEntryRepository CharacterEntries => this.Assembly;

    public IAccountInventoryEntryRepository AccountEntries => this.Assembly;

    public IItemDomainStore Store => new SqliteItemDomainStore(this.databasePath);

    public IItemLifecycleDomainService Lifecycle => new ItemLifecycleDomainService(
        Store, Scope, new ItemPlacementRuleService(this.placements, new NoWearables()));

    public IDailyRefillDomainService Mutations => new DailyRefillDomainService(
        new SqliteCharacterTable(this.database),
        new SqliteCharacterDailyCounterTable(this.database),
        new DailyRefillRuleService(),
        new CharacterBusinessInventoryDomainService(this.Containers,
            new ItemPlacementRuleService(this.placements, new NoWearables()),
            new ItemArrivalRuleService(), lifecycle: Lifecycle),
        new BagRewardDelivery(Grants, Containers));

    /// <summary>
    /// Binds a character to its own main container, as entry does.
    /// </summary>
    /// <remarks>
    /// Placement rules also validate lifecycle creation. The default treats every item as an ordinary
    /// consumable of the main band, which is what the tests that move things here are about; a test
    /// with something else in mind passes its own.
    /// </remarks>
    public void BindMain(
        int characterId,
        ushort parameter = 0,
        IItemPlacementDefinitionSource? placements = null) =>
        this.BindList(InventoryContainerType.Main, characterId, parameter, placements);

    /// <summary>Binds one of the character's lists to a container that really keeps its rows.</summary>
    /// <remarks>
    /// The main list was the only one worth binding while it was the only one served. A second
    /// served list needs the same thing for the same reason: what a container decides about a slot
    /// depends on which list it is, and a fake standing in for it decides nothing.
    /// </remarks>
    public void BindList(
        InventoryContainerType containerType,
        int characterId,
        ushort parameter = 0,
        IItemPlacementDefinitionSource? placements = null)
    {
        var character = new CharacterId(characterId);
        this.Containers.Register(character, new PersistentItemContainer(
            ContainerId.ForCharacter(character, containerType),
            new ContainerShape(containerType, parameter),
            new SqliteItemDomainStore(this.databasePath),
            this.Scope,
            new ItemPlacementRuleService(
                placements ?? this.placements, new NoWearables()),
            new ItemSlotRuleService()));
    }

    /// <summary>Every item an ordinary consumable: kind 2, the main band, piles of a hundred.</summary>
    private sealed class MainBandPlacements : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(new ItemPlacement(2, 100,
                new(BusinessInventorySpace.Consumable, 0, 55)));
    }

    /// <summary>Nothing here is worn; no test reached through this binding puts a piece on.</summary>
    private sealed class NoWearables : IEquipmentSlotDefinitionSource, IEquipmentWearDomainService
    {
        public Dfo.Server.Data.Inventory.ItemSnapshot UnsealForEquip(Dfo.Server.Data.Inventory.ItemSnapshot item) => item;
        public ValueTask<BusinessSlot?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<BusinessSlot?>(null);

        public ValueTask<bool> CanWearAsync(CharacterId characterId, int itemId,
            CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }

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
    public IDailyRefillDomainService MutationsPerCall =>
        new PerCallMutations(this.Scope, this.Mutations);

    internal sealed class PerCallUseCase(DomainTransactionScope scope, InventoryEntryAssembly inner)
        : ICharacterInventoryEntryRepository, IAccountInventoryEntryRepository
    {
        public ValueTask<CharacterInventoryEntryStorageRecord> LoadCharacterAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) =>
            Settled(scope, () => inner.LoadCharacterAsync(accountId, characterId, cancellationToken),
                cancellationToken);

        public ValueTask<AccountInventoryEntryStorageRecord> LoadAccountAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) =>
            Settled(scope, () => inner.LoadAccountAsync(accountId, characterId, cancellationToken),
                cancellationToken);

    }

    private sealed class PerCallMutations(
        DomainTransactionScope scope, IDailyRefillDomainService inner)
        : IDailyRefillDomainService
    {
        public ValueTask<DailyRefillMutationResult> ApplyDailyRefillAsync(
            AccountId accountId,
            CharacterId characterId,
            IReadOnlyList<DailyRefillRule> rules,
            DateTimeOffset preparedAt,
            CancellationToken cancellationToken) =>
            Settled(scope, () => inner.ApplyDailyRefillAsync(
                accountId, characterId, rules, preparedAt, cancellationToken), cancellationToken);

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
        new CharacterBusinessInventoryDomainService(new CharacterContainerDomainService(),
                new ItemPlacementRuleService(this.placements, new NoWearables()), new ItemArrivalRuleService(),
                items: new SqliteItemDomainStore(this.databasePath), transactions: this.Scope, slotRules: new ItemSlotRuleService()),
        new SqliteCharacterItemTable(this.database),
        new SqliteAccountCargoItemTable(this.database),
        new SqliteCharacterContainerStateTable(this.database),
        new SqliteAccountCargoStateTable(this.database),
        new SqliteCharacterGoldLimitTable(this.database),
        new SqliteCharacterCarriedGoldTable(this.database),
        new SqliteCharacterItemCooldownTable(this.database),
        new SqliteCharacterItemEffectTable(this.database),
        new SqliteCharacterItemLockTable(this.database),
        new SqliteCharacterRentalTable(this.database),
        Store, Scope);
}
