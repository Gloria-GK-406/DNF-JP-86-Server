using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.PrivateStore;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.PrivateStore;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Xunit;

namespace Dfo.Server.Application.Tests.PrivateStore;

/// <summary>
/// A visit belongs to the exact visitor and owner sessions, never to a later session of either character;
/// taking a stall down settles with the transaction that returns its goods.
/// </summary>
public sealed class PrivateStoreVisitTests
{
    private static readonly TownAreaKey Area = new(1, 2, 2);

    private static SelectedSessionIdentity Session(int character, int generation = 1) =>
        new(new AccountId(character), new CharacterId(character), new($"stall-{character}-{generation}"), generation, (ushort)character, 10011);

    private sealed class World
    {
        public HashSet<SelectedSessionIdentity> InWorld { get; } = [];
        public DomainTransaction Transaction { get; set; } = new();
        public PrivateStoreDomainService Stores { get; }

        public World()
        {
            var sessions = TestRequired.Absent<ISelectedSessionDomainService>((method, args) =>
                method.Name == nameof(ISelectedSessionDomainService.IsInWorld) ? InWorld.Contains((SelectedSessionIdentity)args![0]!) : null);
            var towns = TestRequired.Absent<ITownAreaDomainService>((method, args) =>
                method.Name == nameof(ITownAreaDomainService.Find) && args![0] is CharacterId character
                    ? InWorld.Where(session => session.CharacterId == character)
                        .Select(session => new TownAreaMember(session, Area, new CharacterPositionState(2, 2, 100, 200, 0), true))
                        .FirstOrDefault()
                    : null);
            IReadOnlyList<AccountPremiumEntryState> licence = [new((int)PremiumServiceType.PrivateStoreLicense, long.MaxValue)];
            var premium = TestRequired.Absent<IAccountPremiumServices>((method, _) =>
                method.Name == nameof(IAccountPremiumServices.ReadAsync) ? ValueTask.FromResult(licence) : null);
            var premiums = TestRequired.Absent<IPremiumServiceDomainService>((method, _) =>
                method.Name == nameof(IPremiumServiceDomainService.For) ? premium : null);
            var custody = new EmptyCustody();
            var items = TestRequired.Absent<IItemDomainStore>((method, _) =>
                method.Name == nameof(IItemDomainStore.SessionIn) ? custody : null);
            Stores = new PrivateStoreDomainService(new PrivateStoreRuleService(), null!, premiums, sessions, towns,
                null!, null!, null!, items, null!, null!, null!, new Accessor(this), TimeProvider.System);
        }

        public SelectedSessionIdentity Join(SelectedSessionIdentity session)
        {
            InWorld.Add(session);
            return session;
        }
    }

    private sealed class Accessor(World world) : IDomainTransactionAccessor
    {
        public IDomainTransaction? Current => world.Transaction;
    }

