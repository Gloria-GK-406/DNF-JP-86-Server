using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.Dungeons;

/// <summary>
/// Pins, from each command handler's public entry, which owner handles a fresh dungeon-owned
/// request in each place its requester can stand: Boss tower, Illusion tower, or the ordinary
/// flow (town, ordinary run, or a tower seat held only by an older session generation).
/// </summary>
/// <remarks>
/// A tower owner shows as the action asked of that tower and the unit it wrote; the ordinary flow
/// shows as its own unit, frame or current-view read. Ordinary-run outcomes themselves are pinned
/// by the per-command handler tests; here the ordinary column is the town and the stale seat.
/// </remarks>
[Trait("Category", "Unit")]
public sealed partial class DungeonOwnerRoutingCharacterizationTests
{
    // 0x0027 carrying an object code: an object destruction report rather than a kill.
    private const int ObjectDestruction = 0x10027;

    public static TheoryData<RoutingPlace> FreshPlaces => new() { RoutingPlace.NoSession, RoutingPlace.Boss, RoutingPlace.Illusion, RoutingPlace.Town };

    [Theory]
    [MemberData(nameof(FreshPlaces))]
    public async Task Pickup_is_owned_by_the_requesters_place(RoutingPlace place)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var connection = world.PlaceConnection(place);

            await world.SendAsync(connection, new GetItemRequest(GameMessageMetadata.Empty, 3));

