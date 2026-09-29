using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CeraShopPurchase;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class CeraShopPurchaseTests
{
    [SkippableTheory]
    [InlineData(100039,22,3,780)]
    [InlineData(100041,22,7,1180)]
    [InlineData(100058,27,3,1080)]
    [InlineData(100059,27,7,1680)]
    [InlineData(292736,84,3,790)]
    [InlineData(292738,84,15,1290)]
    [InlineData(102234,97,7,600)]
    public async Task Ordinary_contract_purchase_and_renewal_persist_and_notify_current_total(int product, int type, int days, int cost)
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive:true);
        var now = shop.Host.Clock.GetUtcNow().ToUnixTimeSeconds();
        // Expired subscriptions restart from now, not from their previous expiration.
        shop.Host.Sql($"INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,{type},1)");
        var entry = CeraShopTestContent.Entry(product) with { Attribute = 255, Variant = 255 };
        await shop.Handle(CeraShopTestContent.Input(entry));
        Assert.Equal(100000-cost,shop.Cera);
        Assert.Equal(now+days*86400,shop.Host.Scalar($"SELECT end_time FROM account_premiums WHERE premium_type={type}"));
        Assert.Equal(0,shop.Host.Scalar("SELECT COUNT(*) FROM items"));
        var frame = shop.Encode(Assert.Single(shop.Host.Sender.Messages, m => m.GetType().Name == "PremiumGrantedMessage"));
        Assert.Equal((ushort)0x0042,BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1,2)));
        Assert.Equal(26,frame.Length);
        Assert.Equal((byte)type,frame[17]);
        Assert.Equal(days*86400L,BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(18,8)));
        var renewal=await shop.Buy(entry);
        Assert.NotNull(renewal.Receipt);
        Assert.Equal(now+days*2*86400,Assert.Single(renewal.Receipt!.GrantedPremiums).ExpiresAtUnixSeconds);
        Assert.Equal(100000-cost*2,shop.Cera);
    }

    [SkippableFact]
    public async Task Ordinary_contract_cart_rolls_back_both_contracts_and_payment_on_failure()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive:true);
        shop.Host.Sql("CREATE TRIGGER fail_expert BEFORE INSERT ON account_premiums WHEN NEW.premium_type=27 BEGIN SELECT RAISE(ABORT,'forced'); END;");
        await Assert.ThrowsAsync<SqliteException>(()=>shop.Buy(CeraShopTestContent.Entry(100039),CeraShopTestContent.Entry(100059)));
        Assert.Equal(100000,shop.Cera);
        Assert.Equal(0,shop.Host.Scalar("SELECT COUNT(*) FROM account_premiums"));
    }

    [SkippableFact]
    public async Task Crystal_gold_product_refuses_insufficient_gold_then_grants_seven_days()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        var entry = CeraShopTestContent.Entry(103061);
        Assert.Equal(CeraShopRejection.InsufficientFunds, (await shop.Buy(entry)).Rejection);
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM account_premiums"));
        shop.Host.Sql("UPDATE character_stats SET carried_gold=2000000");
        var receipt = (await shop.Buy(entry)).Receipt;
        Assert.NotNull(receipt);
        Assert.Equal(500000, shop.Host.Gold);
        Assert.Equal(100000, shop.Cera);
        Assert.Equal(97, Assert.Single(receipt.GrantedPremiums).PremiumType);
        Assert.Equal(shop.Host.Clock.GetUtcNow().AddDays(7).ToUnixTimeSeconds(), receipt.GrantedPremiums[0].ExpiresAtUnixSeconds);
    }

    [SkippableFact]
    public async Task Crystal_payment_and_term_roll_back_together()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        shop.Host.Sql("CREATE TRIGGER fail_crystal BEFORE INSERT ON account_premiums WHEN NEW.premium_type=97 BEGIN SELECT RAISE(ABORT,'forced'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => shop.Buy(CeraShopTestContent.Entry(102234)));
        Assert.Equal(100000, shop.Cera);
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM account_premiums"));
    }

    [SkippableFact]
    public async Task Ordinary_contract_refuses_insufficient_payment_and_catalog_placeholders()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive:true);
        shop.Host.Sql("UPDATE accounts SET cera=779,token_cera=0,happy_token_cera=0");
        Assert.Equal(CeraShopRejection.InsufficientFunds,(await shop.Buy(CeraShopTestContent.Entry(100039))).Rejection);
        shop.Host.Sql("UPDATE accounts SET cera=100000");
        foreach(var product in new[]{100043,100061,102232})
            Assert.Null((await shop.Buy(CeraShopTestContent.Entry(product))).Receipt);
        Assert.Equal(100000,shop.Cera);
        Assert.Equal(0,shop.Host.Scalar("SELECT COUNT(*) FROM account_premiums"));
    }

    [SkippableFact]
    public async Task Service_purchase_sends_complete_snapshot_after_committed_ack_and_wallet()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop=new CeraShopFixture(useArchive:true);
        await shop.Buy(CeraShopTestContent.Entry(100817));
        shop.Host.Sender.BeforeSend=()=>Assert.Equal(2,shop.Host.Scalar("SELECT COUNT(*) FROM account_premiums WHERE account_id=1"));
        await shop.Handle(CeraShopTestContent.Input(CeraShopTestContent.Entry(100538)));
        Assert.Equal(3,shop.Host.Sender.Messages.Count);
        Assert.IsType<CeraShopPurchaseResponse>(shop.Host.Sender.Messages[0]);
        var frame=shop.Encode(shop.Host.Sender.Messages[2]);
        Assert.Equal((ushort)0x036F,BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1,2)));
        // Encode returns a full 15-byte server frame; service data starts after its 3-byte prefix.
        Assert.Equal(92,frame.Length);
        foreach(var slot in new[]{5,6})
            Assert.Equal((uint)shop.Host.Scalar($"SELECT end_time FROM account_premiums WHERE account_id=1 AND premium_type={580+slot}"),
                BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(24+9*slot,4)));
    }

    [SkippableFact]
    public async Task All_eight_services_purchase_and_renew_without_inventory_items()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        int[] seven = [100542,100539,100540,100541,100537,100538,100817,100882];
        int[] thirty = [100550,100547,100548,100549,100545,100546,100818,100883];
        var before = shop.Host.Services.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeSeconds();
        var first = await shop.Buy(seven.Select(id => CeraShopTestContent.Entry(id)).ToArray());
        Assert.True(first.Receipt is not null, first.Detail);
        Assert.Equal(98250, shop.Cera);
        Assert.Empty(first.Receipt!.Updates);
        Assert.Equal(8, shop.Host.Scalar("SELECT COUNT(*) FROM account_premiums WHERE account_id=1"));
        var initial = Enumerable.Range(580,8).Select(type => shop.Host.Scalar($"SELECT end_time FROM account_premiums WHERE account_id=1 AND premium_type={type}")).ToArray();
        Assert.All(initial, end => Assert.InRange((long)end-before, 604800, 604830));
        var second = await shop.Buy(thirty.Select(id => CeraShopTestContent.Entry(id)).ToArray());
        Assert.True(second.Receipt is not null, second.Detail);
        Assert.Equal(95050, shop.Cera);
        for (var slot=0;slot<8;slot++)
            Assert.Equal(initial[slot]+2592000,shop.Host.Scalar($"SELECT end_time FROM account_premiums WHERE account_id=1 AND premium_type={580+slot}"));
        Assert.Equal(0,shop.Host.Scalar("SELECT COUNT(*) FROM items"));
    }

    [SkippableFact]
    public async Task Subscription_write_failure_rolls_back_wallet_and_earlier_entitlements()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        shop.Host.Sql("CREATE TRIGGER fail_subscription BEFORE INSERT ON account_premiums WHEN NEW.premium_type=585 BEGIN SELECT RAISE(ABORT,'forced'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => shop.Buy(CeraShopTestContent.Entry(100817),CeraShopTestContent.Entry(100538)));
        Assert.Equal(100000,shop.Cera);
        Assert.Equal(0,shop.Host.Scalar("SELECT COUNT(*) FROM account_premiums"));
    }

    [SkippableFact]
    public async Task Service_cart_rejects_insufficient_funds_and_unknown_products_without_granting()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive:true);
        shop.Host.Sql("UPDATE accounts SET cera=199,token_cera=0,happy_token_cera=0 WHERE account_id=1");
        Assert.Equal(CeraShopRejection.InsufficientFunds,(await shop.Buy(CeraShopTestContent.Entry(100538))).Rejection);
        shop.Host.Sql("UPDATE accounts SET cera=100000 WHERE account_id=1");
        Assert.Null((await shop.Buy(CeraShopTestContent.Entry(100538),CeraShopTestContent.Entry(int.MaxValue))).Receipt);
        Assert.Equal(100000,shop.Cera);
        Assert.Equal(0,shop.Host.Scalar("SELECT COUNT(*) FROM account_premiums"));
    }

    [SkippableFact]
    public async Task Concurrent_service_renewals_accumulate_all_purchased_days()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive:true);
        var before=shop.Host.Services.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeSeconds();
        var results=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>Task.Run(()=>shop.Buy(CeraShopTestContent.Entry(100538)))));
        Assert.All(results,result=>Assert.NotNull(result.Receipt));
        Assert.Equal(before+28*86400,shop.Host.Scalar("SELECT end_time FROM account_premiums WHERE account_id=1 AND premium_type=585"));
        Assert.Equal(99200,shop.Cera);
    }

    [SkippableFact]
    public async Task Mixed_item_failure_rolls_back_subscription_and_all_charges()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive:true);
        shop.Host.Sql("CREATE TRIGGER fail_items BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT,'forced'); END;");
        await Assert.ThrowsAsync<SqliteException>(()=>shop.Buy(CeraShopTestContent.Entry(100538),CeraShopTestContent.Entry(102925)));
        Assert.Equal(100000,shop.Cera);
        Assert.Equal(0,shop.Host.Scalar("SELECT COUNT(*) FROM account_premiums"));
        Assert.Equal(0,shop.Host.Scalar("SELECT COUNT(*) FROM items"));
    }

    [SkippableFact]
    public async Task Pure_subscription_plan_does_not_require_inventory_space()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive:true);
        var rules=shop.Host.Services.GetRequiredService<ICeraShopRuleService>();
        var quote=await rules.QuoteAsync(CeraShopTestContent.Input(CeraShopTestContent.Entry(100538)),20,CeraShopTestContent.Now,default);
        Assert.NotNull(quote.Quote);
        var plan=rules.Plan(quote.Quote,new Dictionary<BusinessInventorySpace,IReadOnlyList<ContainerSlotItem>>(),0,new(1000,0,0),1);
        Assert.NotNull(plan.Plan);
        Assert.Empty(plan.Plan.Writes);
    }

    [SkippableTheory]
    [InlineData(102925, 10007472, 1, 300)]
    [InlineData(102926, 10007473, 5, 1200)]
    [InlineData(102927, 10007474, 10, 2380)]
    [InlineData(102928, 10007475, 50, 11800)]
    [InlineData(102929, 10007476, 60, 14000)]
    [InlineData(102930, 10007477, 100, 23200)]
    public async Task Actual_archive_hammer_purchase_delivers_material_and_equal_boxes_instead_of_the_display_bundle(
        int product, int displayItem, int quantity, int cost)
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        for (var purchases = 1; purchases <= 2; purchases++)
        {
            var result = await shop.Buy(CeraShopTestContent.Entry(product));
            Assert.True(result.Receipt is not null, result.Detail);
            Assert.Equal(100000 - cost * purchases, shop.Cera);
            Assert.Equal(quantity * purchases, shop.Host.Count(10007367));
            Assert.Equal(quantity * purchases, shop.Host.Count(10007368));
            Assert.Equal(0, shop.Host.Count(displayItem));
            var delivered = result.Receipt!.Updates.SelectMany(update => update.Items).ToArray();
            Assert.Equal(new[] { 10007367, 10007368 }, delivered.Select(item => item.Core.ItemId).Order().ToArray());
            Assert.All(delivered, item => Assert.Equal(quantity * purchases, item.Core.Value));
        }
    }

    [SkippableTheory]
    [InlineData(650021, 2, 450, 7)]
    [InlineData(650022, 0, 990, 30)]
    [InlineData(650023, 40, 2970, 0)]
    public async Task Actual_knight_coat_resolves_shared_skill_ability_case_and_persists_selection(
        int product, byte ability, int price, int days)
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        var result = await shop.Buy(CeraShopTestContent.Entry(product, ability));
        Assert.True(result.Receipt is not null, result.Detail);
        Assert.Equal(100000 - price, shop.Cera);
        Assert.Equal(100000, shop.Host.Gold);
        var reloaded = await shop.Host.Services.GetRequiredService<ICharacterInventoryEntryDomainService>()
            .LoadAsync(shop.Host.Session.AccountId, shop.Host.Session.CharacterId, default);
        var avatar = Assert.Single(reloaded.Containers.Single(container => container.ContainerType == InventoryContainerType.Avatar).Items);
        Assert.Equal(413500013, avatar.Core.ItemId);
        Assert.Equal(ability, avatar.Core.Attribute);
        Assert.Equal(days == 0 ? 0 : shop.Host.Clock.GetUtcNow().AddDays(days).ToUnixTimeSeconds(), avatar.AvatarDetail!.ExpireDate);
    }

    [SkippableTheory]
    [InlineData(630018, 0, 300, 7)]
    [InlineData(630019, 4, 660, 30)]
    [InlineData(630020, 2, 1980, 0)]
    public async Task Actual_knight_avatar_catalog_flag_does_not_override_selected_term_price_or_ability(
        int product, byte ability, int price, int days)
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        var result = await shop.Buy(CeraShopTestContent.Entry(product, ability));
        Assert.True(result.Receipt is not null, result.Detail);
        Assert.Equal(100000 - price, shop.Cera);
        Assert.Equal(100000, shop.Host.Gold);
        Assert.Equal(1, shop.Host.Count(413550012));
        var reloaded = await shop.Host.Services.GetRequiredService<ICharacterInventoryEntryDomainService>()
            .LoadAsync(shop.Host.Session.AccountId, shop.Host.Session.CharacterId, default);
        var avatar = Assert.Single(reloaded.Containers.Single(container => container.ContainerType == InventoryContainerType.Avatar).Items);
        Assert.Equal(ability, avatar.Core.Attribute);
        Assert.Equal(days == 0 ? 0 : shop.Host.Clock.GetUtcNow().AddDays(days).ToUnixTimeSeconds(), avatar.AvatarDetail!.ExpireDate);
    }

    [SkippableFact]
    public async Task Hammer_bundle_that_only_fits_one_reward_does_not_charge_or_deliver_either_reward()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        var placements = shop.Host.Services.GetRequiredService<IItemPlacementRuleService>();
        var hammer = await placements.PlacementForAsync(10007367, default);
        var box = await placements.PlacementForAsync(10007368, default);
        var mappings = shop.Host.Services.GetRequiredService<IInventorySlotMappingRuleService>();
        Assert.NotNull(hammer);
        Assert.NotNull(box);
        static IEnumerable<int> GameRange(ItemPlacement placement,
            IInventorySlotMappingRuleService mappings) =>
            Enumerable.Range(placement.Destination.FirstIndex,
                    placement.Destination.LastIndex - placement.Destination.FirstIndex + 1)
                .Select(index => mappings.ToGameSlot(
                    new BusinessSlot(placement.Destination.Space, index)).Index);
        var occupied = Enumerable.Range(3, 6)
            .Concat(GameRange(hammer, mappings))
            .Concat(GameRange(box, mappings))
            .Distinct().Where(slot => slot != mappings.ToGameSlot(new BusinessSlot(
                hammer.Destination.Space, hammer.Destination.FirstIndex)).Index).ToArray();
        foreach (var slot in occupied) await shop.Host.Seed(slot, 3, 1);
        var result = await shop.Buy(CeraShopTestContent.Entry(102929));
        Assert.Equal(CeraShopRejection.NoRoom, result.Rejection);
        Assert.Null(result.Receipt);
        Assert.Equal(100000, shop.Cera);
        Assert.Equal(0, shop.Host.Count(10007367));
        Assert.Equal(0, shop.Host.Count(10007368));
        Assert.Equal(occupied.Length, shop.Host.Count(3));
    }

    [SkippableFact]
    public async Task Actual_archive_water_purchase_resolves_and_persists_one_item_for_3600()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        var result = await shop.Buy(CeraShopTestContent.Entry());
        Assert.True(result.Receipt is not null, result.Detail);
        Assert.Equal(96400, shop.Cera);
        Assert.Equal(1, shop.Host.Count(3));
        Assert.Equal(1, shop.Host.Scalar("SELECT COUNT(*) FROM item_slots"));
    }

    [SkippableFact]
    public async Task Actual_archive_avatar_pet_artifact_and_package_pass_through_production_sources_and_storage()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        var result = await shop.Buy(CeraShopTestContent.Entry(120000, 1), CeraShopTestContent.Entry(105016),
            CeraShopTestContent.Entry(101075), CeraShopTestContent.Entry(102425));
        Assert.True(result.Receipt is not null, result.Detail);
        Assert.Equal(73020, shop.Cera);
        Assert.Equal(1, shop.Host.Count(39032));
        Assert.Equal(1, shop.Host.Count(63022));
        Assert.Equal(1, shop.Host.Count(2747175));
        Assert.Equal(1, shop.Host.Count(2683470));
        Assert.Equal(1, shop.Host.Count(2683473));
        Assert.Equal(0, shop.Host.Count(2683477));
        Assert.Equal(1, Assert.Single(result.Receipt!.Pets).FieldAfterValue);
        var avatar = Assert.Single(result.Receipt.Updates.Single(update => update.ContainerType == InventoryContainerType.Avatar).Items);
        Assert.Equal(shop.Host.Clock.GetUtcNow().AddDays(7).ToUnixTimeSeconds(), avatar.AvatarDetail!.ExpireDate);
    }

    [Fact]
    public async Task First_success_delivery_observes_the_committed_wallet_and_items()
    {
        using var shop = new CeraShopFixture();
        shop.Host.Sender.BeforeSend = () => { Assert.Equal(96400, shop.Cera); Assert.Equal(1, shop.Host.Count(3)); };
        await shop.Handle(CeraShopTestContent.Input(CeraShopTestContent.Entry()));
        Assert.Equal(3, shop.Host.Sender.Messages.Count);
        Assert.Equal(110000, Assert.IsType<CeraShopPurchaseResponse>(shop.Host.Sender.Messages[0]).ProductId);
        var wallet = shop.Encode(shop.Host.Sender.Messages[1]);
        Assert.Equal(0x0035, BinaryPrimitives.ReadUInt16LittleEndian(wallet.AsSpan(1)));
        Assert.Equal(96400, BinaryPrimitives.ReadInt32LittleEndian(wallet.AsSpan(GameServerFrameEncoder.HeaderLength + 1)));
        var delta = shop.Encode(shop.Host.Sender.Messages[2]);
        Assert.Equal(0x000e, BinaryPrimitives.ReadUInt16LittleEndian(delta.AsSpan(1)));
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(delta.AsSpan(GameServerFrameEncoder.HeaderLength + 5)));
    }

    [Fact]
    public async Task Send_failure_does_not_repeat_or_undo_the_committed_purchase()
    {
        using var shop = new CeraShopFixture();
        shop.Host.Sender.BeforeSend = () => throw new IOException("closed socket");
        await shop.Handle(CeraShopTestContent.Input(CeraShopTestContent.Entry()));
        Assert.Equal(1, shop.Host.Sender.Attempts);
        Assert.Equal(96400, shop.Cera);
        Assert.Equal(1, shop.Host.Count(3));
    }

    [Fact]
    public async Task Invalid_and_failed_purchases_send_only_failure_then_the_same_handler_can_purchase()
    {
        using var shop = new CeraShopFixture();
        await shop.Handle(new(0, 0, 0, [], "Truncated entry"));
        Assert.Equal(1, Assert.IsType<CeraShopPurchaseResponse>(Assert.Single(shop.Host.Sender.Messages)).ErrorCode);
        Assert.Equal(100000, shop.Cera);
        shop.Host.Sender.Messages.Clear();
        shop.Host.Sql("CREATE TRIGGER fail_item BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT,'forced item failure'); END;");
        await shop.Handle(CeraShopTestContent.Input(CeraShopTestContent.Entry()));
        Assert.Null(Assert.IsType<CeraShopPurchaseResponse>(Assert.Single(shop.Host.Sender.Messages)).ProductId);
        Assert.Equal(100000, shop.Cera);
        Assert.Equal(0, shop.Host.Count(3));
        shop.Host.Sql("DROP TRIGGER fail_item");
        shop.Host.Sender.Messages.Clear();
        await shop.Handle(CeraShopTestContent.Input(CeraShopTestContent.Entry()));
        Assert.Equal(110000, Assert.IsType<CeraShopPurchaseResponse>(shop.Host.Sender.Messages[0]).ProductId);
        Assert.Equal(96400, shop.Cera);
    }

    [Fact]
    public async Task Receipt_from_a_replaced_selection_is_not_delivered_to_the_new_selection()
    {
        using var shop = new CeraShopFixture();
        var result = await shop.Buy(CeraShopTestContent.Entry());
        shop.Host.Select(2);
        await shop.Host.Services.GetRequiredService<INotificationBus>().PublishAsync(
            new NotificationAudience.TheseCharacters([result.Receipt!.Session.CharacterId]),
            new CeraShopPurchaseCompleted(result.Receipt), default);
        Assert.Empty(shop.Host.Sender.Messages);
        Assert.Equal(96400, shop.Cera);
    }

    [Fact]
    public async Task Water_purchase_commits_96400_and_one_item_and_leaves_skills_and_other_owners_unchanged()
    {
        using var shop = new CeraShopFixture();
        shop.Host.Sql("INSERT INTO accounts(account_id,m_id,cera) VALUES(2,'other-cera',7654); INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) VALUES(1,'pve',0,0,1,10); UPDATE characters SET bonus_sp=7,bonus_tp=8 WHERE character_id=1;");
        var result = await shop.Buy(CeraShopTestContent.Entry());
        Assert.NotNull(result.Receipt);
        Assert.Equal(96400, shop.Cera);
        Assert.Equal(1, shop.Host.Count(3));
        Assert.Equal(0, shop.Host.Scalar("SELECT token_cera+happy_token_cera FROM accounts WHERE account_id=1"));
        Assert.Equal(7654, shop.Host.Scalar("SELECT cera FROM accounts WHERE account_id=2"));
        Assert.Equal(10, shop.Host.Scalar("SELECT level FROM character_skills WHERE character_id=1"));
        Assert.Equal(15, shop.Host.Scalar("SELECT bonus_sp+bonus_tp FROM characters WHERE character_id=1"));
        Assert.Equal(100000, shop.Host.Gold);
        Assert.Equal((3, 1), (Assert.Single(Assert.Single(result.Receipt.Updates).Items).Core.ItemId,
            Assert.Single(Assert.Single(result.Receipt.Updates).Items).Core.Value));
    }

    [Fact]
    public async Task Equipment_avatar_pet_artifact_and_physical_package_reload_as_complete_instances()
    {
        using var shop = new CeraShopFixture();
        var result = await shop.Buy(CeraShopTestContent.Entry(200003), CeraShopTestContent.Entry(120000, 1),
            CeraShopTestContent.Entry(105016), CeraShopTestContent.Entry(101075), CeraShopTestContent.Entry(102805));
        Assert.NotNull(result.Receipt);
        Assert.Equal(71020, shop.Cera);
        var stored = await shop.Host.Services.GetRequiredService<ICharacterInventoryEntryDomainService>()
            .LoadAsync(shop.Host.Session.AccountId, shop.Host.Session.CharacterId, default);
        var equipment = Assert.Single(stored.Containers.Single(container => container.ContainerType == InventoryContainerType.Main).Items,
            item => item.Core.ItemId == 3000);
        Assert.NotNull(equipment.Identity);
        Assert.Equal((45, 1), ((int)equipment.Core.Durability, (int)equipment.Core.SealFlag));
        var avatar = Assert.Single(stored.Containers.Single(container => container.ContainerType == InventoryContainerType.Avatar).Items);
        Assert.NotNull(avatar.Identity);
        Assert.Equal(1, avatar.Core.Attribute);
        Assert.Equal(shop.Host.Clock.GetUtcNow().AddDays(7).ToUnixTimeSeconds(), avatar.AvatarDetail!.ExpireDate);
        Assert.Equal(30, avatar.AvatarDetail.JewelSocket.Length);
        var pet = Assert.Single(stored.Containers.Single(container => container.ContainerType == InventoryContainerType.Pet).Items,
            item => item.Core.ItemKind == 5);
        Assert.Equal(pet.Identity!.Value.Value, pet.Core.Value);
        Assert.Equal(pet.Core.Value, Assert.Single(result.Receipt.Pets).PetKey);
        Assert.Equal(1, result.Receipt.Pets[0].FieldAfterValue);
        Assert.Equal(63022, pet.Core.ItemId);
        Assert.Equal(1, shop.Host.Count(2683470));
        Assert.Equal(1, shop.Host.Count(2683473));
        Assert.Equal(0, shop.Host.Count(2683477));
    }

    [Fact]
    public async Task Concurrent_purchases_use_latest_balances_and_accumulate_one_stack()
    {
        using var shop = new CeraShopFixture();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => shop.Buy(CeraShopTestContent.Entry()))));
        Assert.All(results, result => Assert.NotNull(result.Receipt));
        Assert.Equal(85600, shop.Cera);
        Assert.Equal(4, shop.Host.Count(3));
        Assert.Equal(1, shop.Host.Scalar("SELECT COUNT(*) FROM item_slots"));
    }

    [Theory]
    [InlineData(103945)]
    [InlineData(900003)]
    [InlineData(999999)]
    public async Task A_later_unavailable_item_never_charges_or_delivers_the_first(int product)
    {
        using var shop = new CeraShopFixture();
        var result = await shop.Buy(CeraShopTestContent.Entry(), CeraShopTestContent.Entry(product));
        Assert.Null(result.Receipt);
        Assert.Equal(100000, shop.Cera);
        Assert.Equal(0, shop.Host.Count(3));
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM items"));
    }

    [Fact]
    public async Task Insufficient_currency_and_a_full_bag_have_no_durable_side_effect()
    {
        using var shop = new CeraShopFixture();
        shop.Host.Sql("UPDATE accounts SET cera=3599 WHERE account_id=1");
        Assert.Equal(CeraShopRejection.InsufficientFunds, (await shop.Buy(CeraShopTestContent.Entry())).Rejection);
        Assert.Equal(3599, shop.Cera);
        shop.Host.Sql("UPDATE accounts SET cera=100000 WHERE account_id=1");
        foreach (var slot in new[] { 3, 4, 5, 6, 7, 8, 65, 66 }) await shop.Host.Seed(slot, 3, 10);
        Assert.Equal(CeraShopRejection.NoRoom, (await shop.Buy(CeraShopTestContent.Entry())).Rejection);
        Assert.Equal(100000, shop.Cera);
        Assert.Equal(80, shop.Host.Count(3));
    }

    [Fact]
    public async Task A_store_failure_after_the_first_grant_rolls_back_account_gold_and_all_items()
    {
        using var shop = new CeraShopFixture();
        shop.Content.Products[110000] = shop.Content.Products[110000] with { Price = new(100, 0, 3600, 0, []) };
        shop.Host.Sql("CREATE TRIGGER fail_pet BEFORE INSERT ON item_slots WHEN NEW.list_type=7 BEGIN SELECT RAISE(ABORT,'forced pet failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => shop.Buy(CeraShopTestContent.Entry(), CeraShopTestContent.Entry(105016)));
        Assert.Equal(100000, shop.Cera);
        Assert.Equal(100000, shop.Host.Gold);
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM items"));
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM item_slots"));
    }

    [Fact]
    public async Task Zero_affected_currency_rows_abort_the_transaction_before_delivery()
    {
        using var shop = new CeraShopFixture();
        shop.Host.Sql("CREATE TRIGGER ignore_wallet BEFORE UPDATE OF cera ON accounts BEGIN SELECT RAISE(IGNORE); END;");
        await Assert.ThrowsAsync<CeraShopPurchaseRejectedException>(() => shop.Buy(CeraShopTestContent.Entry()));
        Assert.Equal(100000, shop.Cera);
        Assert.Equal(0, shop.Host.Count(3));
    }

    [Fact]
    public async Task Old_selection_and_mismatched_persisted_ownership_cannot_purchase()
    {
        using var shop = new CeraShopFixture();
        var old = shop.Host.Session;
        shop.Host.Select(2);
        Assert.Equal(CeraShopRejection.NoSession, (await shop.Command(old, CeraShopTestContent.Entry())).Rejection);
        shop.Host.Sql("INSERT INTO accounts(account_id,m_id) VALUES(2,'actual-owner'); UPDATE characters SET account_id=2 WHERE character_id=2;");
        Assert.Equal(CeraShopRejection.NoSession, (await shop.Buy(CeraShopTestContent.Entry())).Rejection);
        Assert.Equal(100000, shop.Cera);
    }
}

