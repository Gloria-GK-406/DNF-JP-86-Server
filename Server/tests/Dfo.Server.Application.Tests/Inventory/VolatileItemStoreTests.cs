using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Tests.Support;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// The same five operations against the store whose items do not outlive the process.
/// </summary>
/// <remarks>
/// These are deliberately the same five as the persistent store's, written the same way: the point
/// of the addressing question is that one vocabulary says everything on either carrier, so a
/// difference between the two files would be the answer coming out no.
///
/// Beyond the five, this fixes what the memory store owes the coordinator: everything is worked out
/// while preparing, so committing has nothing in it that could fail.
/// </remarks>
public sealed class VolatileItemStoreTests
{
    private static readonly ContainerId Run =
        ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.Main);

    private static readonly ContainerId Reward =
        ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.PersonalCargo);

    [Fact]
    public async Task Relocate_IsExpressedAsAClearAndAWrite()
    {
        var store = new VolatileItemStore();
        var transaction = new StubTransaction();
        await SeedAsync(store, (65, Potion(5)));
        var session = store.SessionIn(transaction);

        var moving = await session.ReadAsync(Run, 65, CancellationToken.None);
        session.Clear(Run, 65);
        session.Write(Run, new StoredItem(3, moving!.Core));
        await transaction.SettleAsync();

        Assert.Equal([(3, PotionId, 5)], await ReadAsync(store));
    }

    [Fact]
    public async Task Split_IsExpressedAsTwoWrites()
    {
        var store = new VolatileItemStore();
        var transaction = new StubTransaction();
        await SeedAsync(store, (65, Potion(5)));
        var session = store.SessionIn(transaction);

        var moving = await session.ReadAsync(Run, 65, CancellationToken.None);
        session.Write(Run, new StoredItem(65, moving!.Core with { Value = 3 }));
        session.Write(Run, new StoredItem(66, Potion(2)));
        await transaction.SettleAsync();

        Assert.Equal([(65, PotionId, 3), (66, PotionId, 2)], await ReadAsync(store));
    }

    [Fact]
    public async Task Swap_IsExpressedAsTwoWrites()
    {
        var store = new VolatileItemStore();
        var transaction = new StubTransaction();
        await SeedAsync(store, (65, Potion(5)), (66, Stone(1)));
        var session = store.SessionIn(transaction);

        var first = await session.ReadAsync(Run, 65, CancellationToken.None);
        var second = await session.ReadAsync(Run, 66, CancellationToken.None);
        session.Write(Run, new StoredItem(66, first!.Core));
        session.Write(Run, new StoredItem(65, second!.Core));
        await transaction.SettleAsync();

        Assert.Equal([(65, StoneId, 1), (66, PotionId, 5)], await ReadAsync(store));
    }

    [Fact]
    public async Task Merge_IsExpressedAsAWriteAndAClear()
    {
        var store = new VolatileItemStore();
        var transaction = new StubTransaction();
        await SeedAsync(store, (65, Potion(5)), (66, Potion(2)));
        var session = store.SessionIn(transaction);

        var moving = await session.ReadAsync(Run, 65, CancellationToken.None);
        var target = await session.ReadAsync(Run, 66, CancellationToken.None);
        session.Write(Run, new StoredItem(66, target!.Core with { Value = 7 }));
        session.Clear(Run, moving!.SlotIndex);
        await transaction.SettleAsync();

        Assert.Equal([(66, PotionId, 7)], await ReadAsync(store));
    }

    [Fact]
    public async Task PartialConsume_IsExpressedAsAWrite()
    {
        var store = new VolatileItemStore();
        var transaction = new StubTransaction();
        await SeedAsync(store, (65, Potion(5)));
        var session = store.SessionIn(transaction);

        var used = await session.ReadAsync(Run, 65, CancellationToken.None);
        session.Write(Run, new StoredItem(65, used!.Core with { Value = 4 }));
        await transaction.SettleAsync();

        Assert.Equal([(65, PotionId, 4)], await ReadAsync(store));
    }

    [Fact]
    public async Task ConsumingTheLastOfAPile_IsExpressedAsAClear()
    {
        var store = new VolatileItemStore();
        var transaction = new StubTransaction();
        await SeedAsync(store, (65, Potion(1)));
        var session = store.SessionIn(transaction);

        var used = await session.ReadAsync(Run, 65, CancellationToken.None);
        session.Clear(Run, used!.SlotIndex);
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
        var store = new VolatileItemStore();
        var transaction = new StubTransaction();
        var session = store.SessionIn(transaction);

        session.Write(Run, new StoredItem(0, Potion(3)));
        session.Write(Run, new StoredItem(1, Stone(1)));
        var moving = await session.ReadAsync(Run, 0, CancellationToken.None);
        session.Clear(Run, 0);
        session.Write(Run, new StoredItem(2, moving!.Core with { Value = 2 }));
        session.Write(Run, new StoredItem(0, Potion(1)));
        await transaction.SettleAsync();

        Assert.Equal(
            [(0, PotionId, 1), (1, StoneId, 1), (2, PotionId, 2)], await ReadAsync(store));
    }

    [Fact]
    public async Task Read_SeesThisSessionsOwnUncommittedWrites()
    {
        var store = new VolatileItemStore();
        var transaction = new StubTransaction();
        await SeedAsync(store, (65, Potion(5)));
        var session = store.SessionIn(transaction);

        session.Write(Run, new StoredItem(66, Potion(2)));
        session.Clear(Run, 65);

        Assert.Equal(2, (await session.ReadAsync(Run, 66, CancellationToken.None))!.Core.Value);
        Assert.Null(await session.ReadAsync(Run, 65, CancellationToken.None));
        Assert.Equal([66], (await session.ReadAllAsync(Run, CancellationToken.None))
            .Select(item => item.SlotIndex));
        // Nothing has reached the store itself yet.
        Assert.Equal([(65, PotionId, 5)], await ReadAsync(store));
    }

    [Fact]
    public async Task Rollback_LeavesTheStoredItemsUntouched()
    {
        var store = new VolatileItemStore();
        var transaction = new StubTransaction();
        await SeedAsync(store, (65, Potion(5)));
        var session = store.SessionIn(transaction);

        session.Clear(Run, 65);
        session.Write(Run, new StoredItem(3, Potion(5)));
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
        var store = new VolatileItemStore();
        var transaction = new StubTransaction();
        await SeedAsync(store, (65, Potion(5)));
        var session = store.SessionIn(transaction);
        var participant = Assert.IsAssignableFrom<ITransactionParticipant>(session);

        session.Write(Run, new StoredItem(3, Potion(1)));

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
        var store = new VolatileItemStore();
        await SeedAsync(store, (65, Potion(5)));
        await SeedAsync(store, Reward, (1, Stone(1)));
        var transaction = new StubTransaction();
        var session = store.SessionIn(transaction);

        var fromRun = await session.ReadAsync(Run, 65, CancellationToken.None);
        var fromReward = await session.ReadAsync(Reward, 1, CancellationToken.None);
        session.Clear(Run, 65);
        session.Clear(Reward, 1);
        session.Write(Run, new StoredItem(65, fromReward!.Core));
        session.Write(Reward, new StoredItem(1, fromRun!.Core));
        await transaction.SettleAsync();

        Assert.Equal([(65, StoneId, 1)], await ReadAsync(store));
        Assert.Equal([(1, PotionId, 5)], await ReadAsync(store, Reward));
    }

    [Fact]
    public async Task ClearingASlotThisSessionJustWroteLeavesItEmpty()
    {
        var store = new VolatileItemStore();
        await SeedAsync(store, (65, Potion(5)));
        var transaction = new StubTransaction();
        var session = store.SessionIn(transaction);

        session.Write(Run, new StoredItem(66, Potion(2)));
        session.Clear(Run, 66);

        Assert.Null(await session.ReadAsync(Run, 66, CancellationToken.None));
        await transaction.SettleAsync();
        Assert.Equal([(65, PotionId, 5)], await ReadAsync(store));
    }

    [Fact]
    public void ANegativeSlotIsRefused()
    {
        var session = new VolatileItemStore().SessionIn(new StubTransaction());

        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.Write(Run, new StoredItem(-1, Potion(1))));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Clear(Run, -1));
    }

    [Fact]
    public void Store_IsNotDurable()
    {
        Assert.False(new VolatileItemStore().IsDurable);
    }

    [Fact]
    public void Session_TakesPartOnceHoweverOftenItsStoreIsAsked()
    {
        var store = new VolatileItemStore();
        var transaction = new StubTransaction();

        var first = store.SessionIn(transaction);
        var second = store.SessionIn(transaction);

        Assert.Same(first, second);
        Assert.Single(transaction.Participants);
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
        VolatileItemStore store, params (int Slot, InventoryItemCoreState Core)[] seeded) =>
        SeedAsync(store, Run, seeded);

    private static async Task SeedAsync(
        VolatileItemStore store,
        ContainerId containerId,
        params (int Slot, InventoryItemCoreState Core)[] seeded)
    {
        var transaction = new StubTransaction();
        var session = store.SessionIn(transaction);
        foreach (var (slot, core) in seeded)
        {
            session.Write(containerId, new StoredItem(slot, core));
        }

        await transaction.SettleAsync();
    }

    private static async Task<List<(int Slot, int ItemId, int Count)>> ReadAsync(
        VolatileItemStore store, ContainerId? containerId = null)
    {
        var session = store.SessionIn(new StubTransaction());
        var items = await session.ReadAllAsync(containerId ?? Run, CancellationToken.None);
        return [.. items.Select(item => (item.SlotIndex, item.Core.ItemId, item.Core.Value))];
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
