using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonEntryIntentDomainServiceTests
{
    private static readonly CharacterId Character = new(42);
    private static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
        new AccountId(1), Character, new NetworkConnectionId("one"), 1, 10010);

    [Fact]
    public void Cycle_preparation_does_not_consume_or_overwrite_a_newer_selection()
    {
        TestGameplay.Run(() =>
        {
            var service = new DungeonEntryIntentDomainService(ProtocolAlgorithmFixture.NoSaoFloors.Create());
            var cycle = DungeonEntryIntent.Cycle(84, 15203);
            service.Record(Session, DungeonEntryIntent.Interactive);
            Assert.Equal(DungeonEntryIntent.Interactive, service.Find(Session));
            Assert.Equal(DungeonEntryIntent.Interactive, service.Find(Session));
            Assert.True(service.TryReplaceExact(Session, DungeonEntryIntent.Interactive, cycle));
            Assert.False(service.TryReplaceExact(Session, DungeonEntryIntent.Interactive, cycle));
            var replacement = Session with { ConnectionGeneration = 2 };
            service.Record(replacement, DungeonEntryIntent.Interactive);
            Assert.Null(service.Find(Session));
            Assert.False(service.TryReplaceExact(Session, cycle, DungeonEntryIntent.Interactive));
            Assert.Equal(DungeonEntryIntent.Interactive, service.Consume(replacement));
            Assert.False(service.TryReplaceExact(replacement, DungeonEntryIntent.Interactive, cycle));
        });
    }

    [Fact]
    public void Authorizing_consumes_nothing_and_binds_a_directed_selection_to_its_dungeon()
    {
        TestGameplay.Run(() =>
        {
            var service = new DungeonEntryIntentDomainService(ProtocolAlgorithmFixture.NoSaoFloors.Create());
            Assert.Null(service.Authorize(Session, 84, mirror: false));
            var directed = DungeonEntryIntent.Directed(84);
            service.Record(Session, directed);
            Assert.Equal(directed, service.Authorize(Session, 84, mirror: false));
            Assert.Null(service.Authorize(Session, 85, mirror: false));
            Assert.Equal(directed, service.Authorize(Session, 85, mirror: true));
            Assert.Equal(directed, service.Find(Session));
            service.Record(Session, DungeonEntryIntent.Interactive);
            Assert.Equal(DungeonEntryIntent.Interactive, service.Authorize(Session, 85, mirror: false));
            Assert.Null(service.Authorize(Session with { ConnectionGeneration = 2 }, 85, mirror: false));
        });
    }

    [Fact]
    public void Refused_intent_can_be_restored_but_cannot_replace_newer_selection()
    {
        TestGameplay.Run(() =>
        {
            var service = new DungeonEntryIntentDomainService(ProtocolAlgorithmFixture.NoSaoFloors.Create());
            service.Record(Session, DungeonEntryIntent.Interactive);
            var attempted = service.Consume(Session)!.Value;
            service.RestoreIfAbsent(Session, attempted);
            Assert.Equal(attempted, service.Consume(Session));
            service.Record(Session, DungeonEntryIntent.Directed(144));
            service.RestoreIfAbsent(Session, attempted);
            Assert.Equal(DungeonEntryIntent.Directed(144), service.Consume(Session));
            var replacement = Session with { ConnectionGeneration = Session.ConnectionGeneration + 1 };
            service.Record(replacement, DungeonEntryIntent.Directed(150));
            service.RestoreIfAbsent(Session, attempted);
            Assert.Null(service.Consume(Session));
            Assert.Equal(DungeonEntryIntent.Directed(150), service.Consume(replacement));
        });
    }

    [Fact]
    public void AnIntentIsConsumedExactlyOnce()
    {
        TestGameplay.Run(() =>
        {
            var service = new DungeonEntryIntentDomainService(ProtocolAlgorithmFixture.NoSaoFloors.Create());
            service.Record(Session, DungeonEntryIntent.Directed(144));

            Assert.Equal(DungeonEntryIntent.Directed(144), service.Consume(Session));
            Assert.Null(service.Consume(Session));
        });
    }

    [Fact]
    public void ANewSelectionReplacesThePreviousUnconsumedIntent()
    {
        TestGameplay.Run(() =>
        {
            var service = new DungeonEntryIntentDomainService(ProtocolAlgorithmFixture.NoSaoFloors.Create());
            service.Record(Session, DungeonEntryIntent.Directed(144));
            service.Record(Session, DungeonEntryIntent.Interactive);

            Assert.Equal(DungeonEntryIntent.Interactive, service.Consume(Session));
        });
    }

    [Fact]
    public void AReplacementSessionCannotConsumeThePreviousSessionsIntent()
    {
        TestGameplay.Run(() =>
        {
            var service = new DungeonEntryIntentDomainService(ProtocolAlgorithmFixture.NoSaoFloors.Create());
            var replacement = SelectedSessionIdentity.Create(
                Session.AccountId,
                Character,
                new NetworkConnectionId("two"),
                connectionGeneration: 2,
                listenerPort: 10010);
            service.Record(Session, DungeonEntryIntent.Directed(144));

            Assert.Null(service.Consume(replacement));
            Assert.Equal(DungeonEntryIntent.Directed(144), service.Consume(Session));
        });
    }

    [Fact]
    public void AReplacementSelectionOverwritesThePreviousSessionsIntent()
    {
        TestGameplay.Run(() =>
        {
            var service = new DungeonEntryIntentDomainService(ProtocolAlgorithmFixture.NoSaoFloors.Create());
            var replacement = SelectedSessionIdentity.Create(
                Session.AccountId,
                Character,
                new NetworkConnectionId("two"),
                connectionGeneration: 2,
                listenerPort: 10010);
            service.Record(Session, DungeonEntryIntent.Directed(144));
            service.Record(replacement, DungeonEntryIntent.Interactive);

            Assert.Null(service.Consume(Session));
            Assert.Equal(DungeonEntryIntent.Interactive, service.Consume(replacement));
        });
    }
}
