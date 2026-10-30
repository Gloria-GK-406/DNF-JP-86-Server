using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class SaoTowerDomainService(ISaoTowerRuleService rules, ISaoTowerDomainStore store,
    IEventManagementDomainService activities, IDungeonAdmissionDomainService admission,
    IDungeonStructureRuleService layouts, IDungeonVisitDomainService visits, IDungeonRunCombatDomainService runs,
    ICharacterDomainService characters, IItemDeliveryDomainService delivery, IEventService events,
    IDomainTransactionAccessor transactions, TimeProvider clock) : ISaoTowerDomainService
{
    public DungeonRunKind Kind => DungeonRunKind.Sao;
    public bool Supports(int dungeonId) => rules.Supports(dungeonId);
    public async ValueTask<SaoTowerProgress?> ReadStatusAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (!await activities.IsOpenAsync(rules.EventId, now, ct).ConfigureAwait(false)) return null;
        var activity = (await activities.ListAsync(now, ct).ConfigureAwait(false)).Entries.Single(x => x.EventId == rules.EventId);
        return await store.ReadAsync(activity.SeasonId, ct).ConfigureAwait(false);
    }

    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct)
    {
        if (!Supports(context.DungeonId) || context.Participants.Count == 0 || context.RequestMode != 0
            || context.PracticeMode != 0 || context.Facts.Intent.Kind == DungeonEntryIntentKind.Cycle)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        var progress = await ReadStatusAsync(ct).ConfigureAwait(false);
        if (progress is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        var floor = rules.Floor(progress.NextFloor);
        foreach (var participant in context.Participants)
            if (await characters.Get(participant.CharacterId).ProgressAsync(ct).ConfigureAwait(false) is not { } standing
                || standing.Level < floor.MinimumLevel)
                return DungeonEntryAttempt.Refused(DungeonEntryFailure.LevelTooLow);
        var facts = context.Facts with { Difficulty = DungeonDifficulty.Normal, Intent = DungeonEntryIntent.Interactive };
        var target = context with { DungeonId = floor.DungeonId, Facts = facts };
        return await admission.ExecuteAsync(target, new()
        {
            RequireMinimumLevel = true, ConsumesFatigue = false, RequiredItemsOverride = floor.Tickets,
        }, async (receipt, token) =>
        {
            return await visits.ResolveAndEnterAsync(layouts, target.Participants, floor.DungeonId, facts, receipt,
                layout => layout with { Mode = new DungeonRunMode.Sao(new(progress)), ConsumesFatigue = false }, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async ValueTask CompleteClearAsync(DungeonActorDeathClaim death,
        IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken ct)
    {
        var transaction = transactions.Current ?? throw new InvalidOperationException("SAO clear requires a transaction.");
        if (transaction.IsReadOnly) throw new InvalidOperationException("SAO clear requires a writable transaction.");
        var run = runs.Find(death.CharacterId);
        if (run is null || run.RunId != death.RunId || run.Layout.Mode is not DungeonRunMode.Sao { Plan: var plan } || !run.IsBossRoom
            || !runs.IsActorDeathClaimCurrent(death) || !runs.WillClearMap(death))
            throw new InvalidOperationException("SAO completion requires the current authoritative room-clear claim.");
        if (participants.Count == 0 || participants.Select(x => x.CharacterId).Distinct().Count() != participants.Count
            || participants.Count != run.Participants.Count
            || participants.Any(x => !run.Participants.Any(p => p.CharacterId == x.CharacterId && p.AccountId == x.AccountId)))
            throw new InvalidOperationException("SAO completion participants changed.");
        var catalog = rules.Catalog;
        if (!await store.TryClearAsync(plan.Progress, run.RunId.Value, catalog.Floors.Count, ct).ConfigureAwait(false)) return;
        try
        {
            var refreshed = new List<SaoTowerRewardRefresh>();
            foreach (var player in participants)
            {
                await store.RecordRewardAsync(run.RunId.Value, player.CharacterId, catalog.FirstClearRewardItemId, ct).ConfigureAwait(false);
                var receipt = await delivery.DeliverAsync(player.AccountId, player.CharacterId,
                    [new(catalog.FirstClearRewardItemId, 1)], "SAO每层首通奖励", ct, ItemDeliveryMode.InventoryThenMail).ConfigureAwait(false);
                refreshed.Add(new(player.CharacterId, [.. receipt.Inventory.Select(x => x.Slot).Distinct()]));
            }
            await events.EnqueueAsync(new SaoTowerCleared(plan.Progress.Season, run.RunId.Value, [.. refreshed]), ct).ConfigureAwait(false);
        }
        catch { transaction.RequireRollback(); throw; }
    }
}
