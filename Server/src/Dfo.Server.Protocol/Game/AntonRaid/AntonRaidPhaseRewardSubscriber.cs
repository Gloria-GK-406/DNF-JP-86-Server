using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.AntonRaid;

internal sealed class AntonRaidPhaseRewardSubscriber(IAntonRaidRewardSettlementDomainService settlements,
    IAntonRaidDomainService raids, ISelectedSessionDomainService sessions,
    IEventService events, IDomainTransactionAccessor transactions, TimeProvider clock)
    : IDomainEventSubscriber<AntonRaidPhaseConcluded>
{
    public ValueTask HandleAsync(DomainEventEnvelope<AntonRaidPhaseConcluded> envelope, CancellationToken ct)
    {
        var outcome = envelope.Payload.Outcome;
        var transaction = transactions.Current ?? throw new InvalidOperationException("Raid phase delivery requires a transaction.");
        transaction.RegisterAfterCommit((typeof(AntonRaidPhaseRewardSubscriber), outcome.Instance, outcome.Phase),
            token => BeginAsync(outcome, token));
        return ValueTask.CompletedTask;
    }

    private async ValueTask BeginAsync(AntonRaidPhaseOutcome outcome, CancellationToken ct)
    {
        var seed = new AntonRaidRewardSettlementSeed(outcome.Instance, outcome.Number, outcome.Phase,
            outcome.Succeeded, outcome.DeathCount, outcome.StartedAt, outcome.EndedAt,
            outcome.Audience,
            outcome.Eligible.Select(member => new AntonRaidRewardParticipant(member.AccountId,
                member.CharacterId, member.Squad, member.Role)).ToArray());
        var started = settlements.Begin(seed, clock.GetUtcNow());
        var snapshot = started ?? settlements.Find(seed.Instance, seed.Phase);
        if (snapshot is null || started is null && snapshot.Stage != AntonRaidRewardStage.Result)
            return;
        foreach (var character in outcome.Audience)
        {
            if (!sessions.TryGetInWorld(character, out var session) || raids.SettlementOf(session) != (seed.Instance, seed.Phase))
                continue;
            var hasCards = seed.Succeeded && seed.Eligible.Any(member => member.Character == session.CharacterId);
            var result = AntonRaidRewardNotice.Result(snapshot, hasCards);
            IGameServerMessage[] notices = seed.Succeeded && seed.Eligible.Count > 0
                ? [result, AntonRaidRewardNotice.State(3, 0)] : [result];
            await events.ProjectAsync(new NotificationAudience.TheseSessions([session]),
                new AntonRaidRuntimeChanged(notices), ct).ConfigureAwait(false);
        }
    }
}
