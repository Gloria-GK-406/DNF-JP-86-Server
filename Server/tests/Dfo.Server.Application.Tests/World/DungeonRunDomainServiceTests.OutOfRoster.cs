using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// Issue #202 AC-009: a character in a run's party who did not enter the run is not one of its members.
/// </summary>
/// <remarks>
/// The protocol commands refuse to grow a running party; the party service has no such rule, so the
/// state is built here directly against the real party service the run reads.
/// </remarks>
public sealed partial class DungeonRunDomainServiceTests
{
    private static readonly PartyCharacterSnapshot OutOfRoster = new(
        new AccountId(404), new CharacterId(4), 4, new PartyDirectoryScope(11, 1, 1));

    [Fact]
    public void A_character_put_into_a_running_party_has_no_part_in_its_run()
    {
        TestGameplay.Run(() =>
        {
            var runs = TestParties.Runs();
            var parties = TestParties.Of(runs);
            var run = BeginPartyWithCell(runs, [DepartureLeader, DepartureMember], groundItems: [DeclaredItem(1)]);
            var leader = new PartyCharacterSnapshot(DepartureLeader.AccountId, DepartureLeader.CharacterId,
                (ushort)DepartureLeader.CharacterId.Value, OutOfRoster.DirectoryScope);
            Assert.True(parties.RequestInvite(leader, OutOfRoster, new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(OutOfRoster, leader).Succeeded);
            Assert.Equal(run.PartyKey, parties.KeyOf(OutOfRoster.CharacterId));
            Assert.Equal(3, parties.FindByKey(run.PartyKey)!.Members.Length);

            AssertOnlyEntrantsAreMembers();
            Assert.Null(runs.Find(OutOfRoster.CharacterId));

            var next = new MazeCell(1, 1);
            Assert.Null(runs.TryMove(OutOfRoster.CharacterId, next));
            Assert.Null(runs.TryMove(OutOfRoster.CharacterId, run.RunId, run.RoomVersion, next,
                CellSeed(next, Tutorial.Cells[next].MapCandidates[0])));
            Assert.Null(runs.TryClaimGroundDrop(OutOfRoster.CharacterId, run.RunId, run.RoomVersion, 1));
            var untouched = Assert.IsType<DungeonRunState>(runs.Find(run.RunId));
            Assert.Equal(run.EntryRevision, untouched.EntryRevision);
            Assert.Equal(run.CurrentCell, untouched.CurrentCell);
            Assert.Equal(run.RoomVersion, untouched.RoomVersion);
            Assert.Single(untouched.CurrentCellState.GroundItems);

            // A member's pickup is published to the members only.
            var pickup = Assert.IsType<DungeonGroundDropClaim>(
                runs.TryClaimGroundDrop(DepartureLeader.CharacterId, run.RunId, run.RoomVersion, 1));
            var picked = Assert.IsType<DungeonPickupChange>(runs.CommitGroundDrop(pickup, DungeonPickupKind.Item));
            Assert.Equal([DepartureLeader.CharacterId, DepartureMember.CharacterId], picked.Recipients);

            var cleared = runs.Find(run.RunId)!;
            Assert.NotNull(runs.TryOpenClear(DepartureLeader.CharacterId, cleared.RunId, cleared.RoomVersion));
            Assert.Null(runs.TryClaimSettlement(OutOfRoster.CharacterId, run.RunId));
            Assert.Null(runs.TryClaimSettlement(OutOfRoster.CharacterId));
            Assert.Null(runs.TrySettle(OutOfRoster.CharacterId));
            foreach (var member in new[] { DepartureLeader, DepartureMember })
                Assert.False(runs.Find(member.CharacterId)!.IsSettled);
            // The settlement the outsider was refused is open to a member.
            Assert.NotNull(runs.TryClaimSettlement(DepartureMember.CharacterId, run.RunId));

            AssertOnlyEntrantsAreMembers();
            Assert.Null(runs.Find(OutOfRoster.CharacterId));

            void AssertOnlyEntrantsAreMembers()
            {
                var views = new[] { DepartureLeader, DepartureMember }
                    .Select(member => Assert.IsType<DungeonRunState>(runs.Find(member.CharacterId)))
                    .Append(Assert.IsType<DungeonRunState>(runs.Find(run.RunId)));
                foreach (var view in views)
                {
                    Assert.Equal(run.RunId, view.RunId);
                    Assert.Equal([DepartureLeader.CharacterId, DepartureMember.CharacterId],
                        view.Participants.Select(participant => participant.CharacterId));
                    Assert.Equal(DepartureLeader.CharacterId, Assert.Single(view.Participants, p => p.IsLeader).CharacterId);
                }
            }
        });
    }
}
