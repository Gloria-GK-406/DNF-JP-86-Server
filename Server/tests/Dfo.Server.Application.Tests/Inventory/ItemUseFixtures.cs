using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
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
/// One character, published, with the level and job the test cares about.
/// </summary>
/// <remarks>
/// Every other member of the entry result is left null. The usability service reads
/// <see cref="SelectCharacterResult.Character"/> and nothing else, and a fixture that filled in
/// the other eight would hide the day it starts reading a ninth.
/// </remarks>
internal sealed class FixedRuntimeSessions(byte level, byte job, bool published = true)
    : ISelectedSessionRuntimeDirectory
{
    /// <summary>A directory that has not been published to yet, as during entry.</summary>
    public static FixedRuntimeSessions Unpublished => new(level: 0, job: 0, published: false);

    private readonly SelectCharacterResult result = new(
        new ActiveCharacterState(
            new CharacterIdentityState(new CharacterId(60), 0, [], job, GrowType: 0),
            new CharacterProgressionState(level, 0),
            new CharacterFatigueState(0, 0),
            new CharacterPositionState(1, 0, 0, 0, 0),
            DateTimeOffset.UnixEpoch,
            DarkKnightCombo: null),
        null!, null!, null!, null!, null!, null!);

    public void Publish(
        SelectedSessionIdentity session, SelectCharacterResult value, bool shouldPersistPosition) =>
        throw new NotSupportedException("This fixture only answers.");

    public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult value) =>
        throw new NotSupportedException("This fixture only answers.");

    public SelectCharacterResult GetRequired(SelectedSessionIdentity session) =>
        published ? result : throw new KeyNotFoundException("Nothing was published for it.");

    public bool TryGet(
        SelectedSessionIdentity session,
        out SelectCharacterResult? value,
        out bool shouldPersistPosition)
    {
        value = published ? result : null;
        shouldPersistPosition = false;
        return published;
    }

    public bool RemoveExact(SelectedSessionIdentity session) => false;
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

    private readonly VolatileItemStore store = new();

    private ContainerId Main => ContainerId.ForCharacter(this.character, InventoryContainerType.Main);

    public DomainTransactionScope Scope { get; } = new();

    public ICharacterContainerDomainService Containers()
    {
        var containers = new CharacterContainerDomainService();
        containers.Register(this.character, new PersistentItemContainer(
            this.Main,
            this.character,
            new ContainerShape(InventoryContainerType.Main, 0),
            this.store,
            this.Scope,
            null!));
        return containers;
    }

    /// <summary>What the container holds of one item, in slot order.</summary>
    public List<(int Slot, int ItemId, int Count)> PilesOf(int itemId) =>
        [.. this.Rows().Where(row => row.ItemId == itemId)];

    public void Seed(int slotIndex, int itemId, int count, byte itemKind = 2) =>
        this.Settle(session => session.Write(
            this.Main, new StoredItem(slotIndex, StackableItemCore.Create(itemKind, itemId, count))));

    public List<(int Slot, int ItemId, int Count)> Rows()
    {
        var alone = new DomainTransaction();
        var read = this.store.SessionIn(alone).ReadAllAsync(this.Main, CancellationToken.None);
        var items = read.IsCompleted ? read.Result : read.AsTask().GetAwaiter().GetResult();
        return [.. items
            .OrderBy(item => item.SlotIndex)
            .Select(item => (item.SlotIndex, item.Core.ItemId, item.Core.Value))];
    }

    public int CountIn(int slotIndex) =>
        this.Rows().FirstOrDefault(row => row.Slot == slotIndex).Count;

    /// <remarks>
    /// The memory store settles without waiting for anything, so nothing here can block; this is
    /// what lets a fixture seed itself from a property initialiser.
    /// </remarks>
    private void Settle(Action<IItemStoreSession> write)
    {
        var alone = new DomainTransaction();
        write(this.store.SessionIn(alone));
        var settling = alone.CommitAsync(CancellationToken.None);
        if (!settling.IsCompleted)
            settling.AsTask().GetAwaiter().GetResult();
    }
}
