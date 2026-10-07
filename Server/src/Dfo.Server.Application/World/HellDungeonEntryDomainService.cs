using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.World;

internal sealed class HellDungeonEntryDomainService(
    IHellDungeonEntryRuleService hellRules, IDungeonStructureRuleService structures,
    IDungeonVisitDomainService visits, ICharacterDomainService characters,
    ICharacterQuestLogDomainService quests, ICharacterBusinessInventoryDomainService inventory,
    IItemLifecycleDomainService lifecycle, IDomainTransactionAccessor transactions) : IHellDungeonEntryDomainService
{
    public ValueTask<DungeonEntryAttempt> EnterRandomAsync(DungeonEntryContext context, CancellationToken token) =>
        ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.HellUnavailable));

    public async ValueTask<DungeonEntryAttempt> EnterManualAsync(DungeonEntryContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (transactions.Current is null) throw new InvalidOperationException("Hell entry requires the shared entry transaction.");
        if (context.RequestMode != 1 || context.PracticeMode != 0)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.HellUnavailable);
        var rules = await hellRules.GetRulesAsync(context.DungeonId, token).ConfigureAwait(false);
        if (rules is not { Enabled: true }) return DungeonEntryAttempt.Refused(DungeonEntryFailure.HellUnavailable);
        var definition = await structures.GetDefinitionAsync(context.DungeonId, token).ConfigureAwait(false);
        if (definition is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound);
        var seed = Random.Shared.Next();
        var layout = await structures.ResolveAsync(context.DungeonId, context.Facts, seed, token).ConfigureAwait(false);
        if (layout is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.HellConfigurationInvalid);
        var plan = await hellRules.PrepareAsync(rules, layout, seed, token).ConfigureAwait(false);
        if (plan is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.HellConfigurationInvalid);
        var pending = new List<Debit>();
        var alternatives = hellRules.TicketAlternatives(rules, definition.MinimumRequiredLevel);
        foreach (var member in context.Participants)
        {
            var progress = await characters.Get(member.CharacterId).ProgressAsync(token).ConfigureAwait(false);
            if (progress is null || progress.Level < definition.MinimumRequiredLevel)
                return new(null, DungeonEntryFailure.LevelTooLow, member.Slot);
            var cleared = await quests.LogOf(member.AccountId, member.CharacterId).ClearedAsync(token).ConfigureAwait(false);
            if (rules.RequiredQuestIds.Any(id => !cleared.Any(quest => quest.QuestId == id)))
                return new(null, DungeonEntryFailure.HellQuestNotCleared, member.Slot);
            var costs = await PrepareTicketsAsync(member, alternatives, definition.RequiredItems, token).ConfigureAwait(false);
            if (costs is null) return new(null, DungeonEntryFailure.MissingHellTicket, member.Slot);
            pending.AddRange(costs);
        }
        layout = layout with { EntryKind = DungeonEntryKind.ManualHell, Hell = plan,
            StartRoomMapId = layout.StartCell == plan.Cell ? plan.MapId : layout.StartRoomMapId };
        var attempt = await visits.TryEnterPreparedPartyAsync(context.Participants, layout, context.Facts, token)
            .ConfigureAwait(false);
        if (attempt.Entry is not { } entry) return attempt;
        // Ordinary entry may consume the same item from these stacks. Rebind the validated
        // hell cost to current references after it commits to the shared transaction.
        pending.Clear();
        foreach (var member in context.Participants)
            pending.AddRange(await PrepareTicketsAsync(member, alternatives, [], token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Validated hell tickets disappeared during entry."));
        var changes = new List<DungeonEntryInventoryChange>();
        foreach (var debit in pending)
        {
            var remaining = await ItemContainerOperations.ConsumeAsync(debit.Container, debit.Item, debit.Count,
                lifecycle, token).ConfigureAwait(false);
            changes.Add(new(debit.Member.CharacterId, new(debit.Container.Space, debit.Item.SlotIndex),
                remaining == 0 ? null : (debit.Item.Snapshot with { Count = remaining }).ToCore()));
        }
        return DungeonEntryAttempt.Accepted(entry with { InventoryChanges = entry.InventoryChanges.Concat(changes).ToArray() });
    }

    private async ValueTask<IReadOnlyList<Debit>?> PrepareTicketsAsync(DungeonRunParticipant member,
        IReadOnlyList<HellTicketAlternative> alternatives, IReadOnlyList<DungeonRequiredItem> ordinaryRequirements, CancellationToken token)
    {
        var store = inventory.GetStore(member.CharacterId, BusinessInventoryStoreKind.Item);
        var items = new List<(IBusinessItemContainer Container, ItemReference Item)>();
        foreach (var space in new[] { BusinessInventorySpace.QuickSlot, BusinessInventorySpace.Consumable,
                     BusinessInventorySpace.Material, BusinessInventorySpace.Quest, BusinessInventorySpace.ExpertMaterial })
            if (store.FindContainer(space) is { } container)
                items.AddRange((await container.GetAllAsync(token).ConfigureAwait(false)).Select(item => (container, item)));
        foreach (var option in alternatives)
        {
            var candidates = items.Where(x => x.Item.ItemId == option.ItemId).OrderBy(x => x.Container.Space)
                .ThenBy(x => x.Item.SlotIndex).ToArray();
            var ordinaryCount = ordinaryRequirements.Where(x => x.ItemId == option.ItemId).Sum(x => (long)x.Count);
            if (candidates.Sum(x => (long)x.Item.Count) < option.Count + ordinaryCount) continue;
            var remaining = option.Count;
            var result = new List<Debit>();
            foreach (var candidate in candidates)
            {
                var count = Math.Min(candidate.Item.Count, remaining);
                result.Add(new(member, candidate.Container, candidate.Item, count));
                remaining -= count;
                if (remaining == 0) break;
            }
            return result;
        }
        return null;
    }

    private sealed record Debit(DungeonRunParticipant Member, IBusinessItemContainer Container, ItemReference Item, int Count);
}