    /// <summary>A seller with nothing left in custody.</summary>
    private sealed class EmptyCustody : IItemDomainStoreSession, IPrivateStoreItemCustodySession
    {
        public ValueTask<IReadOnlyList<PrivateStoreLine>> ReadPrivateStoreLinesAsync(CharacterId owner, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<PrivateStoreLine>>([]);
        public ValueTask AcceptPrivateStoreLineAsync(CharacterId owner, byte displaySlot, long unitPrice, ItemRecord record, CancellationToken ct) =>
            throw new NotSupportedException();
        public ValueTask<ItemRecord> TakePrivateStoreLineAsync(CharacterId owner, byte displaySlot, int? count, CancellationToken ct) =>
            throw new NotSupportedException();
        public ValueTask<ItemReference?> GetAsync(ContainerId container, int slot, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(ContainerId container, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ItemRecord> TakeAsync(ItemReference reference, int? count, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ItemReference> PutAsync(ContainerId container, int slot, ItemRecord record, CancellationToken ct) =>
            throw new NotSupportedException();
        public ValueTask<ItemReference> UpdateAsync(ItemReference reference, Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct) =>
            throw new NotSupportedException();
        public ValueTask<ItemRecord> CreateAsync(ItemSnapshot snapshot, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask DeleteItemAsync(ItemRecord record, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask ApplyDecidedWritesAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_close_that_rolls_back_leaves_the_stall_and_its_visitors()
    {
        var world = new World();
        var owner = world.Join(Session(1));
        var visitor = world.Join(Session(2));
        await TestGameplay.RunAsync(async () =>
        {
            await world.Stores.OpenAsync(owner, new(0, 2, 2, 100, 200), null, default);
            world.Stores.Enter(visitor, owner);
            Assert.NotNull(await world.Stores.CloseAsync(owner, default));
        });
        Assert.NotNull(world.Stores.Find(owner.CharacterId));

        await world.Transaction.RollbackAsync();

        Assert.NotNull(world.Stores.Find(owner.CharacterId));
        Assert.Equal(owner, world.Stores.Visiting(visitor));
    }

    [Fact]
    public async Task A_close_takes_the_stall_down_when_it_commits()
    {
        var world = new World();
        var owner = world.Join(Session(1));
        var visitor = world.Join(Session(2));
        await TestGameplay.RunAsync(async () =>
        {
            await world.Stores.OpenAsync(owner, new(0, 2, 2, 100, 200), null, default);
            world.Stores.Enter(visitor, owner);
            Assert.NotNull(await world.Stores.CloseAsync(owner, default));
        });

        await world.Transaction.CommitAsync(default);

        Assert.Null(world.Stores.Find(owner.CharacterId));
        Assert.Null(world.Stores.Visiting(visitor));
    }

    [Fact]
    public async Task A_later_session_of_the_visitor_does_not_inherit_the_visit()
    {
        var world = new World();
        var owner = world.Join(Session(1));
        var visitor = world.Join(Session(2));
        await TestGameplay.RunAsync(async () =>
        {
            await world.Stores.OpenAsync(owner, new(0, 2, 2, 100, 200), null, default);
            world.Stores.Enter(visitor, owner);
        });
        Assert.Equal(owner, world.Stores.Visiting(visitor));
        Assert.Equal([visitor], world.Stores.VisitorsOf(owner));

        world.InWorld.Remove(visitor);
        var returned = world.Join(Session(2, generation: 2));

        Assert.Null(world.Stores.Visiting(returned));
        Assert.Empty(world.Stores.VisitorsOf(owner));
        TestGameplay.Run(() => world.Stores.Leave(returned));
    }

    [Fact]
    public async Task A_visit_ends_with_the_owner_session_that_kept_the_stall()
    {
        var world = new World();
        var owner = world.Join(Session(1));
        var visitor = world.Join(Session(2));
        await TestGameplay.RunAsync(async () =>
        {
            await world.Stores.OpenAsync(owner, new(0, 2, 2, 100, 200), null, default);
            world.Stores.Enter(visitor, owner);
        });

        world.InWorld.Remove(owner);
        var ownerAgain = world.Join(Session(1, generation: 2));

        Assert.Null(world.Stores.Visiting(visitor));
        Assert.Empty(world.Stores.VisitorsOf(ownerAgain));
    }

    [Theory]
    [InlineData(PremiumServiceType.PrivateStoreLicense, true)]
    [InlineData(PremiumServiceType.AuctionCoupon, true)]
    [InlineData(PremiumServiceType.GrowthContract, false)]
    public void The_licence_or_the_auction_coupon_permits_a_stall(PremiumServiceType active, bool permitted) =>
        Assert.Equal(permitted, new PrivateStoreRuleService().Permits([active]));

    [Fact]
    public async Task Leaving_from_a_stale_session_keeps_the_current_visit()
    {
        var world = new World();
        var owner = world.Join(Session(1));
        var visitor = world.Join(Session(2));
        await TestGameplay.RunAsync(async () =>
        {
            await world.Stores.OpenAsync(owner, new(0, 2, 2, 100, 200), null, default);
            world.Stores.Enter(visitor, owner);
            world.Stores.Leave(Session(2, generation: 9));
        });
        Assert.Equal(owner, world.Stores.Visiting(visitor));
    }
}
