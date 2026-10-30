using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Application.Tests.Support;
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
using Dfo.Server.Testing;

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
    DomainTransactionScope? scope = null, TutorialRewardTestDriver? reward = null) : ICommandBus, ICommandWorkUnits
{
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

    private readonly DomainTransactionScope scope = scope ?? TestRequired.TransactionScope();
    private ICommandHandler<Dfo.Server.Protocol.Game.TaggedIdReport.GameTaggedIdReportCommand, bool>? report;
    public Dfo.Server.Protocol.Game.TaggedIdReport.TaggedIdReportHandler Report(
        Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService users,
        Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService sessions,
        Dfo.Server.Abstractions.World.ITutorialRewardDomainService rewards,
        Dfo.Server.Protocol.Game.IGameMessageSender sender,
        Microsoft.Extensions.Logging.ILogger<Dfo.Server.Protocol.Game.TaggedIdReport.TaggedIdReportHandler> logger)
    {
        report = new Dfo.Server.Protocol.Game.TaggedIdReport.GameTaggedIdReportCommandHandler(users,sessions,            rewards,reward!.Containers,reward.Delivery,ProtocolAlgorithmFixture.Default<ITutorialCompletionDomainService>(),this,new TestOutbox(sender),logger);
        return new(this);
    }
    private sealed class TestOutbox(Dfo.Server.Protocol.Game.IGameMessageSender sender):Dfo.Server.Protocol.Game.IGameCommandOutbox {
        public ValueTask RegisterCmdAsync(Dfo.Server.Data.Networking.NetworkConnectionId id,Dfo.Server.Protocol.Game.IGameCmdMessage m,CancellationToken ct)=>sender.SendCmdAsync(id,m,ct);
        public ValueTask RegisterNotiAsync(Dfo.Server.Data.Networking.NetworkConnectionId id,Dfo.Server.Protocol.Game.IGameNotiMessage m,CancellationToken ct)=>sender.SendNotiAsync(id,m,ct);
        public ValueTask RegisterExplicitAsync(Dfo.Server.Data.Networking.NetworkConnectionId id,Dfo.Server.Protocol.Game.GameExplicitMessageDelivery d,CancellationToken ct)=>sender.SendExplicitAsync(id,d,ct);
        public ValueTask RegisterBatchAsync(Dfo.Server.Data.Networking.NetworkConnectionContext c,IReadOnlyList<Dfo.Server.Protocol.Game.IGameServerMessage> m,CancellationToken ct)=>throw new NotSupportedException();
    }
    public async ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct = default) where TResult : notnull
    {
        var context = DomainCommandFixture.Context(Guid.Empty, command.GetType(), typeof(DirectCommandBus));
        if(command is Dfo.Server.Protocol.Game.TaggedIdReport.GameTaggedIdReportCommand q)
            return (TResult)(object)await report!.HandleAsync(context,q,ct);
        await using var transaction = scope.Begin();
        var result = await reward!.HandleAsync(context,(TutorialRewardTestInput)(object)command,ct);
        await transaction.CommitAsync(ct); return (TResult)(object)result;
    }
    public async ValueTask<T> WriteAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a, CancellationToken ct=default) where T:notnull
    { await using var tx=scope.Begin();var result=await a(null!,ct);await tx.CommitAsync(ct);return result; }
    public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new NotSupportedException();
    public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new NotSupportedException();
}

internal sealed record TutorialRewardTestInput(CharacterId CharacterId, IReadOnlyList<DeclaredRewardItem> Declared,
    Dfo.Server.Data.Accounts.AccountId AccountId, bool CompleteTutorial=false) : ICommand<TutorialRewardResult>;
