using Dfo.Server.Abstractions.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.World;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunDomainService
{
    public void ScheduleClearReturn(CharacterId characterId, DungeonRunId runId, DateTimeOffset now)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(ScheduleClearReturn));
        lock (this.gate)
        {
            if (!this.runsById.TryGetValue(runId, out var run)
                || !run.IsMember(characterId) || run.Participants.Length < 2
                || !run.Cells[run.CurrentCell].ClearOpen
                || !run.ProgressFor(characterId).CardLayoutOpen) return;
            // A21 ordinary party result UI starts a 60 second return countdown.
            this.clearReturnDeadlines.TryAdd(runId, now.AddSeconds(60));
        }
    }

    public IReadOnlyList<DungeonRunId> FindExpiredClearReturns(DateTimeOffset now)
    {
        lock (this.gate)
            return this.clearReturnDeadlines.Where(pair => pair.Value <= now)
                .Select(pair => pair.Key).ToArray();
    }

    public DungeonRunState? TryEndClearReturn(DungeonRunId runId, DateTimeOffset now)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryEndClearReturn));
        lock (this.gate)
        {
            if (!this.runsById.TryGetValue(runId, out var run)
                || !this.clearReturnDeadlines.TryGetValue(runId, out var deadline)
                || now < deadline || !run.Cells[run.CurrentCell].ClearOpen) return null;
            var members = run.Participants;
            if (members.Length == 0) return null;
            var snapshot = run.ViewFor(members[0].CharacterId) with { Participants = members };
            _ = this.EndRun(run);
            return snapshot;
        }
    }

    public IReadOnlyList<DungeonRunId> FindExpiredPartyWipes(DateTimeOffset now)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(FindExpiredPartyWipes));
        lock (this.gate)
        {
            foreach (var aggregate in this.runsById.Values)
            {
                if (!IsPartyWiped(aggregate))
                    this.wipeDeadlines.Remove(aggregate.RunId);
                else
                    this.wipeDeadlines.TryAdd(aggregate.RunId, now.AddSeconds(10));
            }
            return this.wipeDeadlines.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray();
        }
    }

    public DungeonRunState? TryEndPartyWipe(DungeonRunId runId, DateTimeOffset now)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryEndPartyWipe));
        lock (this.gate)
        {
            if (!this.runsById.TryGetValue(runId, out var aggregate)
                || !this.wipeDeadlines.TryGetValue(runId, out var deadline)
                || now < deadline)
                return null;
            if (!IsPartyWiped(aggregate))
            {
                this.wipeDeadlines.Remove(runId);
                return null;
            }
            var members = aggregate.Participants;
            var snapshot = aggregate.ViewFor(members[0].CharacterId) with { Participants = members };
            _ = this.EndRun(aggregate);
            return snapshot;
        }
    }

    private static bool IsPartyWiped(DungeonRunAggregate aggregate)
    {
        var members = aggregate.Participants;
        return !aggregate.Cells[aggregate.CurrentCell].ClearOpen
            && members.Length > 0
            && members.All(member => !aggregate.ProgressFor(member.CharacterId).IsSettled
                && aggregate.DeadParticipants.Contains(member.CharacterId));
    }

    public async ValueTask<DungeonRunState?> TryEndPartyWipeAsync(DungeonRunId runId, DateTimeOffset now)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryEndPartyWipeAsync));
        var ended = TryEndPartyWipe(runId, now);
        if (ended is not null)
            foreach (var member in ended.Participants)
                await this.lifecycle.RecordDepartureAsync(new(member.CharacterId, ended.RunId, ended.Layout.DungeonId)).ConfigureAwait(false);
        if (ended is not null && ended.Layout.AppliesFailureWeakness)
            foreach (var member in ended.Participants)
                await this.weakness.For(member.CharacterId).SetWeakness().ConfigureAwait(false);
        return ended;
    }

    public async ValueTask<DungeonRunState?> TryEndClearReturnAsync(DungeonRunId runId, DateTimeOffset now)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryEndClearReturnAsync));
        var ended = TryEndClearReturn(runId, now);
        if (ended is not null)
            foreach (var member in ended.Participants)
                await this.lifecycle.RecordDepartureAsync(new(member.CharacterId, ended.RunId, ended.Layout.DungeonId)).ConfigureAwait(false);
        return ended;
    }
}
