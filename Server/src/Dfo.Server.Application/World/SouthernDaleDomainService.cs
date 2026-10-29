using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class SouthernDaleDomainService(ISouthernDaleRuleService rules,
    IDungeonAdmissionDomainService admission, IDungeonStructureRuleService layouts,
    IDungeonVisitDomainService visits, IDungeonRunDomainService runs, IEventManagementDomainService activities,
    ISouthernDaleDomainStore store, IItemDeliveryDomainService delivery,
    IDailyRefillConfigurationDomainService refills, IDailyRefillDomainService refill,
    ISelectedSessionDomainService sessions,
    IEventService events,
    IDomainTransactionAccessor transactions, TimeProvider clock) : ISouthernDaleDomainService
{
    public DungeonRunKind Kind => DungeonRunKind.SouthernDale;
    public bool Supports(int dungeonId) => rules.Supports(dungeonId);

    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct)
    {
        if (!Supports(context.DungeonId) || context.RequestMode != 0 || context.PracticeMode != 0
            || context.MirrorQuestId is not null || context.Facts.Intent.Kind == DungeonEntryIntentKind.Cycle)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        if (!await activities.IsOpenAsync(rules.EventId, clock.GetUtcNow(), ct).ConfigureAwait(false))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.OutsideOpeningHours);
        SouthernDaleDefinition definition;
        try { definition = await rules.DefinitionAsync(ct).ConfigureAwait(false); }
        catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
        if (context.Participants.Count < 1 || context.Participants.Count > definition.PartyLimit)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        if (context.Participants.Any(x => runs.Find(x.CharacterId) is not null))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.AlreadyInDungeon);
        return await admission.ExecuteAsync(context, new()
        {
            RequireMinimumLevel = true, ConsumesFatigue = false,
            RequiredItemsOverride = definition.Dungeon.RequiredItems,
        }, async (receipt, token) =>
        {
            DungeonLayout? layout;
            try { layout = await layouts.ResolveAsync(context.DungeonId, context.Facts, Random.Shared.Next(), token).ConfigureAwait(false); }
            catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
            return layout is null ? DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound)
                : await visits.EnterGrantedPartyAsync(context.Participants,
                    layout with { Mode = new DungeonRunMode.Plain(DungeonRunKind.SouthernDale) }, context.Facts, receipt, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async ValueTask<bool> RefillOnlineTicketAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        if (!sessions.IsInWorld(session))
            return false;
        var at = clock.GetUtcNow();
        var configured = (await refills.EffectiveAsync(at, ct).ConfigureAwait(false)).Where(x => x.ItemId == rules.TicketItemId).ToArray();
        var result = await refill.ApplyIndependentRefillAsync(session.AccountId, session.CharacterId, configured, at, ct).ConfigureAwait(false);
        // A new game day restores the daily quest even when two tickets are already held.
        await events.EnqueueAsync(new SouthernDaleDailyRefillCompleted(session.CharacterId,
            [.. result.Grants.Select(x => x.Slot).Distinct()]), ct).ConfigureAwait(false);
        return true;
    }

    public async ValueTask CompleteClearAsync(DungeonActorDeathClaim death,
        IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken ct)
    {
        var transaction = transactions.Current ?? throw new InvalidOperationException("Southern Dale clear requires a transaction.");
        if (transaction.IsReadOnly) throw new InvalidOperationException("Southern Dale clear requires a writable transaction.");
        var run = runs.Find(death.CharacterId);
        if (run is null || !Supports(run.Layout.DungeonId) || run.RunId != death.RunId || !run.IsBossRoom
            || !runs.IsActorDeathClaimCurrent(death) || !runs.WillClearMap(death))
            throw new InvalidOperationException("Southern Dale completion requires the authoritative clear claim.");
        if (participants.Count == 0 || participants.Count != run.Participants.Count
            || participants.Select(x => x.CharacterId).Distinct().Count() != participants.Count
            || participants.Any(x => !sessions.IsInWorld(x)
                || !run.Participants.Any(p => p.CharacterId == x.CharacterId && p.AccountId == x.AccountId)))
            throw new InvalidOperationException("Southern Dale completion participants changed.");
        var definition = await rules.DefinitionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var player in participants)
            {
                if (!await store.TryClaimClearAsync(run.RunId.Value, player.CharacterId, ct).ConfigureAwait(false)) continue;
                var receipt = await delivery.DeliverAsync(player.AccountId, player.CharacterId, definition.ClearRewards,
                    "南部溪谷通关奖励", ct, ItemDeliveryMode.InventoryThenMail).ConfigureAwait(false);
                await events.EnqueueAsync(new SouthernDaleInventoryChanged(player.CharacterId,
                    [.. receipt.Inventory.Select(x => x.Slot).Distinct()]), ct).ConfigureAwait(false);
            }
        }
        catch { transaction.RequireRollback(); throw; }
    }
}
