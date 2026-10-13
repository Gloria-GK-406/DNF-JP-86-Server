using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Mailbox;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class ItemDeliveryTests
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_quest_equipment_starts_full_in_inventory_or_overflow_mail(bool overflow)
    {
        Skip.IfNot(Dfo.Server.Application.Tests.World.ContentArchive.Available,
            Dfo.Server.Application.Tests.World.ContentArchive.SkipReason);
        using var f = new ShopFixture(s => s.AddSingleton<IItemPlacementDefinitionSource>(new Content()));
        if (overflow) await f.SeedCore(9, StackableItemCore.Create(1, 9002, 123));
        var receipt = await Grant(f, new ItemDeliveryGrant(28269, 1));
        Assert.Equal((ushort)48, overflow ? Assert.Single(receipt.MailedItems).Durability
            : Assert.Single(receipt.Inventory).Snapshot.Durability);
        if (overflow)
        {
            Assert.Empty(receipt.Inventory);
            await using (var tx = Scope(f).Begin())
            {
                var bag = f.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
                    .GetPersistentStore(new(InventoryOwnerType.Character, 1), BusinessInventoryOwnership.StoreOf(BusinessInventorySpace.CarriedEquipment))
                    .GetContainer(BusinessInventorySpace.CarriedEquipment);
                await f.Services.GetRequiredService<IItemLifecycleDomainService>().DeleteItemAsync(
                    await bag.TakeAsync((await bag.GetAsync(0, default))!, null, default), default);
                await tx.CommitAsync(default);
            }
            var attachment = Assert.Single(Assert.Single(await Mail(f).InboxAsync(new(1))).Attachments);
            await Mail(f).ClaimAsync(new(1), attachment.Id);
        }
        await using var read = Scope(f).Begin();
        var target = f.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
            .GetPersistentStore(new(InventoryOwnerType.Character, 1), BusinessInventoryOwnership.StoreOf(BusinessInventorySpace.CarriedEquipment))
            .GetContainer(BusinessInventorySpace.CarriedEquipment);
        Assert.Equal((ushort)48, Assert.Single(await target.GetAllAsync(default)).Snapshot.Durability);
    }

    private static ShopFixture Fixture() => new(s =>
    {
        s.AddSingleton<IItemPlacementDefinitionSource>(new Content());
        s.AddSingleton<IEquipmentDefinitionSource>(new EquipmentDefinitions());
    });
    private static IItemDeliveryDomainService Delivery(ShopFixture f) => f.Services.GetRequiredService<IItemDeliveryDomainService>();
    private static IMailboxDomainService Mail(ShopFixture f) => f.Services.GetRequiredService<IMailboxDomainService>();
    private static DomainTransactionScope Scope(ShopFixture f) => (DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>();
    private static async Task FillQuick(ShopFixture f)
    {
        for (var i = 3; i < 9; i++) await f.Seed(i, 9005, 1);
    }
    private static async Task<ItemDeliveryReceipt> Grant(ShopFixture f, params ItemDeliveryGrant[] items)
    {
        await using var tx = Scope(f).Begin();
        var result = await Delivery(f).DeliverAsync(new(1), new(1), items, "测试奖励", default, ItemDeliveryMode.InventoryThenMail);
        await tx.CommitAsync(default);
        return result;
    }

    [Fact]
    public async Task Partial_stack_keeps_exact_remainder_in_one_mail_and_counts_unclaimed_stock()
    {
        using var f = Fixture();
        await f.Seed(121, 9001, 8, 3);
        await FillQuick(f);
        var result = await Grant(f, new ItemDeliveryGrant(9001, 7));
        Assert.Equal(2, Assert.Single(result.Inventory).Count);
        Assert.Equal(5, result.MailedCount);
        Assert.Equal(10, f.Count(9001));
        Assert.Equal(new MailAsset.Stackable(9001, 5), Assert.Single(Assert.Single(await Mail(f).InboxAsync(new(1))).Attachments).Asset);
        Assert.Equal(5, await Delivery(f).PendingQuantityAsync(new(1), 9001, default));
    }

    [Theory]
    [InlineData(8, 7)]
    [InlineData(10, 1)]
    public async Task Inventory_only_refuses_partial_or_full_overflow_without_mail_and_rolls_back_payment(int held, int reward)
    {
        using var f = Fixture();
        await f.Seed(121, 9001, held, 3);
        await FillQuick(f);
        await using (var tx = Scope(f).Begin())
        {
            await f.Services.GetRequiredService<ICharacterDomainService>().Get(new(1)).TrySpendGoldAsync(100, default);
            var error = await Assert.ThrowsAsync<ContainerAdditionException>(() => Delivery(f).DeliverAsync(
                new(1), new(1), [new(9001, reward)], "主动开启", default, ItemDeliveryMode.InventoryOnly).AsTask());
            Assert.Equal(ContainerAdditionFailure.NoRoom, error.Reason);
            await tx.CommitAsync(default); // A caught refusal must still prevent a partial commit.
        }
        Assert.Equal(held, f.Count(9001));
        Assert.Equal(100000, f.Gold);
        Assert.Empty(await Mail(f).InboxAsync(new(1)));
    }

    [Fact]
    public async Task Shared_material_fills_remaining_balance_and_mails_only_the_remainder()
    {
        using var f = new ShopFixture();
        await f.SeedShared(3037, int.MaxValue - 2);
        var result = await Grant(f, new ItemDeliveryGrant(3037, 5));
        Assert.Equal(2, Assert.Single(result.Inventory).Count);
        Assert.Equal(int.MaxValue, f.SharedCount(3037));
        Assert.Equal(3, result.MailedCount);
        Assert.Equal(new MailAsset.Stackable(3037, 3),
            Assert.Single(Assert.Single(await Mail(f).InboxAsync(new(1))).Attachments).Asset);
    }

    [Fact]
    public async Task More_than_ten_equipment_overflows_are_batched_without_duplicate_identities()
    {
        using var f = Fixture();
        await f.SeedCore(9, StackableItemCore.Create(1, 9002, 123));
        var result = await Grant(f, new ItemDeliveryGrant(9002, 23));
        Assert.Empty(result.Inventory); Assert.Equal(23, result.MailedCount); Assert.Equal(3, result.MailIds.Count);
        var mails = await Mail(f).InboxAsync(new(1));
        Assert.Equal([3, 10, 10], mails.Select(x => x.Attachments.Length).Order());
        Assert.Equal(23, mails.SelectMany(x => x.Attachments).Select(x => Assert.IsType<MailAsset.Equipment>(x.Asset).Identity).Distinct().Count());
    }

    [Theory]
    [InlineData(9002, 1, BusinessInventorySpace.CarriedEquipment)]
    [InlineData(9003, 8, BusinessInventorySpace.Avatar)]
    [InlineData(9004, 5, BusinessInventorySpace.CreatureInventory)]
    public async Task Prepared_instance_survives_mail_and_claim_with_full_attributes(int item, byte kind, BusinessInventorySpace space)
    {
        using var f = Fixture();
        ItemIdentity? uid;
        var template = new ItemSnapshot { ItemId = item, Count = 1, IsPile = false, LegacyKind = kind,
            Type = ItemTraitClassification.For(kind), InstanceValue = kind == 5 ? 1 : 45678, Durability = 35,
            Attribute = 7, SealFlag = 1, EnchantCardId = 123,
            AvatarDetail = kind == 8 ? new(0, 0, new byte[30], 0, 0, 0) : null,
            CreatureDetail = kind == 5 ? new(100, 0, 0, 0, 0, 7, [], 0) : null };
        await using (var tx = Scope(f).Begin())
        {
            var instance = await f.Services.GetRequiredService<IItemLifecycleDomainService>().CreateAsync(template, default);
            uid = instance.ItemUid;
            await Delivery(f).DeliverAsync(new(1), new(1), [new(item, 1, Instance: instance)], "实例奖励", default, ItemDeliveryMode.MailOnly);
            await tx.CommitAsync(default);
        }
        var attachment = Assert.Single(Assert.Single(await Mail(f).InboxAsync(new(1))).Attachments);
        Assert.Equal(uid, Assert.IsType<MailAsset.Equipment>(attachment.Asset).Identity);
        await Mail(f).ClaimAsync(new(1), attachment.Id);
        await using var read = Scope(f).Begin();
        var bag = f.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
            .GetPersistentStore(new(InventoryOwnerType.Character, 1), BusinessInventoryOwnership.StoreOf(space)).GetContainer(space);
        var held = Assert.Single(await bag.GetAllAsync(default));
        Assert.Equal(uid, held.ItemUid);
        Assert.Equal(template.ToCore(), held.Snapshot.ToCore());
        Assert.Equal(template.CreatureDetail?.Field04, held.Snapshot.CreatureDetail?.Field04);
        Assert.Equal(template.AvatarDetail?.JewelSocket, held.Snapshot.AvatarDetail?.JewelSocket);
    }

    [Fact]
    public async Task Mail_failure_rolls_back_partial_inventory_and_source_payment_even_if_caught()
    {
        using var f = Fixture();
        await f.Seed(121, 9001, 8, 3);
        await FillQuick(f);
        f.Sql("CREATE TRIGGER reject_reward_mail BEFORE INSERT ON mailbox_messages BEGIN SELECT RAISE(ABORT,'test mail failure'); END;");
        await using (var tx = Scope(f).Begin())
        {
            await f.Services.GetRequiredService<ICharacterDomainService>().Get(new(1)).TrySpendGoldAsync(100, default);
            await Assert.ThrowsAsync<SqliteException>(() => Delivery(f).DeliverAsync(new(1), new(1), [new(9001, 7)], "回滚奖励", default, ItemDeliveryMode.InventoryThenMail).AsTask());
        }
        Assert.Equal(8, f.Count(9001)); Assert.Equal(100000, f.Gold);
        Assert.Empty(await Mail(f).InboxAsync(new(1)));
    }

    [Fact]
    public async Task Unknown_reward_after_a_valid_reward_marks_whole_transaction_for_rollback()
    {
        using var f = Fixture();
        await using (var tx = Scope(f).Begin())
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => Delivery(f).DeliverAsync(new(1), new(1),
                [new(9001, 1), new(9999, 1)], "无效奖励", default, ItemDeliveryMode.InventoryThenMail).AsTask());
            await tx.CommitAsync(default);
        }
        Assert.Equal(0, f.Count(9001)); Assert.Empty(await Mail(f).InboxAsync(new(1)));
    }

    [Fact]
    public async Task Full_mail_claim_keeps_original_attachment_without_creating_new_mail()
    {
        using var f = Fixture();
        await f.Seed(121, 9001, 10, 3);
        await FillQuick(f);
        await Grant(f, new ItemDeliveryGrant(9001, 5));
        var mail = Assert.Single(await Mail(f).InboxAsync(new(1)));
        var error = await Assert.ThrowsAsync<MailException>(() => Mail(f).ClaimAsync(new(1), Assert.Single(mail.Attachments).Id).AsTask());
        Assert.Equal(MailFailure.NoCapacity, error.Reason);
        var remaining = Assert.Single(await Mail(f).InboxAsync(new(1)));
        Assert.Equal(mail.Id, remaining.Id); Assert.False(Assert.Single(remaining.Attachments).Claimed);
        Assert.Equal(10, f.Count(9001));
    }

    [Theory]
    [InlineData(9003, 8, "avatar")]
    [InlineData(9004, 5, "creature")]
    public async Task Nonpile_reward_mail_list_and_claim_encode_the_required_type_extensions(int id, byte kind, string wireKind)
    {
        using var f = Fixture();
        var template = new ItemSnapshot { ItemId=id, Count=1, IsPile=false, LegacyKind=kind,
            Type=ItemTraitClassification.For(kind), InstanceValue=1,
            AvatarDetail=kind==8 ? new(0,0,new byte[30],12,34,0) : null,
            CreatureDetail=kind==5 ? new(100,0,0,0,0,7,[],0) : null };
        await using(var tx=Scope(f).Begin())
        {
            await Delivery(f).DeliverAsync(new(1),new(1),[new(id,1,template)],"扩展字段测试",default,ItemDeliveryMode.MailOnly);
            await tx.CommitAsync(default);
        }
        var inbox=(await f.Services.GetRequiredService<ICommandBus>().SendAsync(
            new MailboxCommand(f.OnlineUser,f.Session,MailboxOperation.Open))).Inbox!;
        var attachment=Assert.Single(Assert.Single(inbox.Messages).Attachments);
        Assert.Equal(wireKind,attachment.ItemKind);
        if(kind==8) Assert.Contains("JewelSocket",attachment.DetailJson);
        await f.Services.GetRequiredService<MailboxOpenHandler>().HandleAsync(new(f.Connection),new MailboxOpenRequest(GameMessageMetadata.Empty),default);
        foreach(var message in f.Sender.Messages) AssertEncodes(f,message);
        f.Sender.Messages.Clear();
        await f.Services.GetRequiredService<MailboxClaimHandler>().HandleAsync(new(f.Connection),
            new MailboxClaimRequest(GameMessageMetadata.Empty,checked(0x40000000u+(uint)attachment.AttachmentId)),default);
        Assert.Equal(0,Assert.Single(f.Sender.Messages.OfType<MailboxClaimResponse>()).Error);
        foreach(var message in f.Sender.Messages) AssertEncodes(f,message);
        // Creature inventory and its independent runtime list are both projected before the ACK.
        if(kind==5) Assert.Equal(3,f.Sender.Messages.Count);
        else Assert.Equal(2,f.Sender.Messages.Count);
    }

    private static void AssertEncodes(ShopFixture f, IServerNetworkMessage message)
    {
        var registration=f.Services.GetServices<ServerMessageEncoderRegistration>().Single(x=>x.MessageType==message.GetType());
        var encoder=(IServerMessageEncoder)f.Services.GetRequiredService(registration.EncoderType);
        Assert.NotEmpty(encoder.Encode(message));
    }

    [Fact]
    public async Task Daily_refill_respects_its_band_and_counts_pending_mail_on_the_next_day()
    {
        using var f=Fixture();
        await f.Seed(121,9001,8,3); // Quick slots are free, but are outside the refill band.
        var rule=new DailyRefillRule(9001,15,DateTimeOffset.MaxValue,DailyRefillMode.RefillToTarget,10,3,
            new(BusinessInventorySpace.Material,0,0));
        var refill=f.Services.GetRequiredService<IDailyRefillDomainService>();
        for(var day=0;day<2;day++)
        {
            await using var tx=Scope(f).Begin();
            var result=await refill.ApplyDailyRefillAsync(new(1),new(1),[rule],f.Clock.GetUtcNow().AddDays(day),default);
            if(day==0) Assert.Equal(2,Assert.Single(result.Grants).GrantedCount);
            else Assert.Empty(result.Grants);
            await tx.CommitAsync(default);
        }
        Assert.Equal(10,f.Count(9001));
        Assert.Equal(5,await Delivery(f).PendingQuantityAsync(new(1),9001,default));
        Assert.Single(await Mail(f).InboxAsync(new(1)));
    }

    private sealed class EquipmentDefinitions : IEquipmentDefinitionSource
    {
        public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(1);
        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentDefinition?>(itemId == 9002 ? new(1, 1, 1, "weapon", 35) : null);
    }

    private sealed class Content : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<ItemPlacement?>(id switch
        {
            9001 => new(3, 10, new(BusinessInventorySpace.Material, 0, 0)),
            9002 or 28269 => new(1, 1, new(BusinessInventorySpace.CarriedEquipment, 0, 0)) { WorldClass = ItemWorldClass.Equipment },
            9003 => new(8, 1, new(BusinessInventorySpace.Avatar, 0, 0)) { WorldClass = ItemWorldClass.Avatar },
            9004 => new(5, 1, new(BusinessInventorySpace.CreatureInventory, 0, 0)) { WorldClass = ItemWorldClass.Creature },
            9005 => new(2, 1, new(BusinessInventorySpace.Consumable, 0, 55)),
            _ => null,
        });
    }
}
