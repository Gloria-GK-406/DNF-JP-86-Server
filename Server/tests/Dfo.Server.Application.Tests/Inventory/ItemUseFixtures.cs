using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Tests.Support;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Protocol.Game.TaggedIdReport;
using Dfo.Server.Protocol.Game.UseStackable;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>What the archive would say about a handful of items.</summary>
/// <remarks>
/// An identifier absent from the table is an identifier the archive lists nothing usable under --
/// which is how equipment reaches the use path and how it is turned away.
/// </remarks>
internal sealed class FakeItemUseRules(params (int ItemId, ItemUseRule Rule)[] rules)
    : IItemUseRuleDefinitionSource
{
    private readonly Dictionary<int, ItemUseRule> declared =
        rules.ToDictionary(entry => entry.ItemId, entry => entry.Rule);

    public ValueTask<ItemUseRule?> GetAsync(int itemId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(declared.GetValueOrDefault(itemId));
}

/// <summary>The shapes the archive actually produces, named once.</summary>
internal static class ItemUseRules
{
    /// <summary>A potion: consumed, ten seconds, no shared group, no restriction.</summary>
    public static ItemUseRule Potion(int cooldownMilliseconds = 10_000, int cooltimeGroup = 0) =>
        new(
            IsConsumedOnUse: true,
            CooldownMilliseconds: cooldownMilliseconds,
            CooltimeGroup: cooltimeGroup,
            MinimumLevel: 1,
            Jobs: ItemJobRestriction.Any,
            Places: ItemPlaceRestriction.Anywhere);

    /// <summary>One of the eighty-four whose <c>[stackable type]</c> begins with unlimited.</summary>
    public static ItemUseRule Unlimited(int cooldownMilliseconds = 0, int cooltimeGroup = 0) =>
        Potion(cooldownMilliseconds, cooltimeGroup) with { IsConsumedOnUse = false };

    public static ItemUseRule RequiringLevel(int minimumLevel) =>
        Potion(cooldownMilliseconds: 0) with { MinimumLevel = minimumLevel };

    public static ItemUseRule RequiringJob(params QuestJobRequirement[] families) =>
        Potion(cooldownMilliseconds: 0) with
        {
            Jobs = new ItemJobRestriction(IsExactAll: false, families),
        };

    public static ItemUseRule UsableIn(params ItemUsePlace[] places) =>
        Potion(cooldownMilliseconds: 0) with
        {
            Places = new ItemPlaceRestriction(IsUnrestricted: false, places),
        };
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class AdvanceableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan amount) => now += amount;
}

/// <summary>
/// Runs a command on the handler that owns it, with a use case boundary and nothing else.
/// </summary>
/// <remarks>
/// The pipeline's other behaviours -- user access scoping, logging -- are the bus's own subject and
/// are covered where the bus is. The boundary is here because both of these commands are
/// transactional now: a container that keeps its items refuses a change with no boundary around it,
/// so a bus that opened none would be testing the handler against a container it can never meet.
/// </remarks>
internal sealed class DirectCommandBus(
    ICommandHandler<UseStackableCommand, ItemUseResult>? use = null,
    ICommandHandler<MoveItemSpaceCommand, ItemMoveResult>? move = null,
    DomainTransactionScope? scope = null,
    ICommandHandler<GrantTutorialRewardCommand, TutorialRewardResult>? reward = null) : ICommandBus
{
    private readonly DomainTransactionScope scope = scope ?? new DomainTransactionScope();

    public async ValueTask<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default)
        where TResult : notnull
    {
        var context = new CommandExecutionContext(
            Guid.Empty, command.GetType(), typeof(DirectCommandBus),
            CommandExecutionKind.Transactional);

        await using var transaction = this.scope.Begin();
        object dispatched = command switch
        {
            UseStackableCommand value => await use!
                .HandleAsync(context, value, cancellationToken).ConfigureAwait(false),
            MoveItemSpaceCommand value => await move!
                .HandleAsync(context, value, cancellationToken).ConfigureAwait(false),
            GrantTutorialRewardCommand value => await reward!
                .HandleAsync(context, value, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"No handler registered for {command.GetType()}."),
        };

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (TResult)dispatched;
    }
}

/// <summary>
/// One character's main container, over a store that keeps nothing past the test.
/// </summary>
/// <remarks>
/// The container is the persistent one, which is the point: it is the one that refuses a change
/// made outside a use case boundary, so wiring it here is what keeps these tests honest about the
/// boundary the real path has. What sits behind it does not have to be a database for that.
/// </remarks>
internal abstract class FakeMainInventory(int characterId = 60)
{
    private readonly CharacterId character = new(characterId);

    private readonly VolatileItemDomainStore store = new();

    private ContainerId Main => ContainerId.ForCharacter(this.character, InventoryContainerType.Main);

    public DomainTransactionScope Scope { get; init; } = new();
    public IItemLifecycleDomainService Lifecycle => LifecycleFor(new ConsumableBandPlacements());
    public IItemLifecycleDomainService LifecycleFor(IItemPlacementDefinitionSource placements) =>
        new ItemLifecycleDomainService(store, Scope, new ItemPlacementRuleService(placements, new NoWearables()));

