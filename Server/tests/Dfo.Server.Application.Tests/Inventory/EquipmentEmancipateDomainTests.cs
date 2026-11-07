using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>Requiem conversion (ticket 19) through the domain service, against the SQLite inventory.</summary>
public sealed class EquipmentEmancipateDomainTests
{
    private const int Requiem = 3000, Liberated = 3001, Book = 7000, OtherBook = 7001, Stone = 7101, Soul = 7102;
    private static readonly BusinessSlot Target = new(BusinessInventorySpace.CarriedEquipment, 0);
    private static readonly BusinessSlot Ticket = new(BusinessInventorySpace.Consumable, 0);

    [Fact]
    public async Task Conversion_spends_one_book_and_the_inputs_and_keeps_the_piece_and_its_upgrades()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var shop = await Seeded();
            var original = Piece() with { Attribute = 0x0C, AmplifyType = 2, AmplifyValue = 9, GenuineUpgrade = 3, EnchantCardId = 567 };
            await shop.SeedCore(9, original);
            var uid = TargetUid(shop);

            var result = await Apply(shop);

            Assert.Equal(EquipmentEmancipateRejection.None, result.Rejection);
            var receipt = Assert.IsType<EquipmentEmancipateReceipt>(result.Receipt);
            var expected = original with { ItemId = Liberated, Durability = 60 };
            Assert.Equal(expected, await Read(shop));
            Assert.Equal(Liberated, receipt.After.ItemId);
            Assert.Equal(uid, TargetUid(shop));
            Assert.Equal(1, shop.Count(Book));
            Assert.Equal(2, shop.Count(Stone));
            Assert.Equal(0, shop.Count(Soul));
            Assert.Equal(100000, shop.Gold);
            Assert.Contains(receipt.Consumed, x => x.Slot == Ticket && x.ItemId == Book && x.Count == 1);

