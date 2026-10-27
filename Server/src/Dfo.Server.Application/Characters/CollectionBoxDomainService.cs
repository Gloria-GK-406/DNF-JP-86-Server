using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Characters;

internal sealed class CollectionBoxDomainService(ICollectionBoxRuleService rules, IItemDomainStore items,
    IDomainTransactionAccessor transactions, ICollectionBoxSelectionTable selection, ICharacterCollectionBoxTable legacy,
    IBusinessInventorySlotDomainService slots, IItemDeliveryDomainService delivery, ICharacterTable characters,
    ILogger<CollectionBoxDomainService> logger) : ICollectionBoxDomainService
{
    private static ContainerId Box(CharacterId character) => ContainerId.ForCharacter(character, InventoryContainerType.CollectionBox);
    private IItemDomainStoreSession Session => items.SessionIn(transactions.Current
        ?? throw new InvalidOperationException("Collection box access requires a use-case transaction."));
    private static int Index(CollectionBoxDefinition box, CollectionBoxSlot slot) => slot.Row * box.Columns + slot.Column;

    public ValueTask<CollectionBoxSnapshot> ReadAsync(CharacterId character, CancellationToken ct) =>
        new AmbientStoreAccess(transactions).ReadSnapshotAsync(async () =>
    {
        var catalog = await rules.CatalogAsync(ct).ConfigureAwait(false);
        return await ReadAsync(character, catalog, ct).ConfigureAwait(false);
    });
    private async ValueTask<CollectionBoxSnapshot> ReadAsync(CharacterId character, IReadOnlyList<CollectionBoxDefinition> catalog, CancellationToken ct)
    {
        var held = await Session.GetAllAsync(Box(character), ct).ConfigureAwait(false);
        var diagnostics = ImmutableArray.CreateBuilder<string>();
        foreach (var row in await legacy.ReadAsync(character, ct).ConfigureAwait(false))
            diagnostics.Add($"Legacy box {row.BoxIndex} slot {row.SlotIndex}: item {row.ItemId} has no recoverable instance.");
        var projected = new Dictionary<int, List<(int Slot, int ItemId)>>();
        foreach (var item in held)
        {
            if (item.StorageSlotIndex is < 256 or > 65535)
            {
                diagnostics.Add($"Invalid stored coordinate {item.StorageSlotIndex}: item {item.ItemId} preserved.");
                continue;
            }
            var (id, index) = CollectionBoxStorage.Decode(item.StorageSlotIndex);
            var definition = catalog.SingleOrDefault(x => x.BoxIndex == id);
            if (definition is null || !definition.Slots.Any(x => Index(definition, x) == index && x.ItemTemplateId == item.ItemId && x.ItemTemplateId > 0)
                || item.Count != 1 || item.Snapshot.IsMirror || !item.Snapshot.Lifetime.IsPermanent)
            {
                diagnostics.Add($"Stored box {id} slot {index}: item {item.ItemId} conflicts with current content; original preserved.");
                continue;
            }
            if (!projected.TryGetValue(id, out var list)) projected.Add(id, list = []);
            list.Add((index, item.ItemId));
        }
        var selected = await selection.ReadAsync(character, ct).ConfigureAwait(false);
        var active = catalog.Any(x => x.BoxIndex == selected) ? selected!.Value : catalog.FirstOrDefault()?.BoxIndex ?? 0;
        foreach (var diagnostic in diagnostics)
            logger.LogWarning("Collection box state requires reconciliation character={CharacterId}: {Diagnostic}", character.Value, diagnostic);
        return new(checked((byte)active), catalog.Select(x => new CollectionBoxContents(checked((byte)x.BoxIndex),
            projected.GetValueOrDefault(x.BoxIndex, []).OrderBy(v => v.Slot).Select(v => v.ItemId).ToImmutableArray())).ToImmutableArray(), diagnostics.ToImmutable());
    }

    public async ValueTask<CollectionBoxMutation> ExecuteAsync(AccountId account, CharacterId character, CollectionBoxAction action,
        byte boxId, BusinessSlot? source, int itemId, CancellationToken ct)
    {
        _ = Session;
        if (!await characters.BelongsToAsync(character, account, ct).ConfigureAwait(false)) throw new CollectionBoxRejectedException(CollectionBoxRejection.InvalidOwner);
        // Permanent boxes never consume extension tickets, including requests forged with a valid ticket slot.
        if (action == CollectionBoxAction.Extend) throw new CollectionBoxRejectedException(CollectionBoxRejection.Permanent);
        var catalog = await rules.CatalogAsync(ct).ConfigureAwait(false);
        var definition = catalog.SingleOrDefault(x => x.BoxIndex == boxId) ?? throw new CollectionBoxRejectedException(CollectionBoxRejection.UnknownBox);
        var state = await ReadAsync(character, catalog, ct).ConfigureAwait(false);
        var changes = new List<CollectionBoxInventoryChange>();
        if (action == CollectionBoxAction.Select)
            await selection.WriteAsync(character, boxId, ct).ConfigureAwait(false);
        else
        {
            if (state.SelectedBoxId != boxId) throw new CollectionBoxRejectedException(CollectionBoxRejection.WrongBox);
            var target = definition.Slots.SingleOrDefault(x => x.ItemTemplateId == itemId && itemId > 0);
            if (target.ItemTemplateId <= 0) throw new CollectionBoxRejectedException(CollectionBoxRejection.InvalidItem);
            var index = Index(definition, target);
            if ((await legacy.ReadAsync(character, ct).ConfigureAwait(false)).Any(x => x.BoxIndex == boxId && x.SlotIndex == index))
                throw new CollectionBoxRejectedException(CollectionBoxRejection.LegacyConflict);
            var encoded = CollectionBoxStorage.Slot(boxId, index);
            var stored = await Session.GetAsync(Box(character), encoded, ct).ConfigureAwait(false);
            switch (action)
            {
                case CollectionBoxAction.Put:
                    if (stored is not null || source is not { } from || !from.Space.IsCarriedItemSpace()
                        || from.Space == BusinessInventorySpace.TowerQuickSlot)
                        throw new CollectionBoxRejectedException(CollectionBoxRejection.SourceUnavailable);
                    var bound = slots.Find(character, from);
                    if (bound is null || !bound.Container.IsPersistent || !bound.Container.IsDefault
                        || bound.Container.StorageId != ContainerId.ForCharacter(character, InventoryContainerType.Main))
                        throw new CollectionBoxRejectedException(CollectionBoxRejection.SourceUnavailable);
                    var item = await bound.Container.GetAsync(from.Index, ct).ConfigureAwait(false);
                    if (item is null || item.ItemId != itemId || item.Count < 1) throw new CollectionBoxRejectedException(CollectionBoxRejection.MissingItem);
                    if (item.Snapshot is { IsMirror: true } or { SortLockFlag: not 0 } or { EquipmentLockId: not 0 }
                        || !item.Snapshot.Lifetime.IsPermanent || item.Snapshot.AvatarDetail is not null || item.Snapshot.CreatureDetail is not null)
                        throw new CollectionBoxRejectedException(CollectionBoxRejection.LockedOrTimed);
                    var original = await bound.Container.TakeAsync(item, 1, ct).ConfigureAwait(false);
                    await bound.Container.FlushAsync(ct).ConfigureAwait(false);
                    await Session.PutAsync(Box(character), encoded, original, ct).ConfigureAwait(false);
                    changes.Add(new(from, (await bound.Container.GetAsync(from.Index, ct).ConfigureAwait(false))?.Snapshot));
                    break;
                case CollectionBoxAction.Withdraw:
                    if (stored is null || stored.ItemId != itemId || stored.Count != 1) throw new CollectionBoxRejectedException(CollectionBoxRejection.MissingItem);
                    var detached = await Session.TakeAsync(stored, null, ct).ConfigureAwait(false);
                    var received = await delivery.DeliverAsync(account, character, [new(itemId, 1, Instance: detached)],
                        "collection-box.withdraw", ct, ItemDeliveryMode.InventoryOnly).ConfigureAwait(false);
                    changes.AddRange(received.Inventory.Select(x => new CollectionBoxInventoryChange(x.Slot, x.Snapshot)));
                    break;
                default: throw new CollectionBoxRejectedException(CollectionBoxRejection.InvalidAction);
            }
            await selection.WriteAsync(character, boxId, ct).ConfigureAwait(false);
        }
        await Session.ApplyDecidedWritesAsync(ct).ConfigureAwait(false);
        return new(await ReadAsync(character, catalog, ct).ConfigureAwait(false), changes);
    }
}
