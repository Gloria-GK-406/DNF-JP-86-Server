using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Application.Creatures;
namespace Dfo.Server.Application.Mail;
internal sealed class MailboxAssetDomainService(IItemDomainStore store, IDomainTransactionAccessor transactions,
    ICharacterBusinessInventoryDomainService inventories, ICharacterTable owners, TimeProvider clock,
    ICreatureDomainService creatures) : IMailboxAssetDomainService
{
    public async ValueTask<MailboxAssetProjection> PrepareAsync(CharacterId character, IReadOnlyList<MailAssetChange> changes, CancellationToken token)
    {
        var session = store.SessionIn(transactions.Current ?? throw new InvalidOperationException("Mailbox projection requires the command transaction."));
        await session.ApplyDecidedWritesAsync(token);
        var at = clock.GetUtcNow();
        var updates = new List<MailboxInventoryUpdate>();
        var cleared = new List<BusinessSlot>();
        foreach (var slot in changes.Where(x => x.Character == character && x.Slot is not null)
            .Select(x => x.Slot!.Value).Distinct())
        {
            var physical = LegacyInventoryStorageCoordinates.ToStorage(slot);
            var owner = slot.Space == BusinessInventorySpace.SharedMaterial
                ? new ContainerOwner(InventoryOwnerType.Account, (await owners.FindOwnerAsync(character, token)
                    ?? throw new InvalidDataException("Mail recipient no longer exists.")).Value)
                : new ContainerOwner(InventoryOwnerType.Character, character.Value);
            var container = inventories.GetPersistentStore(owner, BusinessInventoryOwnership.StoreOf(slot.Space))
                .GetContainer(slot.Space);
            var item = await session.GetAsync(container.StorageId,
                physical.Index, token).ConfigureAwait(false);
            if (item is null) cleared.Add(slot);
            else updates.Add(new(slot, item.ItemUid, item.Snapshot.ToCore(), item.Snapshot.AvatarDetail));
        }
        IReadOnlyList<CharacterPetRuntimeState>? pets = null;
        if (updates.Any(x => x.Core.ItemKind == 5))
        {
            var states = await creatures.GetStore(character).ReadAllAsync(token).ConfigureAwait(false);
            if (states.Count > byte.MaxValue) throw new MailException(MailFailure.NoCapacity, "Creature runtime list exceeds protocol capacity.");
            pets = states.Select(CreatureRuntimeProjection.Build).ToArray();
        }
        return new(updates, cleared, changes.LastOrDefault(x => x.GoldBalance is not null)?.GoldBalance, at, pets);
    }
}
