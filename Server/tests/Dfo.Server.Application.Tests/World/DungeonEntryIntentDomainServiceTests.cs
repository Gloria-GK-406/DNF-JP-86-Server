using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonEntryIntentDomainServiceTests
{
    private static readonly CharacterId Character = new(42);
    private static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
        new AccountId(1), Character, new NetworkConnectionId("one"), 1, 10010);

    [Fact]
    public void Refused_intent_can_be_restored_but_cannot_replace_newer_selection()
    {
        var service = new DungeonEntryIntentDomainService();
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
    }

    [Fact]
    public void AnIntentIsConsumedExactlyOnce()
    {
        var service = new DungeonEntryIntentDomainService();
        service.Record(Session, DungeonEntryIntent.Directed(144));

        Assert.Equal(DungeonEntryIntent.Directed(144), service.Consume(Session));
        Assert.Null(service.Consume(Session));
    }

    [Fact]
    public void ANewSelectionReplacesThePreviousUnconsumedIntent()
    {
        var service = new DungeonEntryIntentDomainService();
        service.Record(Session, DungeonEntryIntent.Directed(144));
        service.Record(Session, DungeonEntryIntent.Interactive);

        Assert.Equal(DungeonEntryIntent.Interactive, service.Consume(Session));
    }

    [Fact]
    public void AReplacementSessionCannotConsumeThePreviousSessionsIntent()
    {
        var service = new DungeonEntryIntentDomainService();
        var replacement = SelectedSessionIdentity.Create(
            Session.AccountId,
            Character,
            new NetworkConnectionId("two"),
            connectionGeneration: 2,
            listenerPort: 10010);
        service.Record(Session, DungeonEntryIntent.Directed(144));

        Assert.Null(service.Consume(replacement));
        Assert.Equal(DungeonEntryIntent.Directed(144), service.Consume(Session));
    }

    [Fact]
    public void AReplacementSelectionOverwritesThePreviousSessionsIntent()
    {
        var service = new DungeonEntryIntentDomainService();
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
    }
}
