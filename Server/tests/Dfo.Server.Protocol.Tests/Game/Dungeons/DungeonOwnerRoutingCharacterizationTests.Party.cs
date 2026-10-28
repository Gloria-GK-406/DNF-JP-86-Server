using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.Dungeons;

// Party commands are owned by where the whole party stands, not only the caller: the caller, the
// target and every online member are asked, a Boss tower seat anywhere wins over an Illusion one,
// and only a party with no tower seat takes the ordinary flow.
public sealed partial class DungeonOwnerRoutingCharacterizationTests
{
    public static TheoryData<RoutingPlace> PartyPlaces => new() { RoutingPlace.Boss, RoutingPlace.Illusion, RoutingPlace.Town };

    [Theory]
    [MemberData(nameof(PartyPlaces))]
    public async Task A_member_leaving_from_town_is_owned_by_the_tower_the_leader_sits_in(RoutingPlace leaderPlace)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var (leader, member) = Party(world, leaderPlace, RoutingPlace.Town);

            await world.SendAsync(member, new LeavePartyRequest(GameMessageMetadata.Empty));

            Assert.DoesNotContain(world.PartyOf(leader).Members, value => value.CharacterId == member.CharacterId);
            Assert.NotEmpty(world.Projections.Projected);
            AssertNoTower(world);
            Assert.Empty(world.Units.Names);
            // Only a tower owner re-announces the tower host to the seated leader.
            if (leaderPlace == RoutingPlace.Town)
                Assert.Empty(world.Outbox.Frames);
            else
                Assert.Equal(new UdpHostNotification(0), Assert.Single(world.Outbox.To(leader)));
        });
    }

    [Theory]
    [MemberData(nameof(PartyPlaces))]
    public async Task Leadership_given_from_town_to_a_seated_member_is_owned_by_that_members_tower(RoutingPlace memberPlace)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var (leader, member) = Party(world, RoutingPlace.Town, memberPlace);

            await world.SendAsync(leader, new ChangePartyLeaderRequest(GameMessageMetadata.Empty, 1));

            Assert.Equal(member.WireUserId, world.PartyOf(leader).LeaderWireUserId);
            switch (memberPlace)
            {
                case RoutingPlace.Boss:
                    AssertOnly(world.Boss.Actions, "SynchronizeLeaderAsync", member.ConnectionId);
                    Assert.Empty(world.Illusion.Actions);
                    Assert.Equal(["party-synchronize-boss-leader"], world.Units.Names);
                    Assert.Equal(new UdpHostNotification(0), Assert.Single(world.Outbox.To(member)));
                    break;
                case RoutingPlace.Illusion:
                    AssertOnly(world.Illusion.Actions, "SynchronizeLeaderAsync", member.ConnectionId);
                    Assert.Empty(world.Boss.Actions);
                    Assert.Equal(["party-synchronize-illusion-leader"], world.Units.Names);
                    Assert.Equal(new UdpHostNotification(0), Assert.Single(world.Outbox.To(member)));
                    break;
                default:
                    AssertNoTower(world);
                    Assert.Empty(world.Units.Names);
                    Assert.Empty(world.Outbox.Frames);
                    break;
            }
        });
    }

    // The tower answers the departure "unchanged" here, so a tower owner refuses the removal. The
    // Illusion owner tells the leader with the native refusal; the Boss owner stays silent.
    [Theory]
    [MemberData(nameof(PartyPlaces))]
    public async Task Removing_a_seated_member_from_town_is_owned_by_that_members_tower(RoutingPlace memberPlace)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var (leader, member) = Party(world, RoutingPlace.Town, memberPlace);

            await world.SendAsync(leader, new RemovePartyMemberRequest(GameMessageMetadata.Empty, 1));

            var members = world.PartyOf(leader).Members.Select(value => value.CharacterId).ToArray();
            switch (memberPlace)
            {
                case RoutingPlace.Boss:
                    AssertOnly(world.Boss.Actions, "LeaveAsync", member.ConnectionId);
                    Assert.Empty(world.Illusion.Actions);
                    Assert.Equal(["party-leave-boss-tower"], world.Units.Names);
                    Assert.Contains(member.CharacterId, members);
                    Assert.Empty(world.Outbox.Frames);
                    break;
                case RoutingPlace.Illusion:
                    AssertOnly(world.Illusion.Actions, "LeaveAsync", member.ConnectionId);
                    Assert.Empty(world.Boss.Actions);
                    Assert.Equal(["party-leave-illusion-tower"], world.Units.Names);
                    Assert.Contains(member.CharacterId, members);
                    Assert.IsType<RemovePartyMemberRefusal>(Assert.Single(world.Outbox.To(leader)));
                    Assert.Single(world.Outbox.Frames);
                    break;
                default:
                    AssertNoTower(world);
                    Assert.DoesNotContain(member.CharacterId, members);
                    Assert.Empty(world.Outbox.Frames);
                    break;
            }
        });
    }

    // A refusal the owner predicts before any write: the Boss owner sends nothing, the Illusion
    // owner and the ordinary flow both send the native refusal.
    [Theory]
    [MemberData(nameof(PartyPlaces))]
    public async Task A_non_leaders_removal_is_refused_by_the_owner_of_the_whole_partys_place(RoutingPlace place)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var (leader, member) = Party(world, place, place);

            await world.SendAsync(member, new RemovePartyMemberRequest(GameMessageMetadata.Empty, 0));

            Assert.Equal(2, world.PartyOf(leader).Members.Length);
            AssertNoTower(world);
            Assert.Empty(world.Units.Names);
            if (place == RoutingPlace.Boss)
                Assert.Empty(world.Outbox.Frames);
            else
                Assert.IsType<RemovePartyMemberRefusal>(Assert.Single(world.Outbox.To(member)));
            Assert.Equal(place == RoutingPlace.Boss ? 0 : 1, world.Outbox.Frames.Count);
        });
    }

    [Theory]
    [MemberData(nameof(PartyPlaces))]
    public async Task Giving_up_from_town_is_owned_by_the_tower_a_member_sits_in(RoutingPlace memberPlace)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var (leader, member) = Party(world, RoutingPlace.Town, memberPlace);

            await world.SendAsync(leader, new GiveUpGameRequest(GameMessageMetadata.Empty));

            if (memberPlace == RoutingPlace.Town)
            {
                AssertNoTower(world);
                Assert.Empty(world.Units.Names);
                Assert.Equal(leader, Assert.Single(world.Views.Loaded));
                Assert.Equal(2, world.PartyOf(member).Members.Length);
                Assert.Empty(world.Outbox.Frames);
                return;
            }
            // The leader leaves the party; the seated successor becomes the tower leader.
            var actions = memberPlace == RoutingPlace.Boss ? world.Boss.Actions : world.Illusion.Actions;
            AssertOnly(actions, "SynchronizeLeaderAsync", member.ConnectionId);
            Assert.Empty(memberPlace == RoutingPlace.Boss ? world.Illusion.Actions : world.Boss.Actions);
            Assert.Empty(world.Views.Loaded);
            Assert.DoesNotContain(world.PartyOf(member).Members, value => value.CharacterId == leader.CharacterId);
            Assert.Equal(new UdpHostNotification(0), Assert.Single(world.Outbox.To(member)));
            Assert.Single(world.Outbox.Frames);
        });
    }

    [Fact]
    public async Task A_boss_seat_in_the_party_wins_over_an_illusion_seat()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var leader = world.Join(); var boss = world.Join(); var illusion = world.Join();
            world.FormParty(leader, boss); world.FormParty(leader, illusion);
            world.Boss.Enter(boss); world.Illusion.Enter(illusion);

            await world.SendAsync(leader, new ChangePartyLeaderRequest(GameMessageMetadata.Empty, 1));

            AssertOnly(world.Boss.Actions, "SynchronizeLeaderAsync", boss.ConnectionId);
            Assert.Empty(world.Illusion.Actions);
            Assert.Equal(["party-synchronize-boss-leader"], world.Units.Names);
            Assert.Equal(boss.WireUserId, world.PartyOf(leader).LeaderWireUserId);
        });
    }

    [Theory]
    [InlineData(0x000D)]
    [InlineData(0x000E)]
    [InlineData(0x0079)]
    public async Task Party_mutations_without_a_published_session_change_nothing(int type)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var (leader, member) = Party(world, RoutingPlace.Boss, RoutingPlace.Boss);

            await world.SendAsync(DungeonOwnerRoutingWorld.Unpublished, PartyMutation(type, GameMessageMetadata.Empty));

            world.AssertUntouched();
            Assert.Equal(2, world.PartyOf(leader).Members.Length);
            Assert.Equal(leader.WireUserId, world.PartyOf(member).LeaderWireUserId);
        });
    }

    public static TheoryData<RoutingPlace, bool, int> OlderGenerationPartySeats
    {
        get
        {
            var data = new TheoryData<RoutingPlace, bool, int>();
            foreach (var tower in new[] { RoutingPlace.Boss, RoutingPlace.Illusion })
                foreach (var callerSeated in new[] { true, false })
                    foreach (var type in new[] { 0x000D, 0x000E, 0x0079, 0x002A })
                        data.Add(tower, callerSeated, type);
            return data;
        }
    }

    // The whole party's place counts a member only through the session the tower recognises. When
    // the only tower seat in the party is held by an older generation of the leader (the caller)
    // or of the member, the party stands nowhere and the command takes the ordinary flow, exactly
    // as for a party standing in town.
    [Theory]
    [MemberData(nameof(OlderGenerationPartySeats))]
    public async Task A_party_whose_only_tower_seat_is_held_by_an_older_generation_takes_the_ordinary_flow(
        RoutingPlace tower, bool callerSeated, int type)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var (leader, member) = Party(world, RoutingPlace.Town, RoutingPlace.Town);
            if (callerSeated) { Seat(world, tower, leader); leader = world.Reselect(leader); }
            else { Seat(world, tower, member); member = world.Reselect(member); }
            Assert.NotNull(world.Participation.Find((callerSeated ? leader : member).CharacterId));

            await world.SendAsync(leader, type == 0x002A ? new GiveUpGameRequest(GameMessageMetadata.Empty) : PartyMutation(type, GameMessageMetadata.Empty));

            AssertNoTower(world);
            Assert.Empty(world.Units.Names);
            Assert.Empty(world.Outbox.Frames);
            switch (type)
            {
                case 0x000D:
                    Assert.DoesNotContain(world.PartyOf(member).Members, value => value.CharacterId == leader.CharacterId);
                    Assert.NotEmpty(world.Projections.Projected);
                    break;
                case 0x000E:
                    Assert.DoesNotContain(world.PartyOf(leader).Members, value => value.CharacterId == member.CharacterId);
                    Assert.NotEmpty(world.Projections.Projected);
                    break;
                case 0x0079:
                    Assert.Equal(member.WireUserId, world.PartyOf(leader).LeaderWireUserId);
                    Assert.NotEmpty(world.Projections.Projected);
                    break;
                default:
                    Assert.Equal(leader, Assert.Single(world.Views.Loaded));
                    Assert.Equal(2, world.PartyOf(leader).Members.Length);
                    break;
            }
        });
    }

    internal static Dfo.Server.Data.Networking.IClientNetworkMessage PartyMutation(int type, GameMessageMetadata metadata) => type switch
    {
        0x000D => new LeavePartyRequest(metadata),
        0x000E => new RemovePartyMemberRequest(metadata, 1),
        0x0079 => new ChangePartyLeaderRequest(metadata, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>A real two-member party: the leader in slot 0, the member in slot 1, each seated as asked.</summary>
    private static (SelectedSessionIdentity Leader, SelectedSessionIdentity Member) Party(DungeonOwnerRoutingWorld world,
        RoutingPlace leaderPlace, RoutingPlace memberPlace)
    {
        var leader = world.Join(); var member = world.Join();
        world.FormParty(leader, member);
        if (leaderPlace == memberPlace && leaderPlace != RoutingPlace.Town)
        {
            Seat(world, leaderPlace, leader, member);
            return (leader, member);
        }
        Seat(world, leaderPlace, leader);
        Seat(world, memberPlace, member);
        return (leader, member);
    }

    private static void Seat(DungeonOwnerRoutingWorld world, RoutingPlace place, params SelectedSessionIdentity[] members)
    {
        switch (place)
        {
            case RoutingPlace.Boss: world.Boss.Enter(members); break;
            case RoutingPlace.Illusion: world.Illusion.Enter(members); break;
            case RoutingPlace.Ordinary when members.Length == 2: world.EnterOrdinaryParty(members[0], members[1]); break;
            case RoutingPlace.Ordinary: world.EnterOrdinary(members[0]); break;
            case RoutingPlace.Town: break;
            default: throw new ArgumentOutOfRangeException(nameof(place));
        }
    }
}
