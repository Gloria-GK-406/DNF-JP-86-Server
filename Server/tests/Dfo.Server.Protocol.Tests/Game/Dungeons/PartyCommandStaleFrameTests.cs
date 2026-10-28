using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.Dungeons;

/// <summary>
/// A stale party command (give up, leave, remove, change leader) is dropped wherever the whole
/// party stands, including where only the place of the whole party makes it a tower request:
/// nothing is written, no tower is asked, no current view is read and nothing is registered.
/// </summary>
/// <remarks>
/// These places already dropped a stale party command at its entry; this pins that the whole
/// party's place is never reached first. Party mutations with the whole party seated together,
/// and give up from a lone requester, are pinned by the characterization tests.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class PartyCommandStaleFrameTests
{
    public static TheoryData<int, RoutingPlace> StaleFrames
    {
        get
        {
            var data = new TheoryData<int, RoutingPlace>();
            foreach (var type in new[] { 0x000D, 0x000E, 0x0079 })
                foreach (var place in new[] { RoutingPlace.Boss, RoutingPlace.Illusion })
                    data.Add(type, place);
            foreach (var place in new[] { RoutingPlace.Town, RoutingPlace.Ordinary, RoutingPlace.Boss, RoutingPlace.Illusion })
                data.Add(0x002A, place);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(StaleFrames))]
    public async Task A_stale_party_command_is_dropped_wherever_the_whole_party_stands(int type, RoutingPlace partyPlace)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var (leader, member) = Party(world, partyPlace);
            var before = (Snapshot(world, leader), Snapshot(world, member));

            var metadata = DungeonOwnerRoutingWorld.Stale((ushort)type);
            await world.SendAsync(leader, type == 0x002A
                ? new GiveUpGameRequest(metadata)
                : DungeonOwnerRoutingCharacterizationTests.PartyMutation(type, metadata));

            world.AssertUntouched();
            Assert.Empty(world.Projections.Projected);
            Assert.Equal(before, (Snapshot(world, leader), Snapshot(world, member)));
        });
    }

    /// <summary>
    /// A real two-member party with the leader, who sends the request, in slot 0. In a tower only
    /// the member is seated, so the tower is the whole party's place and not the leader's own.
    /// </summary>
    private static (SelectedSessionIdentity Leader, SelectedSessionIdentity Member) Party(DungeonOwnerRoutingWorld world, RoutingPlace place)
    {
        var leader = world.Join(); var member = world.Join();
        world.FormParty(leader, member);
        switch (place)
        {
            case RoutingPlace.Boss: world.Boss.Enter(member); break;
            case RoutingPlace.Illusion: world.Illusion.Enter(member); break;
            case RoutingPlace.Ordinary: world.EnterOrdinaryParty(leader, member); break;
            case RoutingPlace.Town: break;
            default: throw new ArgumentOutOfRangeException(nameof(place));
        }
        return (leader, member);
    }

    /// <summary>What a dropped request must leave as it was: the party, the ordinary run, and both towers' runs.</summary>
    private static string Snapshot(DungeonOwnerRoutingWorld world, SelectedSessionIdentity session)
    {
        var party = world.PartyOf(session);
        var run = world.Runs.Find(session.CharacterId);
        return string.Join('|',
            $"party={party.LeaderWireUserId}:{string.Join(',', party.Members.Select(value => value.CharacterId.Value))}",
            run is null ? "run=none" : $"run={run.RunId.Value}:{run.RoomVersion}:{run.DeadParticipants.Count}:{run.ReadyParticipants.Count}",
            $"boss={world.Boss.Find(session)?.RunId}", $"illusion={world.Illusion.Find(session)?.RunId}");
    }
}