            shop.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(shop.Session.CharacterId);
            shop.Select(1);
            Assert.Equal(expected, await Read(shop));
        });
    }

    [Fact]
    public async Task Missing_input_spends_nothing()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var shop = await Seeded(soul: 1);
            await shop.SeedCore(9, Piece());
            Assert.Equal(EquipmentEmancipateRejection.MaterialUnavailable, (await Apply(shop)).Rejection);
            AssertUntouched(shop, await Read(shop), soul: 1);
        });
    }

    [Fact]
    public async Task A_ticket_kind_without_a_policy_or_a_template_without_its_block_is_refused()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var shop = await Seeded();
            await shop.Seed(66, OtherBook, 1);
            await shop.SeedCore(9, Piece());
            Assert.Equal(EquipmentEmancipateRejection.NotApplicable,
                (await Apply(shop, ticketSlot: new(BusinessInventorySpace.Consumable, 1), ticketId: OtherBook)).Rejection);
            await shop.SeedCore(9, Piece() with { ItemId = Liberated, Durability = 60 });
            Assert.Equal(EquipmentEmancipateRejection.NotApplicable, (await Apply(shop, targetId: Liberated)).Rejection);
            Assert.Equal(2, shop.Count(Book));
            Assert.Equal(3, shop.Count(Stone));
        });
    }

    public static TheoryData<string, EquipmentEmancipateRejection> States => new()
    {
        { "locked", EquipmentEmancipateRejection.Locked },
        { "worn-out", EquipmentEmancipateRejection.Durability },
        { "unpurified", EquipmentEmancipateRejection.Unpurified },
        { "expired", EquipmentEmancipateRejection.Expired },
    };

    [Theory]
    [MemberData(nameof(States))]
    public async Task Target_state_refusals_spend_nothing(string state, EquipmentEmancipateRejection rejection)
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var shop = await Seeded();
            var piece = state switch
            {
                "locked" => Piece() with { EquipmentLockId = 1 },
                "worn-out" => Piece() with { Durability = 44 },
                "unpurified" => Piece() with { AmplifyType = 0x80 },
                _ => Piece() with { ExpireTime = checked((int)shop.Clock.GetUtcNow().ToUnixTimeSeconds() - 1) },
            };
            await shop.SeedCore(9, piece);
            Assert.Equal(rejection, (await Apply(shop)).Rejection);
            Assert.Equal(piece, await Read(shop));
            AssertUntouched(shop, piece);
        });
    }

    [Fact]
    public async Task Slot_and_identity_mismatches_spend_nothing()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var shop = await Seeded();
            await shop.SeedCore(9, Piece());
            Assert.Equal(EquipmentEmancipateRejection.ItemUnavailable, (await Apply(shop, targetId: Liberated)).Rejection);
            Assert.Equal(EquipmentEmancipateRejection.ItemUnavailable, (await Apply(shop, ticketId: OtherBook)).Rejection);
            Assert.Equal(EquipmentEmancipateRejection.InUse,
                (await Apply(shop, targetSlot: new(BusinessInventorySpace.WornEquipment, 0))).Rejection);
            Assert.Equal(EquipmentEmancipateRejection.InvalidRequest,
                (await Apply(shop, ticketSlot: new(BusinessInventorySpace.Material, 0))).Rejection);
            AssertUntouched(shop, await Read(shop));
        });
    }

    [Fact]
    public async Task A_failed_template_write_rolls_back_the_spent_book_and_inputs()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var shop = await Seeded();
            await shop.SeedCore(9, Piece());
            shop.Sql("CREATE TRIGGER refuse_conversion BEFORE UPDATE ON items "
                + "WHEN NEW.item_core <> OLD.item_core AND NEW.item_kind=1 "
                + "BEGIN SELECT RAISE(ABORT,'forced conversion failure'); END;");
            await Assert.ThrowsAsync<SqliteException>(() => Apply(shop).AsTask());
            AssertUntouched(shop, await Read(shop));
            Assert.Equal(Requiem, (await Read(shop))!.ItemId);
        });
    }

    [Fact]
    public async Task A_converted_piece_cannot_be_converted_again_by_a_replayed_request()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var shop = await Seeded();
            await shop.SeedCore(9, Piece());
            Assert.Equal(EquipmentEmancipateRejection.None, (await Apply(shop)).Rejection);
            Assert.Equal(EquipmentEmancipateRejection.ItemUnavailable, (await Apply(shop)).Rejection);
            Assert.Equal(1, shop.Count(Book));
            Assert.Equal(2, shop.Count(Stone));
        });
    }

    private static void AssertUntouched(ShopFixture shop, InventoryItemCoreState? piece, int soul = 2)
    {
        Assert.NotNull(piece);
        Assert.Equal(2, shop.Count(Book));
        Assert.Equal(3, shop.Count(Stone));
        Assert.Equal(soul, shop.Count(Soul));
        Assert.Equal(100000, shop.Gold);
    }

    private static async Task<ShopFixture> Seeded(int soul = 2)
    {
        var shop = new ShopFixture(services =>
        {
            var content = new Content();
            services.AddSingleton<IEquipmentEmancipateDefinitionSource>(content);
            services.AddSingleton<IEquipmentDefinitionSource>(content);
            services.AddSingleton<IItemPlacementDefinitionSource>(provider => new Placement(new NpcShopTestContent()));
        });
        await shop.Seed(65, Book, 2);
        await shop.Seed(67, Stone, 3);
        await shop.Seed(68, Soul, soul);
        return shop;
    }

    private static InventoryItemCoreState Piece() => StackableItemCore.Create(1, Requiem, 1234) with { Durability = 45 };

    private static int TargetUid(ShopFixture shop) => shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' "
        + "AND owner_id=1 AND list_type=0 AND slot_index=9");

    private static async ValueTask<InventoryItemCoreState?> Read(ShopFixture shop)
    {
        var bound = shop.Services.GetRequiredService<IBusinessInventorySlotDomainService>().Get(shop.Session.CharacterId, Target);
        return (await bound.Container.GetAsync(bound.Slot.Index, default))?.Snapshot.ToCore();
    }

    private static async ValueTask<EquipmentEmancipateResult> Apply(ShopFixture shop, int ticketId = Book, int targetId = Requiem,
        BusinessSlot? ticketSlot = null, BusinessSlot? targetSlot = null)
    {
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using var tx = scope.Begin();
        var result = await TestEffectFootprints.Run(shop.Session, cap => shop.Services.GetRequiredService<IEquipmentEmancipateDomainService>()
            .EmancipateAsync(shop.Session, ticketSlot ?? Ticket, ticketId, targetSlot ?? Target, targetId, cap, default));
        if (result.Rejection == EquipmentEmancipateRejection.None) await tx.CommitAsync(default);
        return result;
    }

    private sealed class Content : IEquipmentEmancipateDefinitionSource, IEquipmentDefinitionSource
    {
        public ValueTask<EmancipateTicketDefinition?> GetTicketAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EmancipateTicketDefinition?>(itemId switch
            {
                Book => new(19),
                OtherBook => new(7),
                _ => null,
            });

        public ValueTask<IReadOnlyList<EquipmentEmancipateDefinition>> GetEquipmentAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<EquipmentEmancipateDefinition>>(itemId == Requiem
                ? [new(7, null, [new(Stone, 9)], [new(Liberated, 1)]), new(19, null, [new(Stone, 1), new(Soul, 2)], [new(Liberated, 1)])]
                : []);

        public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(0);

        ValueTask<EquipmentDefinition?> IEquipmentDefinitionSource.GetEquipmentAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentDefinition?>(itemId switch
            {
                Requiem => new(85, 90, 6, "weapon", 45),
                Liberated => new(85, 90, 6, "weapon", 60),
                _ => null,
            });
    }

    private sealed class Placement(NpcShopTestContent inner) : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken ct) => itemId == Liberated
            ? ValueTask.FromResult<ItemPlacement?>(new(1, 1, new(BusinessInventorySpace.CarriedEquipment, 0, 1)) { WorldClass = ItemWorldClass.Equipment })
            : inner.GetAsync(itemId, ct);
    }
}
