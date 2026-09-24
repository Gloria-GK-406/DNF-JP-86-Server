using Dfo.Server.Data.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Protocol.Game.MonsterDeath;

internal static class PendingQuestItemGrants
{
    internal static async ValueTask<QuestItemGrant> GrantAsync(
        AccountId accountId, CharacterId characterId,
        IReadOnlyList<RolledDrop>? pendingLoot, PartyKey partyKey,
        ICharacterQuestLogDomainService questLogs, ICharacterContainerDomainService containers,
        IItemGrantDomainService itemGrants, IPartyService? parties,
        CancellationToken cancellationToken, bool failIfFull = false)
    {
        if (partyKey != default
            && (parties?.FindByKey(partyKey) is not { } current
                || !current.Members.Any(member => member.CharacterId == characterId)))
            return QuestItemGrant.Empty;
        var candidates = pendingLoot?
            .Where(drop => drop.Delivery == RolledDropDelivery.DirectQuestItem
                && drop.OwnerCharacterId == characterId)
            .ToArray() ?? [];
        if (candidates.Length == 0)
            return QuestItemGrant.Empty;

        var bag = containers.FindContainer(characterId, InventoryContainerType.Main);
        if (bag is null)
        {
            if (failIfFull) throw new InvalidOperationException("The quest inventory is unavailable; object reward was not consumed.");
            return QuestItemGrant.Empty;
        }
        var carried = await questLogs.LogOf(accountId, characterId)
            .CarriedAsync(cancellationToken).ConfigureAwait(false);
        var active = carried.Select(quest => quest.QuestId).ToHashSet();
        var changed = new HashSet<int>();
        var accountChanges = new List<AccountMaterialChange>();
        foreach (var drop in candidates)
        {
            if (drop.QuestId <= 0 || !active.Contains(drop.QuestId)
                || drop.ItemId <= 0 || drop.Count <= 0 || drop.GrantLimit <= 0)
                continue;

            var held = (await bag.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                .Where(item => item.ItemId == drop.ItemId)
                .Sum(item => (long)item.Count);
            var count = checked((int)Math.Min(drop.Count, Math.Max(0, drop.GrantLimit - held)));
            if (count <= 0)
                continue;
            var remaining = count;
            while (remaining > 0)
            {
                var granted = await itemGrants.GrantAsync(
                        accountId, bag, drop.ItemId, remaining,
                        cancellationToken)
                    .ConfigureAwait(false);
                switch (granted)
                {
                    case ItemGrantOutcome.CharacterItem
                        { Placement: ItemPlacementOutcome.AddedToPile topped } when topped.Added > 0:
                        changed.Add(topped.SlotIndex);
                        remaining -= topped.Added;
                        break;
                    case ItemGrantOutcome.CharacterItem
                        { Placement: ItemPlacementOutcome.PutInEmptySlot opened } when opened.Count > 0:
                        changed.Add(opened.SlotIndex);
                        remaining -= opened.Count;
                        break;
                    case ItemGrantOutcome.AccountMaterial account:
                        accountChanges.Add(account.Change);
                        remaining -= account.Change.AddedCount;
                        break;
                    case ItemGrantOutcome.CharacterItem { Placement: ItemPlacementOutcome.NoRoom }:
                        if (failIfFull) throw new InvalidOperationException("The quest inventory is full; object reward was not consumed.");
                        remaining = 0;
                        break;
                    case ItemGrantOutcome.CharacterItem { Placement: ItemPlacementOutcome.UnknownItem }:
                        throw new InvalidDataException($"Quest item {drop.ItemId} has no inventory definition.");
                    default:
                        throw new InvalidDataException(
                            $"Quest item {drop.ItemId} placement made no progress: {granted}.");
                }
            }
        }

        if (changed.Count == 0)
            return new QuestItemGrant(null, accountChanges);
        var items = new List<InventoryItemState>(changed.Count);
        foreach (var slot in changed.Order())
        {
            var item = await bag.ReadSlotAsync(slot, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException($"Quest item slot {slot} disappeared before commit.");
            items.Add(new InventoryItemState(null, slot, item.Core));
        }
        return new QuestItemGrant(
            new CommittedInventoryUpdateState(
                InventoryContainerType.Main, items, DateTimeOffset.UtcNow),
            accountChanges);
    }

    internal sealed record QuestItemGrant(
        CommittedInventoryUpdateState? CharacterItems,
        IReadOnlyList<AccountMaterialChange> AccountMaterials)
    {
        public static QuestItemGrant Empty { get; } = new(null, []);

        public bool HasChanges => this.CharacterItems is not null || this.AccountMaterials.Count > 0;
    }
}
