using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.DungeonEntry;

public sealed class DungeonRunDeliveryRegistryTests
{
    [Fact]
    public void It_freezes_entry_generations_and_removes_the_run_with_its_last_participant()
    {
        TestGameplay.Run(() =>
        {
            var registry = new DungeonRunDeliveryRegistry();
            var runId = DungeonRunId.New();
            var leader = Session(1, "leader", 11);
            var member = Session(2, "member", 21);
            var source = new List<SelectedSessionIdentity> { leader, member };

            registry.Register(runId, source);
            source.Clear();

            Assert.Equal([leader, member], registry.Find(runId));
            Assert.True(registry.RemoveParticipant(runId, leader.CharacterId));
            Assert.Equal([member], registry.Find(runId));
            Assert.True(registry.RemoveParticipant(runId, member.CharacterId));
            Assert.Empty(registry.Find(runId));
        });
    }

    [Fact]
    public void Register_refuses_duplicate_character_generations()
    {
        TestGameplay.Run(() =>
        {
            var registry = new DungeonRunDeliveryRegistry();
            var runId = DungeonRunId.New();
            var first = Session(1, "first", 11);
            var replacement = first with
            {
                ConnectionId = new NetworkConnectionId("replacement"),
                ConnectionGeneration = 12,
            };

            Assert.Throws<ArgumentException>(() => registry.Register(runId, [first, replacement]));
            Assert.Empty(registry.Find(runId));
        });
    }

    [Fact]
    public void Participant_departure_sink_removes_only_the_named_run_participant()
    {
        TestGameplay.Run(() =>
        {
            var registry = new DungeonRunDeliveryRegistry();
            var runId = DungeonRunId.New();
            var leader = Session(1, "leader", 11);
            var member = Session(2, "member", 21);
            registry.Register(runId, [leader, member]);

            ((IDungeonRunParticipantDepartureSink)registry)
                .ParticipantDeparted(runId, leader.CharacterId);

            Assert.Equal([member], registry.Find(runId));
        });
    }

    private static SelectedSessionIdentity Session(int characterId, string connection, long generation) =>
        SelectedSessionIdentity.Create(
            new AccountId(characterId),
            new CharacterId(characterId),
            new NetworkConnectionId(connection),
            generation,
            10010);
}