    /// <remarks>
    /// The archive is a parameter because the bag now reads it: what an item's band is, and how
    /// large a pile of it may grow, are questions the bag asks before it places anything. A fixture
    /// that fixed one answer here would make every test that needs a different band build its own
    /// container instead.
    /// </remarks>
    public ICharacterContainerDomainService Containers(
        IItemPlacementDefinitionSource? placements = null)
    {
        var containers = new CharacterContainerDomainService();
        containers.Register(this.character, new PersistentItemContainer(
            this.Main,
            new ContainerShape(InventoryContainerType.Main, 0),
            this.store,
            this.Scope,
            new ItemPlacementRuleService(
                placements ?? new ConsumableBandPlacements(), new NoWearables()),
            new ItemSlotRuleService()));
        return containers;
    }

    public IItemGrantDomainService Grants(IItemPlacementDefinitionSource placements) =>
        new Dfo.Server.Application.Tests.Support.OrdinaryItemGrantDomainService(
            new ItemPlacementRuleService(placements, new NoWearables()), LifecycleFor(placements));

    /// <summary>Every item an ordinary consumable: kind 2, band 65 to 120, piles of a hundred.</summary>
    private sealed class ConsumableBandPlacements : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(new ItemPlacement(2, 100,
                new(BusinessInventorySpace.Consumable, 0, 55)));
    }

    /// <summary>Nothing here is worn; these fixtures use items rather than put them on.</summary>
    private sealed class NoWearables : IEquipmentSlotDefinitionSource
    {
        public ValueTask<BusinessSlot?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<BusinessSlot?>(null);
    }

    /// <summary>What the container holds of one item, in slot order.</summary>
    public List<(int Slot, int ItemId, int Count)> PilesOf(int itemId) =>
        [.. this.Rows().Where(row => row.ItemId == itemId)];

    public void Seed(int slotIndex, int itemId, int count, byte itemKind = 2) =>
        this.Settle(async session =>
        {
            if (await session.GetAsync(this.Main, slotIndex, default) is { } old)
                await session.DeleteItemAsync(await session.TakeAsync(old, null, default), default);
            var snapshot = ItemSnapshot.FromCore(StackableItemCore.Create(itemKind, itemId, count),
                isPile: itemKind is not (1 or 5 or 6 or 8));
            await session.PutAsync(this.Main, slotIndex, await session.CreateAsync(snapshot, default), default);
        });

    public List<(int Slot, int ItemId, int Count)> Rows()
    {
        var alone = new DomainTransaction();
        try
        {
            var items = this.store.SessionIn(alone).GetAllAsync(this.Main, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
            return [.. items.OrderBy(item => item.SlotIndex)
                .Select(item => (item.SlotIndex, item.ItemId, item.Count))];
        }
        finally { alone.RollbackAsync().AsTask().GetAwaiter().GetResult(); }
    }

    public int CountIn(int slotIndex) =>
        this.Rows().FirstOrDefault(row => row.Slot == slotIndex).Count;

    /// <remarks>
    /// The memory store settles without waiting for anything, so nothing here can block; this is
    /// what lets a fixture seed itself from a property initialiser.
    /// </remarks>
    private void Settle(Func<IItemDomainStoreSession, ValueTask> write)
    {
        var alone = new DomainTransaction();
        write(this.store.SessionIn(alone)).AsTask().GetAwaiter().GetResult();
        var settling = alone.CommitAsync(CancellationToken.None);
        if (!settling.IsCompleted)
            settling.AsTask().GetAwaiter().GetResult();
    }
}

/// <summary>
/// A character repository that knows exactly one character and answers nothing else.
/// </summary>
/// <remarks>
/// The handler under test asks the repository for the character and then asks the character. What
/// the rest of the repository does -- rosters, creation, deletion -- is its own subject and is
/// covered where it lives, so a fixture that filled those in would hide the day one of them starts
/// being read here.
/// </remarks>
internal sealed class OneCharacter(ICharacter character) : ICharacterDomainService
{
    public ICharacter Get(CharacterId characterId) => character;

    public ValueTask<AccountCharacterRoster> GetAccountRosterAsync(
        AccountId accountId, CancellationToken cancellationToken) => throw Unused();

    public ValueTask<AccountCharacterRoster> GetAccountRosterWithAppearanceAsync(
        AccountId accountId, CancellationToken cancellationToken) => throw Unused();

    public ValueTask<ActiveCharacterState> LoadForSelectionAsync(
        AccountId accountId, ushort slotIndex, CancellationToken cancellationToken) => throw Unused();

    public ValueTask<ActiveCharacterState> LoadByRosterPositionAsync(
        AccountId accountId, ushort rosterIndex, CancellationToken cancellationToken) => throw Unused();

    public ValueTask<CharacterNameStatus> CheckNameAsync(
        byte[] rawName, CancellationToken cancellationToken) => throw Unused();

    public ValueTask<CharacterCreationResult> CreateCharacterAsync(
        AccountId accountId, byte job, byte[] rawName, CancellationToken cancellationToken) =>
        throw Unused();

    public ValueTask<CharacterDeletionResult> DeleteCharacterAsync(
        AccountId accountId, ushort slotIndex, byte[] rawName, CancellationToken cancellationToken) =>
        throw Unused();

    private static NotSupportedException Unused() =>
        new("This repository knows one character and nothing else about characters.");
}
