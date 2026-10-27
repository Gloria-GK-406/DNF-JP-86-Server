using Dfo.Server.Testing;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Content;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Application.Users;
using Dfo.Server.Abstractions.World;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class NameTagPurchaseTests
{
    private const int First = 901001;
    private const int Second = 901002;
    private static CeraShopFixture Synthetic()
    {
        var shop = new CeraShopFixture(configure: services =>
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService, NameTagCurrentView>());
        foreach (var (product, item, days, cost) in new[] { (First, 100330501, 30, 600), (Second, 2681313, 7, 300) })
        {
            shop.Content.Add(product, item, CeraShopItemKind.Service, cost);
            shop.Content.Products[product] = shop.Content.Products[product] with { Category = "[visual]", ServiceDays = days };
            shop.Content.Items[item] = shop.Content.Items[item] with { IsNameTag = true };
        }
        return shop;
    }
    private static ICharacterNameTagDomainService Tags(CeraShopFixture shop) =>
        shop.Host.Services.GetRequiredService<ICharacterNameTagDomainService>();

    [Fact]
    public async Task Same_design_renews_from_existing_end_but_switching_replaces_its_remaining_time()
    {
        using var shop = Synthetic();
        var now = shop.Host.Clock.GetUtcNow();
        await shop.Buy(CeraShopTestContent.Entry(First));
        shop.Host.Clock.Advance(TimeSpan.FromDays(2));
        await shop.Buy(CeraShopTestContent.Entry(First));
        Assert.Equal(new CharacterNameTag(100330501, now.AddDays(60).ToUnixTimeSeconds()),
            await Tags(shop).ReadEffectiveAsync(shop.Host.Session.AccountId, shop.Host.Session.CharacterId, default));
        await shop.Buy(CeraShopTestContent.Entry(Second));
        Assert.Equal(new CharacterNameTag(2681313, now.AddDays(9).ToUnixTimeSeconds()),
            await Tags(shop).ReadEffectiveAsync(shop.Host.Session.AccountId, shop.Host.Session.CharacterId, default));
        Assert.Equal(98500, shop.Cera);
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM items"));
    }

    [Fact]
    public async Task Expired_design_is_hidden_and_a_new_purchase_restarts_from_now()
    {
        using var shop = Synthetic();
        await shop.Buy(CeraShopTestContent.Entry(First));
        shop.Host.Clock.Advance(TimeSpan.FromDays(30));
        Assert.Equal(CharacterNameTag.Empty, await Tags(shop).ReadEffectiveAsync(shop.Host.Session.AccountId, shop.Host.Session.CharacterId, default));
        var now = shop.Host.Clock.GetUtcNow();
        await shop.Buy(CeraShopTestContent.Entry(First));
        Assert.Equal(now.AddDays(30).ToUnixTimeSeconds(), shop.Host.Scalar("SELECT name_tag_expire_time FROM characters WHERE character_id=1"));
    }

    [Fact]
    public async Task Name_tag_and_payment_roll_back_on_persistence_failure_and_insufficient_funds()
    {
        using var shop = Synthetic();
        await shop.Buy(CeraShopTestContent.Entry(First));
        shop.Host.Sql("CREATE TRIGGER fail_name_tag BEFORE UPDATE OF name_tag_item_id ON characters BEGIN SELECT RAISE(ABORT,'forced'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => shop.Buy(CeraShopTestContent.Entry(Second)));
        Assert.Equal(99400, shop.Cera);
        Assert.Equal(100330501, shop.Host.Scalar("SELECT name_tag_item_id FROM characters WHERE character_id=1"));
        shop.Host.Sql("DROP TRIGGER fail_name_tag; UPDATE accounts SET cera=299;");
        Assert.Equal(CeraShopRejection.InsufficientFunds, (await shop.Buy(CeraShopTestContent.Entry(Second))).Rejection);
        Assert.Equal(299, shop.Cera);
        Assert.Equal(100330501, shop.Host.Scalar("SELECT name_tag_item_id FROM characters WHERE character_id=1"));
    }

    [Fact]
    public async Task Late_cart_failure_rolls_back_name_tag_and_wallet_together()
    {
        using var shop = Synthetic();
        shop.Host.Sql("CREATE TRIGGER fail_item BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT,'forced'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => shop.Buy(CeraShopTestContent.Entry(First), CeraShopTestContent.Entry(110000)));
        Assert.Equal(100000, shop.Cera);
        Assert.Equal(0, shop.Host.Scalar("SELECT name_tag_item_id FROM characters WHERE character_id=1"));
    }

    [Fact]
    public async Task Visual_category_alone_does_not_authorize_an_unknown_item_or_unsupported_choices()
    {
        using var shop = Synthetic();
        var original = shop.Content.Items[100330501];
        shop.Content.Items[100330501] = original with { IsNameTag = false };
        Assert.Equal(CeraShopRejection.UnsupportedProduct, (await shop.Buy(CeraShopTestContent.Entry(First))).Rejection);
        shop.Content.Items[100330501] = original with { Diagnostics = [new("Invalid", "fixture", "bad declaration")] };
        Assert.Equal(CeraShopRejection.UnsupportedProduct, (await shop.Buy(CeraShopTestContent.Entry(First))).Rejection);
        shop.Content.Items[100330501] = original;
        Assert.Equal(CeraShopRejection.InvalidChoice, (await shop.Buy(CeraShopTestContent.Entry(First) with { Attribute = 1 })).Rejection);
        Assert.Equal(100000, shop.Cera);
        Assert.Equal(0, shop.Host.Scalar("SELECT name_tag_item_id FROM characters WHERE character_id=1"));
    }

    [Fact]
    public async Task Expiration_overflow_and_ignored_state_write_do_not_debit_payment()
    {
        using var shop = Synthetic();
        shop.Host.Sql($"UPDATE characters SET name_tag_item_id=100330501,name_tag_expire_time={uint.MaxValue-1L} WHERE character_id=1");
        await Assert.ThrowsAsync<Dfo.Server.Abstractions.Inventory.CeraShopPurchaseRejectedException>(() => shop.Buy(CeraShopTestContent.Entry(First)));
        Assert.Equal(100000, shop.Cera);
        shop.Host.Sql("UPDATE characters SET name_tag_item_id=0,name_tag_expire_time=0 WHERE character_id=1; CREATE TRIGGER ignore_name_tag BEFORE UPDATE OF name_tag_item_id ON characters BEGIN SELECT RAISE(IGNORE); END;");
        await Assert.ThrowsAsync<Dfo.Server.Abstractions.Inventory.CeraShopPurchaseRejectedException>(() => shop.Buy(CeraShopTestContent.Entry(First)));
        Assert.Equal(100000, shop.Cera);
    }

    [Fact]
    public async Task Online_expiration_clears_once_and_queues_only_a_committed_refresh()
    {
        using var shop = Synthetic();
        await shop.Buy(CeraShopTestContent.Entry(First));
        shop.Host.Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().EnsureInWorld(shop.Host.Session);
        shop.Host.Clock.Advance(TimeSpan.FromDays(30));
        await Dfo.Server.Application.Tests.Scheduling.TestScheduledTasks.RunActualAsync(shop.Host.Services, "character.name-tag.expiration");
        Assert.Equal(Dfo.Server.Data.Characters.CharacterNameTag.Empty, await shop.Host.Services.GetRequiredService<Dfo.Server.Abstractions.Characters.ICharacterNameTagDomainService>().ReadEffectiveAsync(shop.Host.Session.AccountId, shop.Host.Session.CharacterId, default));
        await Dfo.Server.Application.Tests.Scheduling.TestScheduledTasks.RunActualAsync(shop.Host.Services, "character.name-tag.expiration");
        Assert.Equal(0, shop.Host.Scalar("SELECT name_tag_item_id FROM characters WHERE character_id=1"));
        var pending = await shop.Host.Services.GetRequiredService<IDomainEventPersistence>().ReadDueAsync(
            "character.name-tag.expiration.notifications", shop.Host.Clock.GetUtcNow(), 100, default);
        Assert.Single(pending);
        await shop.Host.Services.GetRequiredService<DomainEventDispatcher>().DrainOnceAsync("character.name-tag.expiration.notifications");
        Assert.Equal(2, shop.Host.Sender.Messages.Count);
        Assert.Equal(0, shop.Host.Scalar("SELECT name_tag_expire_time FROM characters WHERE character_id=1"));
    }

    [Fact]
    public async Task Purchase_refresh_reaches_nearby_players_but_private_purchase_ack_stays_with_buyer()
    {
        using var shop = Synthetic();
        SelectedSessionIdentity peer = null!;
        await TestGameplay.RunAsync(shop.Host.Services, () =>
        {
            var registry = shop.Host.Services.GetRequiredService<OnlineStateRegistry>();
            var connection = new NetworkConnectionContext(new("name-tag-peer"), NetworkEndpointKind.Game, 10011);
            var user = registry.RegisterConnection(connection);
            registry.Authenticate(user.OnlineUserId, new(2));
            peer = SelectedSessionIdentity.Create(new(2), new(3), connection.ConnectionId,
                registry.GetRequiredUser(user.OnlineUserId).Generation, 10011);
            registry.Select(peer);
            registry.EnterWorld(peer);
            shop.Host.Services.GetRequiredService<ISelectedSessionDomainService>().Select(peer);
            shop.Host.Services.GetRequiredService<ITownAreaDomainService>().Enter(new TownAreaMember(peer.CharacterId,
                peer.TownAreaLease(), new(1, 0, 0), new(0, 0, 0, 0, 0), true));
            return Task.CompletedTask;
        });
        await shop.Handle(CeraShopTestContent.Input(CeraShopTestContent.Entry(First)));
        var peerMessages = shop.Host.Sender.Outbound.MessagesFor(peer.ConnectionId);
        Assert.Single(peerMessages, m => m.GetType().Name == "CurrentUserStateMessage");
        Assert.DoesNotContain(peerMessages, m => m.GetType().Name is "CeraShopPurchaseResponse" or "CeraBalanceUpdateMessage" or "ExtendedUserStateMessage");
        var buyerMessages = shop.Host.Sender.Outbound.MessagesFor(shop.Host.Session.ConnectionId);
        Assert.Single(buyerMessages, m => m.GetType().Name == "CeraShopPurchaseResponse");
        Assert.Single(buyerMessages, m => m.GetType().Name == "CeraBalanceUpdateMessage");
        Assert.Equal(99400, shop.Cera);
        Assert.Equal(100330501, (await Tags(shop).ReadEffectiveAsync(shop.Host.Session.AccountId, shop.Host.Session.CharacterId, default)).ItemId);
        Assert.Equal(2, shop.Host.Sender.Messages.Count(m => m.GetType().Name == "CurrentUserStateMessage"));
        Assert.Single(shop.Host.Sender.Messages, m => m.GetType().Name == "ExtendedUserStateMessage");
        Assert.Single(shop.Host.Sender.Messages, m => m.GetType().Name == "CeraShopPurchaseResponse");
        Assert.Single(shop.Host.Sender.Messages, m => m.GetType().Name == "CeraBalanceUpdateMessage");
    }

    [SkippableFact]
    public async Task Purchase_handler_refreshes_name_tag_after_the_purchase_ack()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true, configure: services =>
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService, NameTagCurrentView>());
        shop.Host.Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().EnsureInWorld(shop.Host.Session);
        await shop.Handle(CeraShopTestContent.Input(CeraShopTestContent.Entry(102241)));
        Assert.NotNull(await shop.Host.Services.GetRequiredService<ISelectedCharacterCurrentViewDomainService>().TryLoadAsync(shop.Host.Session, default));
        Assert.IsType<Dfo.Server.Protocol.Game.CeraShopPurchase.CeraShopPurchaseResponse>(shop.Host.Sender.Messages[0]);
        Assert.Contains(shop.Host.Sender.Messages, m => m.GetType().Name == "CurrentUserStateMessage");
        Assert.Contains(shop.Host.Sender.Messages, m => m.GetType().Name == "ExtendedUserStateMessage");
        var current = Assert.Single(shop.Host.Sender.Messages, m => m.GetType().Name == "CurrentUserStateMessage");
        var frame = shop.Encode(current);
        Assert.Equal((ushort)2, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));
        using var reader = new BinaryReader(new MemoryStream(frame));
        reader.BaseStream.Position = 15 + 3 + 38 + 2; // Header, subtype/version, A21 reserved area, character id.
        var nameLength = reader.ReadInt32();
        reader.ReadBytes(nameLength + 6); // Name, job/grow/level, PVP fields.
        var appearanceCount = reader.ReadByte();
        reader.ReadBytes(appearanceCount * 23 + 8); // Appearance parts, title, creature flags.
        Assert.Equal(100330501U, reader.ReadUInt32());
        Assert.Equal(shop.Host.Clock.GetUtcNow().AddDays(30).ToUnixTimeSeconds(), reader.ReadUInt32());
    }

    // Other character facts are fixtures; the purchased decoration always comes from the real store/domain.
    private sealed class NameTagCurrentView(ICharacterNameTagDomainService tags) : ISelectedCharacterCurrentViewDomainService
    {
        public async ValueTask<SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity session, CancellationToken ct)
        {
            var baseline = await Characters.CharacterEntryInventoryTests.CreateLifecycleResultAsync(session.AccountId, session.CharacterId.Value, 0);
            var tag = await tags.ReadEffectiveAsync(session.AccountId, session.CharacterId, ct);
            return baseline with { CharacterInventory = baseline.CharacterInventory with
            { Containers = [new(InventoryContainerType.Equipment, 0, [])] }, CharacterEntry = baseline.CharacterEntry with
            { Skills = baseline.CharacterEntry.Skills with { ProtocolMirror = new(0, 0, 0, 0) },
                Public = baseline.CharacterEntry.Public with { Identity = new(0, 0, 0), Extras = baseline.CharacterEntry.Public.Extras with
                { NameTagItemId = tag.ItemId, NameTagExpireTime = tag.ExpiresAtUnixSeconds } } } };
        }
    }

    [SkippableFact]
    public async Task Different_period_products_of_the_same_archive_design_extend_the_same_expiration()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        await shop.Buy(CeraShopTestContent.Entry(100615));
        await shop.Buy(CeraShopTestContent.Entry(100620));
        Assert.Equal(99100, shop.Cera);
        Assert.Equal(2681313, shop.Host.Scalar("SELECT name_tag_item_id FROM characters WHERE character_id=1"));
        Assert.Equal(shop.Host.Clock.GetUtcNow().AddDays(37).ToUnixTimeSeconds(),
            shop.Host.Scalar("SELECT name_tag_expire_time FROM characters WHERE character_id=1"));
    }

    [SkippableFact]
    public async Task Archive_name_tag_purchase_charges_catalog_price_and_activates_without_a_bag_item()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        var result = await shop.Buy(CeraShopTestContent.Entry(102241) with { Attribute = 255 });
        Assert.Equal(CeraShopRejection.None, result.Rejection);
        Assert.Equal(99400, shop.Cera);
        Assert.Equal(100330501, shop.Host.Scalar("SELECT name_tag_item_id FROM characters WHERE character_id=1"));
        Assert.Equal(shop.Host.Clock.GetUtcNow().AddDays(30).ToUnixTimeSeconds(),
            shop.Host.Scalar("SELECT name_tag_expire_time FROM characters WHERE character_id=1"));
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM items"));
    }
}