internal sealed class TutorialRewardTestDriver(ICharacterContainerDomainService containers, IItemDeliveryDomainService delivery,
    Dfo.Server.Abstractions.Characters.ITutorialCompletionDomainService tutorial)
{
    internal ICharacterContainerDomainService Containers => containers;
    internal IItemDeliveryDomainService Delivery => delivery;
    public ValueTask<TutorialRewardResult> HandleAsync(CommandExecutionContext context,TutorialRewardTestInput input,CancellationToken ct) =>
        Dfo.Server.Protocol.Game.TaggedIdReport.GameTaggedIdReportCommandHandler.GrantAsync(containers,delivery,tutorial,input.CharacterId,input.Declared,input.AccountId,input.CompleteTutorial,ct);
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

    public DomainTransactionScope Scope { get; init; } = new(logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<Dfo.Server.Application.Transactions.DomainTransaction>.Instance, outboxes: TestRequired.Outboxes());
    public IItemLifecycleDomainService Lifecycle => LifecycleFor(new ConsumableBandPlacements());
    public IItemLifecycleDomainService LifecycleFor(IItemPlacementDefinitionSource placements) =>
        new ItemLifecycleDomainService(store, Scope, new ItemPlacementRuleService(placements, new NoWearables()), clock: TimeProvider.System, expiration: null!);

    /// <remarks>
    /// The archive is a parameter because the bag now reads it: what an item's band is, and how
    /// large a pile of it may grow, are questions the bag asks before it places anything. A fixture
    /// that fixed one answer here would make every test that needs a different band build its own
    /// container instead.
    /// </remarks>
    public ICharacterContainerDomainService Containers(
        IItemPlacementDefinitionSource? placements = null)
    {
        var containers = new CharacterContainerDomainService(TestRequired.TransactionScope());
        TestGameplay.Run(() => containers.Register(this.character, new PersistentItemContainer(
            this.Main,
            new ContainerShape(InventoryContainerType.Main, 0),
            this.store,
            this.Scope,
            new ItemPlacementRuleService(
                placements ?? new ConsumableBandPlacements(), new NoWearables()),
            new ItemSlotRuleService())));
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

/// <summary>The entity gateway for exactly one character.</summary>
internal sealed class OneCharacter(ICharacter character) : ICharacterDomainService
{
    public ICharacter Get(CharacterId characterId) => character;
}

/// <summary>Builds a complete fresh transport input for the actual protocol command.</summary>
internal static class WholeUse
{
    private static long ordinal;
    internal static Dfo.Server.Protocol.Game.UseStackable.GameUseStackableCommand Command(IServiceProvider services,
        Dfo.Server.Data.World.SelectedSessionIdentity session, BusinessSlot slot, int itemCode)
    {
        var query = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService>(services);
        if (!query.TryGetByConnection(session.ConnectionId, out var user) || user is null) throw new InvalidOperationException("Missing physical fixture connection.");
        var game = A21InventoryLayout.ToGameSlot(slot);
        return new(new(user.Connection), Input(game, itemCode));
    }
    internal static Dfo.Server.Protocol.Game.UseStackable.UseStackableRequest Input(GameSlot slot, int itemCode, int instance = 0)
    {
        var next = Interlocked.Increment(ref ordinal);
        var body = new byte[11];
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(body, checked((short)slot.Index));
        body[2] = (byte)slot.Space;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(3), instance);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(7), itemCode);
        var metadata = (new Dfo.Server.Data.Networking.GameMessageMetadata(1, 0x2C, 0, unchecked((ushort)next))
            { InboundOrdinal = next, SequenceIsFresh = true }).WithRawBody(body);
        return new(metadata, slot, instance, itemCode);
    }
}