            switch (place)
            {
                case RoutingPlace.NoSession: world.AssertUntouched(); break;
                case RoutingPlace.Boss:
                    AssertOnly(world.Boss.Actions, "PickupAsync", connection);
                    Assert.Empty(world.Illusion.Actions);
                    Assert.Equal(["pick-up-boss-tower-item"], world.Units.Names);
                    Assert.Equal(new GetItemResponse(false), Assert.Single(world.Outbox.To(connection)));
                    break;
                case RoutingPlace.Illusion:
                    AssertOnly(world.Illusion.Actions, "PickupAsync", connection);
                    Assert.Empty(world.Boss.Actions);
                    Assert.Equal(["pick-up-illusion-tower-item"], world.Units.Names);
                    Assert.Equal(new GetItemResponse(false), Assert.Single(world.Outbox.To(connection)));
                    break;
                default:
                    AssertNoTower(world);
                    Assert.Equal(["pick-up-ground-drop"], world.Units.Names);
                    Assert.Empty(world.Outbox.Frames);
                    break;
            }
        });
    }

    [Theory]
    [MemberData(nameof(FreshPlaces))]
    public async Task Character_death_is_owned_by_the_requesters_place(RoutingPlace place)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var connection = world.PlaceConnection(place);

            await world.SendAsync(connection, new CharacterDeathRequest(GameMessageMetadata.Empty, 612, 294, 0));

            AssertTowerOrNothing(world, place, connection, "DieAsync", "die-in-boss-tower", "die-in-illusion-tower");
            Assert.Empty(world.Outbox.Frames);
        });
    }

    [Theory]
    [MemberData(nameof(FreshPlaces))]
    public async Task Monster_death_is_owned_by_the_requesters_place(RoutingPlace place)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var connection = world.PlaceConnection(place);

            await world.SendAsync(connection, new MonsterDeathRequest(GameMessageMetadata.Empty, 7, 1));

            if (place == RoutingPlace.Town)
            {
                AssertNoTower(world);
                Assert.Empty(world.Units.Names);
                Assert.Equal(MonsterDeathNotification.Empty(7), Assert.Single(world.Outbox.To(connection)));
                return;
            }
            AssertTowerOrNothing(world, place, connection, "KillAsync", "kill-boss-tower-actor", "kill-illusion-tower-actor");
            Assert.Empty(world.Outbox.Frames);
        });
    }

    // Object destruction never enters the Illusion owner: the ordinary flow answers an empty
    // acknowledgement there, as in town, while a Boss tower seat swallows the report.
    [Theory]
    [MemberData(nameof(FreshPlaces))]
    public async Task Object_destruction_is_ignored_in_a_boss_tower_and_otherwise_acknowledged_empty(RoutingPlace place)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var connection = world.PlaceConnection(place);

            await world.SendAsync(connection, new MonsterDeathRequest(GameMessageMetadata.Empty, 7, 1) { ObjectCode = DungeonOwnerRoutingWorld.ObjectCode });

            AssertNoTower(world);
            Assert.Empty(world.Units.Names);
            if (place is RoutingPlace.Illusion or RoutingPlace.Town)
                Assert.Equal(MonsterDeathNotification.Empty(7), Assert.Single(world.Outbox.To(connection)));
            else
                Assert.Empty(world.Outbox.Frames);
        });
    }

    [Theory]
    [MemberData(nameof(FreshPlaces))]
    public async Task Finish_loading_is_owned_by_the_requesters_place(RoutingPlace place)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var connection = world.PlaceConnection(place);

            await world.SendAsync(connection, new FinishLoadingRequest(GameMessageMetadata.Empty, 0, 0));

            if (place == RoutingPlace.Town)
            {
                AssertNoTower(world);
                Assert.Empty(world.Units.Names);
                Assert.IsType<LoadingCompleteNotification>(Assert.Single(world.Outbox.To(connection)));
                return;
            }
            AssertTowerOrNothing(world, place, connection, "LoadAsync", "load-boss-tower", "load-illusion-tower");
            Assert.Empty(world.Outbox.Frames);
        });
    }

    [Theory]
    [MemberData(nameof(FreshPlaces))]
    public async Task Move_map_is_owned_by_the_requesters_place(RoutingPlace place)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var connection = world.PlaceConnection(place);

            await world.SendAsync(connection, new MoveMapRequest(GameMessageMetadata.Empty, 1, 0, 0, 0));

            AssertTowerOrNothing(world, place, connection, "NextLevelAsync", "advance-boss-tower", "advance-illusion-tower");
            Assert.Empty(world.Outbox.Frames);
        });
    }

    [Theory]
    [MemberData(nameof(FreshPlaces))]
    public async Task Eplp_is_owned_by_the_requesters_place(RoutingPlace place)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var connection = world.PlaceConnection(place);

            await world.SendAsync(connection, new EplpCommandRequest(GameMessageMetadata.Empty, EplpCommandHandler.LeaveState, 0));

            AssertTownFlowOrTower(world, place, connection, ("SelectResultAsync", "select-boss-tower-result"),
                ("SelectReturnAsync", "select-illusion-tower-return"));
            Assert.Empty(world.Outbox.Frames);
        });
    }

    [Theory]
    [MemberData(nameof(FreshPlaces))]
    public async Task Back_to_village_is_owned_by_the_requesters_place(RoutingPlace place)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var connection = world.PlaceConnection(place);

            await world.SendAsync(connection, new BackToVillageRequest(GameMessageMetadata.Empty));

            AssertTownFlowOrTower(world, place, connection, ("LeaveAsync", "party-leave-boss-tower"),
                ("LeaveAsync", "party-leave-illusion-tower"));
            Assert.Empty(world.Outbox.Frames);
        });
    }

    [Theory]
    [MemberData(nameof(FreshPlaces))]
    public async Task Give_up_alone_is_owned_by_the_requesters_place(RoutingPlace place)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var connection = world.PlaceConnection(place);

            await world.SendAsync(connection, new GiveUpGameRequest(GameMessageMetadata.Empty));

            AssertTownFlowOrTower(world, place, connection, ("LeaveAsync", "party-leave-boss-tower"),
                ("LeaveAsync", "party-leave-illusion-tower"));
            Assert.Empty(world.Outbox.Frames);
        });
    }

    // Selection has a Boss owner only: an Illusion tower seat goes to the ordinary selection.
    [Theory]
    [MemberData(nameof(FreshPlaces))]
    public async Task Enter_selection_has_only_a_boss_owner(RoutingPlace place)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var connection = world.PlaceConnection(place);

            await world.SendAsync(connection, new EnterSelectDungeonRequest(GameMessageMetadata.Empty, 3505));

            Assert.Empty(world.Illusion.Actions);
            Assert.Empty(world.Outbox.Frames);
            switch (place)
            {
                case RoutingPlace.NoSession: world.AssertUntouched(); break;
                case RoutingPlace.Boss:
                    AssertOnly(world.Boss.Actions, "ReleaseForSelectionAsync", connection);
                    Assert.Equal(["selection-release-boss-tower"], world.Units.Names);
                    // The tower kept its run, so the selection ends with the Boss owner.
                    Assert.Empty(world.Views.Loaded);
                    break;
                default:
                    Assert.Empty(world.Boss.Actions);
                    Assert.Empty(world.Units.Names);
                    Assert.Equal(connection, Assert.Single(world.Views.Loaded).ConnectionId);
                    break;
            }
        });
    }

    [Fact]
    public async Task Enter_selection_continues_into_the_ordinary_selection_once_the_boss_run_is_released()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld { Boss = { ReleasesForSelection = true } };
            var session = world.Place(RoutingPlace.Boss);

            await world.SendAsync(session, new EnterSelectDungeonRequest(GameMessageMetadata.Empty, 3505));

            AssertOnly(world.Boss.Actions, "ReleaseForSelectionAsync", session.ConnectionId);
            Assert.Equal(["selection-release-boss-tower"], world.Units.Names);
            Assert.Null(world.Boss.Find(session));
            Assert.Equal(session, Assert.Single(world.Views.Loaded));
        });
    }

    // Revival has a Boss owner only, which refuses; every other place goes to the ordinary flow,
    // which is silent without an ordinary run.
    [Theory]
    [MemberData(nameof(FreshPlaces))]
    public async Task Revival_coin_is_refused_only_by_a_boss_tower(RoutingPlace place)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var connection = world.PlaceConnection(place);

            await world.SendAsync(connection, new UseRevivalCoinRequest(GameMessageMetadata.Empty, 2, 1));

            AssertNoTower(world);
            Assert.Empty(world.Units.Names);
            if (place == RoutingPlace.Boss)
                Assert.Equal(new UseRevivalCoinResponse(false, 2, 1), Assert.Single(world.Outbox.To(connection)));
            else
                Assert.Empty(world.Outbox.Frames);
        });
    }

    // A tower still seats the character's previous generation while a new generation of the
    // same character is published: the occupancy registry names the tower, but the tower does not
    // recognise the requesting session, so the command takes the ordinary flow, as in town.
    public static TheoryData<RoutingPlace, int> OlderGenerationSeats
    {
        get
        {
            var data = new TheoryData<RoutingPlace, int>();
            foreach (var tower in new[] { RoutingPlace.Boss, RoutingPlace.Illusion })
                foreach (var type in new[] { 0x002B, 0x0028, 0x0027, ObjectDestruction, 0x0025, 0x002D, 0x0029, 0x0048, 0x0084, 0x000F, 0x002A })
                    data.Add(tower, type);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(OlderGenerationSeats))]
    public async Task A_tower_seat_held_by_an_older_generation_routes_the_new_generation_to_the_ordinary_flow(RoutingPlace tower, int type)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var old = world.Place(tower);
            var current = world.Reselect(old);
            Assert.Equal(old.ConnectionId, current.ConnectionId);
            Assert.NotEqual(old, current);
            Assert.NotNull(world.Participation.Find(current.CharacterId));

            await world.SendAsync(current, FreshRequest(type));

            AssertNoTower(world);
            Assert.Equal(type == 0x002B ? ["pick-up-ground-drop"] : [], world.Units.Names);
            Assert.Equal(world.Outbox.Frames.Count, world.Outbox.To(current).Count);
            switch (type)
            {
                case 0x0027 or ObjectDestruction:
                    Assert.Equal(MonsterDeathNotification.Empty(7), Assert.Single(world.Outbox.Frames).Message);
                    break;
                case 0x0025:
                    Assert.IsType<LoadingCompleteNotification>(Assert.Single(world.Outbox.Frames).Message);
                    break;
                default:
                    Assert.Empty(world.Outbox.Frames);
                    break;
            }
            if (type is 0x0048 or 0x0084 or 0x000F or 0x002A)
                Assert.Equal(current, Assert.Single(world.Views.Loaded));
            else
                Assert.Empty(world.Views.Loaded);
        });
    }

    private static IClientNetworkMessage FreshRequest(int type) => type switch
    {
        0x002B => new GetItemRequest(GameMessageMetadata.Empty, 3),
        0x0028 => new CharacterDeathRequest(GameMessageMetadata.Empty, 612, 294, 0),
        0x0027 => new MonsterDeathRequest(GameMessageMetadata.Empty, 7, 1),
        ObjectDestruction => new MonsterDeathRequest(GameMessageMetadata.Empty, 7, 1) { ObjectCode = DungeonOwnerRoutingWorld.ObjectCode },
        0x0025 => new FinishLoadingRequest(GameMessageMetadata.Empty, 0, 0),
        0x002D => new MoveMapRequest(GameMessageMetadata.Empty, 1, 0, 0, 0),
        0x0029 => new UseRevivalCoinRequest(GameMessageMetadata.Empty, 2, 1),
        0x0048 => new EplpCommandRequest(GameMessageMetadata.Empty, EplpCommandHandler.LeaveState, 0),
        0x0084 => new BackToVillageRequest(GameMessageMetadata.Empty),
        0x000F => new EnterSelectDungeonRequest(GameMessageMetadata.Empty, 3505),
        0x002A => new GiveUpGameRequest(GameMessageMetadata.Empty),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    // An ordinary registration can outlive the run being findable by character (the party was
    // left, the run not yet ended for this character): the ordinary flow answers as in town.
    [Fact]
    public async Task An_ordinary_registration_without_a_findable_run_is_answered_by_the_ordinary_flow()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var session = world.Place(RoutingPlace.Town);
            world.Participation.Residual[session.CharacterId] = new(Dfo.Server.Abstractions.World.DungeonParticipationMode.Ordinary, Guid.NewGuid());

            await world.SendAsync(session, new MonsterDeathRequest(GameMessageMetadata.Empty, 7, 1));
            await world.SendAsync(session, new FinishLoadingRequest(GameMessageMetadata.Empty, 0, 0));

            AssertNoTower(world);
            Assert.Collection(world.Outbox.To(session),
                message => Assert.Equal(MonsterDeathNotification.Empty(7), message),
                message => Assert.IsType<LoadingCompleteNotification>(message));
        });
    }

    private static void AssertOnly(IReadOnlyList<DungeonOwnerRoutingWorld.TowerAction> actions, string name, NetworkConnectionId connection)
    {
        var action = Assert.Single(actions);
        Assert.Equal(name, action.Name);
        Assert.Equal(connection, action.Session.ConnectionId);
    }

    private static void AssertNoTower(DungeonOwnerRoutingWorld world)
    {
        Assert.Empty(world.Boss.Actions);
        Assert.Empty(world.Illusion.Actions);
    }

    // For commands whose ordinary flow does nothing observable without an ordinary run.
    private static void AssertTowerOrNothing(DungeonOwnerRoutingWorld world, RoutingPlace place, NetworkConnectionId connection,
        string action, string bossUnit, string illusionUnit)
    {
        switch (place)
        {
            case RoutingPlace.Boss:
                AssertOnly(world.Boss.Actions, action, connection);
                Assert.Empty(world.Illusion.Actions);
                Assert.Equal([bossUnit], world.Units.Names);
                break;
            case RoutingPlace.Illusion:
                AssertOnly(world.Illusion.Actions, action, connection);
                Assert.Empty(world.Boss.Actions);
                Assert.Equal([illusionUnit], world.Units.Names);
                break;
            default:
                AssertNoTower(world);
                Assert.Empty(world.Units.Names);
                break;
        }
    }

    // For commands whose ordinary flow first loads the requester's current view.
    private static void AssertTownFlowOrTower(DungeonOwnerRoutingWorld world, RoutingPlace place, NetworkConnectionId connection,
        (string Action, string Unit) boss, (string Action, string Unit) illusion)
    {
        switch (place)
        {
            case RoutingPlace.NoSession: world.AssertUntouched(); break;
            case RoutingPlace.Boss:
                AssertOnly(world.Boss.Actions, boss.Action, connection);
                Assert.Empty(world.Illusion.Actions);
                Assert.Equal([boss.Unit], world.Units.Names);
                Assert.Empty(world.Views.Loaded);
                break;
            case RoutingPlace.Illusion:
                AssertOnly(world.Illusion.Actions, illusion.Action, connection);
                Assert.Empty(world.Boss.Actions);
                Assert.Equal([illusion.Unit], world.Units.Names);
                Assert.Empty(world.Views.Loaded);
                break;
            default:
                AssertNoTower(world);
                Assert.Empty(world.Units.Names);
                Assert.Equal(connection, Assert.Single(world.Views.Loaded).ConnectionId);
                break;
        }
    }
}
