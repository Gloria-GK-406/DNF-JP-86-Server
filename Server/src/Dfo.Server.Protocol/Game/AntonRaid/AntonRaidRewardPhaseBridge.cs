using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.AntonRaid;

internal sealed class AntonRaidRewardPhaseBridge(IAntonRaidDomainService raids, ISelectedSessionDomainService sessions,
    ICommandWorkUnits units, AntonRaidSquadProjection squads, ICommandProjections projections,
    IDungeonEntryIntentDomainService entryIntents, ITownAreaDomainService towns, DungeonTownReturnProjection returns, TimeProvider clock)
    : IAntonRaidRewardPhaseBridge
{
    public (Guid Instance, byte Phase)? Locate(SelectedSessionIdentity session) => raids.SettlementOf(session);

    public async ValueTask<bool> CompleteAsync(CommandExecutionContext execution, AntonRaidRewardSnapshot snapshot, CancellationToken ct)
    {
        var finalized = await units.WriteAsync(execution, new("anton-raid.finish-settlement", []),
            (_, _) => ValueTask.FromResult(new PhaseFinalization(raids.CompletePhaseSettlement(
                snapshot.Seed.Instance, snapshot.Seed.Phase))), ct).ConfigureAwait(false);
        if (finalized.Change is not { Raid: { } raid } change)
            return true;
        await squads.DeliverChangesAsync([change], ct).ConfigureAwait(false);
        var recipients = raid.Members.Where(member => member.RejoinUntil is null)
            .Select(member => member.Session).Where(sessions.IsInWorld).ToArray();
        if (recipients.Length == 0)
            return true;
        var messages = new List<IGameServerMessage> { AntonRaidRuntimeNotice.State(raid.Stage, raid.Phase) };
        if (raid.Stage == AntonRaidStage.Break && raids.StageDeadline(recipients[0]) is { } breakEnds)
            messages.AddRange(AntonRaidRuntimeNotice.Timer(breakEnds, clock.GetUtcNow(), kind: 1));
        await AntonRaidRuntimeProjection.SendAsync(projections, recipients, messages, ct).ConfigureAwait(false);
        foreach (var recipient in recipients)
        {
            if (entryIntents.Find(recipient)?.Raid is not { } selection
                || selection.Instance != snapshot.Seed.Instance || selection.Phase != snapshot.Seed.Phase)
                continue;
            entryIntents.ClearExact(recipient);
            if (towns.Find(recipient.CharacterId) is null)
                await returns.ReturnAsync(recipient, ct).ConfigureAwait(false);
        }
        return true;
    }

    private sealed record PhaseFinalization(AntonRaidRecruitmentChange? Change);
}

internal sealed class AntonRaidPhaseRewardSubscriber(IAntonRaidRewardSettlementDomainService settlements,
    IAntonRaidRewardPhaseBridge bridge, ISelectedSessionDomainService sessions,
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
            if (!sessions.TryGetInWorld(character, out var session) || bridge.Locate(session) != (seed.Instance, seed.Phase))
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