/// <summary>Direct domain access retaining the original explicit fixture transaction.</summary>
internal sealed class DirectInventoryMove(IInventoryMoveDomainService move,DomainTransactionScope? scope=null)
{
    private readonly DomainTransactionScope scope=scope??new(logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<Dfo.Server.Application.Transactions.DomainTransaction>.Instance, outboxes: TestRequired.Outboxes());
    public async ValueTask<ItemMoveResult> MoveAsync(InventoryMoveRequest request,CancellationToken ct=default)
    {
        if(scope.Current is not null)return await move.MoveAsync(request,ct);
        await using var tx=scope.Begin();var result=await move.MoveAsync(request,ct);await tx.CommitAsync(ct);return result;
    }
}
internal static class InventoryMoveTestAccess
{
    internal static async ValueTask<ItemMoveResult> MoveAsync(IServiceProvider services,InventoryMoveRequest request,CancellationToken ct=default)=>
        await TestGameplay.RunAsync(services,async()=>await new DirectInventoryMove(
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IInventoryMoveDomainService>(services),
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<DomainTransactionScope>(services)).MoveAsync(request,ct));
}
/// <summary>Local whole orchestration with the original fake inventory's actual transaction.</summary>
internal static class MoveWholeTestDriver
{
    internal sealed record Execution(IInventoryMoveDomainService Domain,DomainTransactionScope Scope);
    internal static MoveItemSpaceHandler Create(ISelectedSessionDomainService sessions,
        ISelectedCharacterCurrentViewDomainService views,
        Execution execution,Dfo.Server.Protocol.Game.IGameMessageSender sender,
        Microsoft.Extensions.Logging.ILogger<GameMoveItemSpaceCommandHandler> logger,
        ICharacterInventoryEntryDomainService character,IAccountInventoryEntryDomainService account,
        TimeProvider time,Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService? housing=null)=>
        new(new Bus(new(sessions,views,execution.Domain,new Units(execution.Scope),new Outbox(sender),logger,character,account,time,housing ?? ProtocolAlgorithmFixture.Default<IMarriageHousingQueryDomainService>())));
    private sealed class Bus(GameMoveItemSpaceCommandHandler handler):ICommandBus
    {
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

        public async ValueTask<T> SendAsync<T>(ICommand<T> command,CancellationToken ct=default)where T:notnull=>
            (T)(object)await handler.HandleAsync(new(Guid.NewGuid(),typeof(GameMoveItemSpaceCommand),typeof(GameMoveItemSpaceCommandHandler)),Assert.IsType<GameMoveItemSpaceCommand>(command),ct);
    }
    private sealed class Units(DomainTransactionScope scope):ICommandWorkUnits
    {
        public async ValueTask<T> WriteAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull
        {Assert.Equal(CommandWorkUnitKind.Ordinary,d.Kind);Assert.Equal(CommandFootprintMode.None,d.Footprint);Assert.Single(d.Participants);if(scope.Current is not null)return await a(null!,ct);await using var tx=scope.Begin();var result=await a(null!,ct);await tx.CommitAsync(ct);return result;}
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new NotSupportedException();
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new NotSupportedException();
    }
    private sealed class Outbox(Dfo.Server.Protocol.Game.IGameMessageSender sender):Dfo.Server.Protocol.Game.IGameCommandOutbox
    {
        public ValueTask RegisterCmdAsync(Dfo.Server.Data.Networking.NetworkConnectionId id,Dfo.Server.Protocol.Game.IGameCmdMessage m,CancellationToken ct)=>sender.SendCmdAsync(id,m,ct);
        public ValueTask RegisterNotiAsync(Dfo.Server.Data.Networking.NetworkConnectionId id,Dfo.Server.Protocol.Game.IGameNotiMessage m,CancellationToken ct)=>sender.SendNotiAsync(id,m,ct);
        public ValueTask RegisterBatchAsync(Dfo.Server.Data.Networking.NetworkConnectionContext target,IReadOnlyList<Dfo.Server.Protocol.Game.IGameServerMessage> m,CancellationToken ct)=>sender.SendBatchAsync(target,m,ct);
        public ValueTask RegisterExplicitAsync(Dfo.Server.Data.Networking.NetworkConnectionId id,Dfo.Server.Protocol.Game.GameExplicitMessageDelivery d,CancellationToken ct)=>sender.SendExplicitAsync(id,d,ct);
    }
}
