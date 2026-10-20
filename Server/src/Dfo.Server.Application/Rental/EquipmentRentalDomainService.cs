using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Rental;

internal sealed class EquipmentRentalDomainService(IEquipmentRentalDomainStore links, ILuckyStarDomainStore starStore,
    IAccountLuckyStarDomainService stars, IEquipmentRentalRuleService rules, ICharacterDomainService characters,
    ICharacterTable owners, ICharacterItemTable items, IItemDeliveryDomainService delivery, IItemDomainStore itemStore,
    IDomainTransactionAccessor transactions, TimeProvider clock, IInventorySlotMappingRuleService mapping) : IEquipmentRentalDomainService
{
    public async ValueTask<EquipmentRentalPanel> PanelAsync(AccountId account,CharacterId character,CancellationToken ct)
    {
        if (!await owners.BelongsToAsync(character,account,ct).ConfigureAwait(false)) return new(0,[]);
        return new(await stars.BalanceAsync(account,ct).ConfigureAwait(false),await ActiveAsync(character,clock.GetUtcNow(),ct).ConfigureAwait(false));
    }
    private async ValueTask<IReadOnlyList<EquipmentRentalItem>> ActiveAsync(CharacterId character,DateTimeOffset now,CancellationToken ct)
    {
        var rows=await items.ReadByCharacterAsync(character,ct).ConfigureAwait(false);
        var related=await links.ReadAsync(character,ct).ConfigureAwait(false);
        return related.Select(link=>(Link:link,Row:rows.SingleOrDefault(row=>row.ItemUid==link.Uid.Value)))
            .Where(x=>x.Row is {} row && row.Core.ItemId==x.Link.ItemId && row.Core.ExpireTime>now.ToUnixTimeSeconds())
            .Select(x=>new EquipmentRentalItem(x.Link.Uid,x.Link.ItemId,x.Row!.Core.ExpireTime)).ToArray();
    }
    public async ValueTask<EquipmentRentalReceipt> RentAsync(AccountId account,CharacterId character,int itemId,CancellationToken ct)
    {
        var transaction=transactions.Current ?? throw new InvalidOperationException("Rental requires a transaction.");
        await starStore.AcquireWriteAsync(ct).ConfigureAwait(false);
        var now=clock.GetUtcNow();
        var panel=await PanelAsync(account,character,ct).ConfigureAwait(false);
        EquipmentRentalReceipt Reject(EquipmentRentalRejection rejection)=>new(rejection,panel,[]);
        if (!await owners.BelongsToAsync(character,account,ct).ConfigureAwait(false)) return Reject(EquipmentRentalRejection.UnknownOwner);
        var instance=characters.Get(character);
        var identity=await instance.IdentityAsync(ct).ConfigureAwait(false);
        var progress=await instance.ProgressAsync(ct).ConfigureAwait(false);
        if(identity is null || progress is null) return Reject(EquipmentRentalRejection.UnknownOwner);
        var quote=await rules.QuoteAsync(itemId,identity.Job,progress.Level,now,ct).ConfigureAwait(false);
        if(quote.Rejection!=EquipmentRentalRejection.None) return Reject(quote.Rejection);
        var existing=panel.Items.SingleOrDefault(x=>x.ItemId==itemId);
        if(existing is null && panel.Items.Count>=10) return Reject(EquipmentRentalRejection.TooManyRentals);
        if(panel.LuckyStar<quote.Price) return Reject(EquipmentRentalRejection.InsufficientStars);
        try
        {
            var spent=await stars.SpendAsync(account,quote.Price,ct).ConfigureAwait(false);
            if(!spent.Succeeded) return Reject(EquipmentRentalRejection.InsufficientStars);
            IReadOnlyList<InventoryContainerType> changed;
            IReadOnlyList<BusinessSlot> touched;
            if(existing is not null)
            {
                var row=(await items.ReadByCharacterAsync(character,ct).ConfigureAwait(false)).Single(x=>x.ItemUid==existing.Uid.Value);
                var session=itemStore.SessionIn(transaction);
                var container=ContainerId.ForCharacter(character,(InventoryContainerType)row.ListType);
                var item=await session.GetAsync(container,row.SlotIndex,ct).ConfigureAwait(false)
                    ?? throw new InvalidDataException("Rental instance disappeared.");
                await session.UpdateAsync(item,x=>x with {ExpireTime=checked((int)quote.ExpiresAt.ToUnixTimeSeconds())},ct).ConfigureAwait(false);
                changed=[(InventoryContainerType)row.ListType];
                touched=[mapping.ToBusinessSlot(new((InventoryContainerType)row.ListType,row.SlotIndex))];
            }
            else
            {
                var receipt=await delivery.DeliverAsync(account,character,[new(itemId,1,quote.Snapshot)],"幸运星武器租赁",ct,
                    ItemDeliveryMode.InventoryOnly,new(quote.ExpiresAt,string.Empty)).ConfigureAwait(false);
                var added=receipt.Inventory.Single();
                await links.AddAsync(character,added.Snapshot.ItemUid??throw new InvalidDataException("Rental equipment has no world identity."),itemId,ct).ConfigureAwait(false);
                changed=[InventoryContainerType.Main];
                touched=receipt.Inventory.Select(x=>x.Slot).ToArray();
            }
            return new(EquipmentRentalRejection.None,await PanelAsync(account,character,ct).ConfigureAwait(false),changed,existing is not null){TouchedSlots=touched};
        }
        catch(ContainerAdditionException error) when(error.Reason==ContainerAdditionFailure.NoRoom)
        { transaction.RequireRollback(); return Reject(EquipmentRentalRejection.InventoryFull); }
        catch { transaction.RequireRollback(); throw; }
    }
}