internal sealed class CeraShopFixture : IDisposable
{
    public CeraShopTestContent Content { get; } = new();
    public ShopFixture Host { get; }
    public int Cera => Host.Scalar("SELECT cera FROM accounts WHERE account_id=1");
    public CeraShopFixture(bool useArchive = false, Action<IServiceCollection>? configure = null)
    {
        Host = new(services =>
        {
            if (useArchive)
            {
                services.AddSingleton<ICeraShopDefinitionSource, PvfCeraShopDefinitionSource>();
                services.AddSingleton<IItemPlacementDefinitionSource, PvfItemPlacementDefinitionSource>();
            }
            else
            {
                services.AddSingleton<ICeraShopDefinitionSource>(Content);
                services.AddSingleton<IItemPlacementDefinitionSource>(Content);
            }
            configure?.Invoke(services);
        });
        Host.Sql("UPDATE accounts SET cera=100000; UPDATE characters SET level=85;");
        foreach (var type in (InventoryContainerType[])[InventoryContainerType.Avatar, InventoryContainerType.Pet])
            Host.Services.GetRequiredService<ICharacterContainerDomainService>().Register(Host.Session.CharacterId,
                new PersistentItemContainer(ContainerId.ForCharacter(Host.Session.CharacterId, type), Host.Session.CharacterId, new(type, 0),
                    Host.Services.GetRequiredService<IItemDomainStore>(), Host.Services.GetRequiredService<IDomainTransactionAccessor>(),
                    Host.Services.GetRequiredService<IEntryInventoryMutationRepository>(), Host.Services.GetRequiredService<IItemPlacementRuleService>(),
                    Host.Services.GetRequiredService<IItemArrivalRuleService>(), Host.Services.GetRequiredService<IItemSlotRuleService>()));
    }
    public Task<CeraShopPurchaseResult> Buy(params CeraShopPurchaseEntry[] entries) => Command(Host.Session, entries);
    public ValueTask Handle(CeraShopPurchaseInput input) => Host.Services.GetRequiredService<CeraShopPurchaseHandler>().HandleAsync(
        new(Host.Connection), new CeraShopPurchaseRequest(GameMessageMetadata.Empty, input), default);
    public byte[] Encode(IServerNetworkMessage message)
    {
        var registration = Host.Services.GetServices<ServerMessageEncoderRegistration>().Single(value => value.MessageType == message.GetType());
        var encoder = (IServerMessageEncoder)Host.Services.GetRequiredService(registration.EncoderType);
        return Assert.Single(encoder.Encode(message));
    }
    public Task<CeraShopPurchaseResult> Command(SelectedSessionIdentity session, params CeraShopPurchaseEntry[] entries) =>
        Host.Services.GetRequiredService<ICommandBus>().SendAsync(new CeraShopPurchaseCommand(Host.OnlineUser, session,
            CeraShopTestContent.Input(entries))).AsTask();
    public void Dispose() => Host.Dispose();
}
