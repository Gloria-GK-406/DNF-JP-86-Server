using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Tests.Support;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// Lifecycle operations against the store whose items do not outlive the process.
/// </summary>
/// <remarks>
/// These are deliberately the same five as the persistent store's, written the same way: the point
/// of the addressing question is that one vocabulary says everything on either carrier, so a
/// difference between the two files would be the answer coming out no.
///
/// Beyond the five, this fixes what the memory store owes the coordinator: everything is worked out
/// while preparing, so committing has nothing in it that could fail.
/// </remarks>
public sealed class VolatileItemDomainStoreTests
{
    private static readonly ContainerId Run =
        ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.Main);

    private static readonly ContainerId Reward =
        ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.PersonalCargo);

    [Fact]
    public async Task Relocate_TakesAndPutsTheSameRecord()
    {
        var store = new VolatileItemDomainStore();
        var transaction = new StubTransaction();
        await SeedAsync(store, (65, Potion(5)));
        var session = store.SessionIn(transaction);

        var moving = await session.GetAsync(Run, 65, CancellationToken.None);
        await session.PutAsync(Run, 3, await session.TakeAsync(moving!, null, default), default);
        await transaction.SettleAsync();

        Assert.Equal([(3, PotionId, 5)], await ReadAsync(store));
    }

    [Fact]
    public async Task Split_TakesAPartialPileAndPutsItElsewhere()
    {
        var store = new VolatileItemDomainStore();
        var transaction = new StubTransaction();
        await SeedAsync(store, (65, Potion(5)));
        var session = store.SessionIn(transaction);

        var moving = await session.GetAsync(Run, 65, CancellationToken.None);
        await session.PutAsync(Run, 66, await session.TakeAsync(moving!, 2, default), default);
        await transaction.SettleAsync();

        Assert.Equal([(65, PotionId, 3), (66, PotionId, 2)], await ReadAsync(store));
    }

    [Fact]
    public async Task Swap_TakesBothRecordsBeforePuttingEither()
    {
        var store = new VolatileItemDomainStore();
        var transaction = new StubTransaction();
        await SeedAsync(store, (65, Potion(5)), (66, Stone(1)));
        var session = store.SessionIn(transaction);

        var first = await session.GetAsync(Run, 65, CancellationToken.None);
        var second = await session.GetAsync(Run, 66, CancellationToken.None);
        var a = await session.TakeAsync(first!, null, default);
        var b = await session.TakeAsync(second!, null, default);
        await session.PutAsync(Run, 66, a, default);
        await session.PutAsync(Run, 65, b, default);
        await transaction.SettleAsync();

        Assert.Equal([(65, StoneId, 1), (66, PotionId, 5)], await ReadAsync(store));
    }

    [Fact]
    public async Task Merge_PutsTakenPileIntoCompatibleOccupancy()
    {
        var store = new VolatileItemDomainStore();
        var transaction = new StubTransaction();
        await SeedAsync(store, (65, Potion(5)), (66, Potion(2)));
        var session = store.SessionIn(transaction);

        var moving = await session.GetAsync(Run, 65, CancellationToken.None);
        var target = await session.GetAsync(Run, 66, CancellationToken.None);
        await session.PutAsync(Run, target!.SlotIndex, await session.TakeAsync(moving!, null, default), default);
        await transaction.SettleAsync();

        Assert.Equal([(66, PotionId, 7)], await ReadAsync(store));
    }

    [Fact]
    public async Task PartialConsume_TakesAndDeletesOnlyConsumedCount()
    {
        var store = new VolatileItemDomainStore();
        var transaction = new StubTransaction();
        await SeedAsync(store, (65, Potion(5)));
        var session = store.SessionIn(transaction);

        var used = await session.GetAsync(Run, 65, CancellationToken.None);
        await session.DeleteItemAsync(await session.TakeAsync(used!, 1, default), default);
        await transaction.SettleAsync();

        Assert.Equal([(65, PotionId, 4)], await ReadAsync(store));
    }

    [Fact]
    public async Task ConsumingTheLastOfAPile_TakesAndDeletesTheRecord()
    {
        var store = new VolatileItemDomainStore();
        var transaction = new StubTransaction();
        await SeedAsync(store, (65, Potion(1)));
        var session = store.SessionIn(transaction);

        var used = await session.GetAsync(Run, 65, CancellationToken.None);
        await session.DeleteItemAsync(await session.TakeAsync(used!, null, default), default);
        await transaction.SettleAsync();

        Assert.Empty(await ReadAsync(store));
    }

    /// <summary>
    /// No slot needs a number for the store to hand back, on either carrier.
    /// </summary>
    /// <remarks>
    /// The container this stands in for is the one that used to have to mint fake row numbers to
    /// look like the database. Here a whole run of operations is carried out with nothing but the
    /// container and the slot, which is what makes those numbers removable.
    /// </remarks>
    [Fact]
    public async Task AWholeRunOfOperationsNeedsNoIdentityForAnyItem()
    {
        var store = new VolatileItemDomainStore();
        var transaction = new StubTransaction();
        var session = store.SessionIn(transaction);

        await session.PutAsync(Run, 0, await session.CreateAsync(ItemSnapshot.FromCore(Potion(3), true), default), default);
        await session.PutAsync(Run, 1, await session.CreateAsync(ItemSnapshot.FromCore(Stone(1), true), default), default);
        var moving = await session.GetAsync(Run, 0, CancellationToken.None);
        await session.PutAsync(Run, 2, await session.TakeAsync(moving!, 2, default), default);
        await transaction.SettleAsync();

        Assert.Equal(
            [(0, PotionId, 1), (1, StoneId, 1), (2, PotionId, 2)], await ReadAsync(store));
    }

    [Fact]
    public async Task Read_SeesThisSessionsOwnUncommittedWrites()
    {
        var store = new VolatileItemDomainStore();
        var transaction = new StubTransaction();
        await SeedAsync(store, (65, Potion(5)));
        var session = store.SessionIn(transaction);

        await session.PutAsync(Run, 66, await session.CreateAsync(ItemSnapshot.FromCore(Potion(2), true), default), default);
        await session.DeleteItemAsync(await session.TakeAsync((await session.GetAsync(Run, 65, default))!, null, default), default);

        Assert.Equal(2, (await session.GetAsync(Run, 66, CancellationToken.None))!.Count);
        Assert.Null(await session.GetAsync(Run, 65, CancellationToken.None));
        Assert.Equal([66], (await session.GetAllAsync(Run, CancellationToken.None))
            .Select(item => item.SlotIndex));
        // Nothing has reached the store itself yet.
        Assert.Equal([(65, PotionId, 5)], await ReadAsync(store));
    }

    [Fact]
    public async Task Rollback_LeavesTheStoredItemsUntouched()
    {
        var store = new VolatileItemDomainStore();
        var transaction = new StubTransaction();
        await SeedAsync(store, (65, Potion(5)));
        var session = store.SessionIn(transaction);

        await session.DeleteItemAsync(await session.TakeAsync((await session.GetAsync(Run, 65, default))!, null, default), default);
        await session.PutAsync(Run, 3, await session.CreateAsync(ItemSnapshot.FromCore(Potion(5), true), default), default);
        await transaction.PrepareAsync();
        await transaction.RollbackAsync();

        Assert.Equal([(65, PotionId, 5)], await ReadAsync(store));
    }

    /// <summary>
    /// Committing announces itself as unable to fail, and what it does is the reason it can.
    /// </summary>
    /// <remarks>
    /// Preparing works out each touched container's whole replacement contents; committing assigns
    /// them. Asserted by committing without preparing: it must refuse, because a commit that still
    /// had the working-out in it would be a commit that could fail, and the coordinator would then
    /// be holding two participants that can. Refusing rather than quietly doing nothing matters
    /// too -- the second would show up as items that vanished, not as an exception.
    /// </remarks>
    [Fact]
    public async Task Commit_RefusesToStandInForAPrepareThatNeverRan()
    {
        var store = new VolatileItemDomainStore();
        var transaction = new StubTransaction();
        await SeedAsync(store, (65, Potion(5)));
        var session = store.SessionIn(transaction);
        var participant = Assert.IsAssignableFrom<ITransactionParticipant>(session);

        await session.PutAsync(Run, 3, await session.CreateAsync(ItemSnapshot.FromCore(Potion(1), true), default), default);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await participant.CommitAsync(CancellationToken.None));
        Assert.False(participant.CommitCanFail);
        Assert.Equal([(65, PotionId, 5)], await ReadAsync(store));
    }

    /// <remarks>
    /// The persistent store needs its containers emptied before any is filled, or a swap collides
    /// with the row it is displacing. Here there is no such key, but the operation has to read the
    /// same either way, so the same case is written out for both carriers.
    /// </remarks>
    [Fact]
    public async Task TwoContainersExchangeTheirContentsInOneSettlement()
    {
        var store = new VolatileItemDomainStore();
        await SeedAsync(store, (65, Potion(5)));
        await SeedAsync(store, Reward, (1, Stone(1)));
        var transaction = new StubTransaction();
        var session = store.SessionIn(transaction);

        var fromRun = await session.GetAsync(Run, 65, CancellationToken.None);
        var fromReward = await session.GetAsync(Reward, 1, CancellationToken.None);
        var runRecord = await session.TakeAsync(fromRun!, null, default);
        var rewardRecord = await session.TakeAsync(fromReward!, null, default);
        await session.PutAsync(Run, 65, rewardRecord, default);
        await session.PutAsync(Reward, 1, runRecord, default);
        await transaction.SettleAsync();

        Assert.Equal([(65, StoneId, 1)], await ReadAsync(store));
        Assert.Equal([(1, PotionId, 5)], await ReadAsync(store, Reward));
    }

    [Fact]
    public async Task ClearingASlotThisSessionJustWroteLeavesItEmpty()
    {
        var store = new VolatileItemDomainStore();
        await SeedAsync(store, (65, Potion(5)));
        var transaction = new StubTransaction();
        var session = store.SessionIn(transaction);

        await session.PutAsync(Run, 66, await session.CreateAsync(ItemSnapshot.FromCore(Potion(2), true), default), default);
        await session.DeleteItemAsync(await session.TakeAsync((await session.GetAsync(Run, 66, default))!, null, default), default);

        Assert.Null(await session.GetAsync(Run, 66, CancellationToken.None));
        await transaction.SettleAsync();
        Assert.Equal([(65, PotionId, 5)], await ReadAsync(store));
    }

    [Fact]
    public async Task ANegativeSlotIsRefused()
    {
        var session = new VolatileItemDomainStore().SessionIn(new StubTransaction());
        var record = await session.CreateAsync(ItemSnapshot.FromCore(Potion(1), true), default);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.PutAsync(Run, -1, record, default).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.GetAsync(Run, -1, default).AsTask());
        await session.DeleteItemAsync(record, default);
    }

    [Fact]
    public void Store_IsNotDurable()
    {
        Assert.False(new VolatileItemDomainStore().IsDurable);
    }

    [Fact]
    public void Session_TakesPartOnceHoweverOftenItsStoreIsAsked()
    {
        var store = new VolatileItemDomainStore();
        var transaction = new StubTransaction();

        var first = store.SessionIn(transaction);
        var second = store.SessionIn(transaction);

        Assert.Same(first, second);
        Assert.Single(transaction.Participants.OfType<ItemLifecycleScope>());
        Assert.Single(transaction.Participants, x => x is not ItemLifecycleScope);
    }

    private const int PotionId = 1000;

    private const int StoneId = 2000;

    private static InventoryItemCoreState Potion(int count) => Core(PotionId, count);

    private static InventoryItemCoreState Stone(int count) => Core(StoneId, count);

    private static InventoryItemCoreState Core(int itemId, int count) =>
        new(6, itemId, count, 0, 0, 0, 0, 0, 0, 0, -1,
            new ChronicleOptionState(0, 0, 0, 0, 0), new ChronicleOptionState(0, 0, 0, 0, 0),
            0, 0, 0, 0, 0,
            new RandomOptionState(0, 0, 0), new RandomOptionState(0, 0, 0),
            new RandomOptionState(0, 0, 0), 0, byte.MaxValue, 0, new RandomOptionState(0, 0, 0),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Puts items in the store the only way there is: through a settled transaction.</summary>
    private static Task SeedAsync(
        VolatileItemDomainStore store, params (int Slot, InventoryItemCoreState Core)[] seeded) =>
        SeedAsync(store, Run, seeded);

    private static async Task SeedAsync(
        VolatileItemDomainStore store,
        ContainerId containerId,
        params (int Slot, InventoryItemCoreState Core)[] seeded)
    {
        var transaction = new StubTransaction();
        var session = store.SessionIn(transaction);
        foreach (var (slot, core) in seeded)
        {
            await session.PutAsync(containerId, slot, await session.CreateAsync(ItemSnapshot.FromCore(core, true), default), default);
        }

        await transaction.SettleAsync();
    }

    private static async Task<List<(int Slot, int ItemId, int Count)>> ReadAsync(
        VolatileItemDomainStore store, ContainerId? containerId = null)
    {
        var session = store.SessionIn(new StubTransaction());
        var items = await session.GetAllAsync(containerId ?? Run, CancellationToken.None);
        return [.. items.Select(item => (item.SlotIndex, item.ItemId, item.Count))];
    }

    /// <summary>
    /// Stands in for the coordinator, so what these tests prove is the store's own behaviour
    /// rather than the coordinator's.
    /// </summary>
    private sealed class StubTransaction : IDomainTransaction
    {
        private readonly Dictionary<object, ITransactionParticipant> enlisted = [];

        public IReadOnlyCollection<ITransactionParticipant> Participants => this.enlisted.Values;

        public TParticipant Enlist<TParticipant>(object identity, Func<TParticipant> create)
            where TParticipant : ITransactionParticipant
        {
            if (this.enlisted.TryGetValue(identity, out var existing))
            {
                return (TParticipant)existing;
            }

            var participant = create();
            this.enlisted.Add(identity, participant);
            return participant;
        }

        public async Task PrepareAsync()
        {
            foreach (var participant in this.enlisted.Values)
            {
                await participant.PrepareAsync(CancellationToken.None);
            }
        }

        public async Task RollbackAsync()
        {
            foreach (var participant in this.enlisted.Values)
            {
                await participant.RollbackAsync(CancellationToken.None);
            }
        }

        public async Task SettleAsync()
        {
            await this.PrepareAsync();
            foreach (var participant in this.enlisted.Values)
            {
                await participant.CommitAsync(CancellationToken.None);
            }
        }
    }
}
