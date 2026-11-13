using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.PrivateStore;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.PrivateStore;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Xunit;

namespace Dfo.Server.Application.Tests.PrivateStore;

/// <summary>A visit belongs to the exact visitor and owner sessions, never to a later session of either character.</summary>
public sealed class PrivateStoreVisitTests
{
    private static readonly TownAreaKey Area = new(1, 2, 2);

    private static SelectedSessionIdentity Session(int character, int generation = 1) =>
        new(new AccountId(character), new CharacterId(character), new($"stall-{character}-{generation}"), generation, (ushort)character, 10011);

    private sealed class World
    {
        public HashSet<SelectedSessionIdentity> InWorld { get; } = [];
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
            var premium = TestRequired.Absent<IAccountPremiumServices>((method, _) =>
                method.Name == nameof(IAccountPremiumServices.IsActiveAsync) ? ValueTask.FromResult(true) : null);
            var premiums = TestRequired.Absent<IPremiumServiceDomainService>((method, _) =>
                method.Name == nameof(IPremiumServiceDomainService.For) ? premium : null);
            Stores = new PrivateStoreDomainService(new PrivateStoreRuleService(), null!, premiums, sessions, towns,
                null!, null!, null!, null!, null!, null!, null!, null!);
        }

        public SelectedSessionIdentity Join(SelectedSessionIdentity session)
        {
            InWorld.Add(session);
            return session;
        }
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
