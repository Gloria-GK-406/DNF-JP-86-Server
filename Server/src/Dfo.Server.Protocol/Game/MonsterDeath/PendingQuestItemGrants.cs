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
        var changed = new Dictionary<int, (IBusinessItemContainer Container, int Index)>();
        var accountChanges = new List<InventoryRuntimeValueState>();
        foreach (var drop in candidates)
        {
            if (drop.QuestId <= 0 || !active.Contains(drop.QuestId)
                || drop.ItemId <= 0 || drop.Count <= 0 || drop.GrantLimit <= 0)
                continue;

            var sourceBag = AccountMaterialCatalog.ContainsItem(drop.ItemId)
                ? containers.FindContainer(characterId, InventoryContainerType.SharedMaterial) ?? throw new InvalidDataException("Shared material container is unavailable.")
                : bag;
            var held = (await sourceBag.GetAllAsync(cancellationToken).ConfigureAwait(false))
                .Where(item => item.ItemId == drop.ItemId)
                .Sum(item => (long)item.Count);
            var count = checked((int)Math.Min(drop.Count, Math.Max(0, drop.GrantLimit - held)));
            if (count <= 0)
                continue;
            var remaining = count;
            while (remaining > 0)
            {
                var granted = await itemGrants.GrantAsync(
                        accountId, characterId, containers, drop.ItemId, remaining,
                        cancellationToken)
                    .ConfigureAwait(false);
                switch (granted)
                {
                    case BusinessItemAdditionResult
                        { Placement: ItemPlacementOutcome.AddedToPile topped } character when topped.Added > 0:
                        changed[MainQuestSlot(character, topped.SlotIndex)] = (character.Container, topped.SlotIndex);
                        remaining -= topped.Added;
                        break;
                    case BusinessItemAdditionResult
                        { Placement: ItemPlacementOutcome.PutInEmptySlot opened } character when opened.Count > 0:
                        changed[MainQuestSlot(character, opened.SlotIndex)] = (character.Container, opened.SlotIndex);
                        remaining -= opened.Count;
                        break;
                    case BusinessItemAdditionResult { Placement: ItemPlacementOutcome.NoRoom }:
                        if (failIfFull) throw new InvalidOperationException("The quest inventory is full; object reward was not consumed.");
                        remaining = 0;
                        break;
                    case BusinessItemAdditionResult { Placement: ItemPlacementOutcome.UnknownItem }:
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
        foreach (var (slot, source) in changed.OrderBy(x => x.Key))
        {
            var item = await source.Container.GetAsync(source.Index, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException($"Quest item slot {slot} disappeared before commit.");
            if (source.Container.Space == BusinessInventorySpace.SharedMaterial) accountChanges.Add(new(slot, item.ItemId, item.Count));
            else items.Add(new InventoryItemState(null, slot, item.Snapshot.ToCore()));
        }
        return new QuestItemGrant(
            new CommittedInventoryUpdateState(
                InventoryContainerType.Main, items, DateTimeOffset.UtcNow),
            accountChanges);
    }

    private static int MainQuestSlot(BusinessItemAdditionResult grant, int localIndex)
    {
        var wire = ItemGrantWireProjection.Slot(grant, localIndex);
        if (wire.Space != InventoryContainerType.Main)
            throw new InvalidDataException($"Direct quest item cannot be projected into {wire.Space}.");
        return wire.Index;
    }

    internal sealed record QuestItemGrant(
        CommittedInventoryUpdateState? CharacterItems,
        IReadOnlyList<InventoryRuntimeValueState> AccountMaterials)
    {
        public static QuestItemGrant Empty { get; } = new(null, []);

        public bool HasChanges => this.CharacterItems is not null || this.AccountMaterials.Count > 0;
    }
}
