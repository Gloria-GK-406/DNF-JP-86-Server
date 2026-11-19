using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.AntonRaid;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.AntonRaid;

public sealed class AntonRaidRewardPhaseSubscriberTests
{
    [Fact]
    public Task Rollback_discards_the_dispatch_and_commit_sends_the_result_outside_the_write_unit() => TestGameplay.RunAsync(async () =>
    {
        var sessions = TestSessions.Directory(out var registry);
        var member = sessions.InWorld(registry, new(1), new(1), "raid-result", 10200);
        var now = DateTimeOffset.UtcNow;
        var outcome = new AntonRaidPhaseOutcome(Guid.NewGuid(), 1, 0, true, 0, now.AddMinutes(-5), now,
            [new(member.AccountId, member.CharacterId, 1, 0)], [member.CharacterId]);
        var envelope = new DomainEventEnvelope<AntonRaidPhaseConcluded>(Guid.NewGuid(), Guid.NewGuid(), now, Guid.NewGuid(),
            new AntonRaidPhaseConcluded(outcome));
        var transactions = TestRequired.TransactionScope();
        var effects = new WireEffects(transactions);
        var starts = 0;
        var settlements = TestRequired.Absent<IAntonRaidRewardSettlementDomainService>((method, args) =>
        {
            if (method.Name != nameof(IAntonRaidRewardSettlementDomainService.Begin))
                return null;
            Assert.Null(transactions.Current);
            starts++;
            return new AntonRaidRewardSnapshot((AntonRaidRewardSettlementSeed)args![0]!, 2, AntonRaidRewardStage.Result, []);
        });
        var subscriber = new AntonRaidPhaseRewardSubscriber(settlements, new FixedBridge(member, outcome.Instance),
            sessions, effects, transactions, TimeProvider.System);

        await using (var rolledBack = transactions.Begin())
        {
            await subscriber.HandleAsync(envelope, default);
            Assert.Empty(effects.Types);
        }
        Assert.Equal(0, starts);

        await using (var committed = transactions.Begin())
        {
            await subscriber.HandleAsync(envelope, default);
            Assert.Empty(effects.Types);
            await committed.CommitAsync(default);
        }
        Assert.Equal(1, starts);
        Assert.Equal([ServerMessageType.GameAntonRaidResult, ServerMessageType.GameAntonRaidState], effects.Types);
    });

    private sealed class FixedBridge(SelectedSessionIdentity member, Guid instance) : IAntonRaidRewardPhaseBridge
    {
        public (Guid Instance, byte Phase)? Locate(SelectedSessionIdentity session) => session == member ? (instance, 0) : null;
        public ValueTask<bool> CompleteAsync(CommandExecutionContext execution, AntonRaidRewardSnapshot snapshot, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class WireEffects(IDomainTransactionAccessor transactions) : IEventService
    {
        public List<ServerMessageType> Types { get; } = [];

        public ValueTask<NotificationDelivery> ProjectAsync<T>(NotificationAudience audience, T notification, CancellationToken ct)
            where T : class, INotification<T>
        {
            Assert.Null(transactions.Current);
            Assert.IsType<NotificationAudience.TheseSessions>(audience);
            Types.AddRange(Assert.IsType<AntonRaidRuntimeChanged>(notification).Messages.Select(message => message.MessageType));
            return ValueTask.FromResult(new NotificationDelivery([]));
        }

        public ValueTask<Guid> EnqueueAsync<T>(T payload, CancellationToken cancellationToken = default)
            where T : class, IDomainEvent => throw new NotSupportedException();

        public ValueTask DirectAsync<T>(T payload, CancellationToken cancellationToken = default)
            where T : class, IDomainEvent => throw new NotSupportedException();
    }
}
