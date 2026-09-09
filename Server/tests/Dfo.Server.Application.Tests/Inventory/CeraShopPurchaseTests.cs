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
        Assert.NotNull(hammer);
        Assert.NotNull(box);
        var occupied = Enumerable.Range(3, 6)
            .Concat(Enumerable.Range(hammer.SlotStart, hammer.SlotEnd - hammer.SlotStart + 1))
            .Concat(Enumerable.Range(box.SlotStart, box.SlotEnd - box.SlotStart + 1))
            .Distinct().Where(slot => slot != hammer.SlotStart).ToArray();
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
