using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>Probabilistic equipment-upgrade drawings (requiem → liberated) through the recipe domain service.</summary>
public sealed class RequiemDrawingCraftTests
{
    private const int Drawing = 1287, Requiem = 3000, Liberated = 3001, Stone = 3244, Soul = 7300;
    private static readonly BusinessSlot Piece = new(BusinessInventorySpace.CarriedEquipment, 0);

    [Fact]
    public async Task A_roll_under_the_rate_converts_the_piece_in_place_sealed_with_its_upgrades()
    {
        using var f = Fixture(roll: 999);
        var original = Weapon() with { Attribute = 0x07, AmplifyType = 2, AmplifyValue = 9, GenuineUpgrade = 3, EnchantCardId = 567 };
        await Seed(f, original);
        var uid = Uid(f);

        var receipt = await Craft(f);

        Assert.Equal(Piece, receipt.Upgraded);
        Assert.Equal(original with { ItemId = Liberated, SealFlag = 1, Durability = 60 }, await Read(f));
        Assert.Equal(uid, Uid(f));
        Assert.Equal(0, f.Count(Drawing)); Assert.Equal(5, f.Count(Stone)); Assert.Equal(0, f.Count(Soul));
        Assert.Equal(100000, f.Gold);
        Assert.Contains(receipt.Inventory.Rewards, x => x.Slot == Piece && x.ItemId == Liberated);
        Assert.Contains(Piece, receipt.Inventory.Replaced);
        Assert.DoesNotContain(receipt.Consumed, x => x.ItemId == Requiem);

        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(f.Session.CharacterId));
        f.Select(1);
        Assert.Equal(original with { ItemId = Liberated, SealFlag = 1, Durability = 60 }, await Read(f));
    }

    [Fact]
    public async Task A_roll_at_the_rate_fails_spends_the_drawing_and_inputs_keeps_the_piece_and_grants_a_soul()
    {
        using var f = Fixture(roll: 1000);
        var original = Weapon() with { Attribute = 0x07 };
        await Seed(f, original);

        var receipt = await Craft(f);

        Assert.Null(receipt.Upgraded);
        Assert.Equal(original, await Read(f));
        Assert.Equal(0, f.Count(Drawing)); Assert.Equal(5, f.Count(Stone)); Assert.Equal(1, f.Count(Soul));
        Assert.Equal(Soul, Assert.Single(receipt.Inventory.Rewards).ItemId);
    }

    public static TheoryData<string, OpeningRejection> Refusals => new()
    {
        { "missing", OpeningRejection.MissingMaterial },
        { "two", OpeningRejection.InvalidChoice },
        { "worn-out", OpeningRejection.EquipmentUnrepaired },
        { "unpurified", OpeningRejection.EquipmentUnpurified },
        { "locked", OpeningRejection.ItemUnusable },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Refused_attempts_spend_nothing(string state, OpeningRejection reason)
    {
        using var f = Fixture(roll: 0);
        var piece = state switch
        {
            "worn-out" => Weapon() with { Durability = 44 },
            "unpurified" => Weapon() with { AmplifyType = 0x80 },
            "locked" => Weapon() with { EquipmentLockId = 1 },
            _ => Weapon(),
        };
        await Seed(f, state == "missing" ? null : piece);
        if (state == "two") await f.SeedCore(10, Weapon());

        var refusal = await Assert.ThrowsAsync<OpeningRejectedException>(() => Craft(f));

        Assert.Equal(reason, refusal.Reason);
        Assert.Equal(1, f.Count(Drawing)); Assert.Equal(30, f.Count(Stone)); Assert.Equal(0, f.Count(Soul));
        if (state != "missing") Assert.Equal(piece, await Read(f));
    }

    [Fact]
    public async Task Upgrade_drawings_craft_one_at_a_time()
    {
        using var f = Fixture(roll: 0);
        await Seed(f, Weapon(), drawings: 2);
        Assert.Equal(OpeningRejection.InvalidRequest, (await Assert.ThrowsAsync<OpeningRejectedException>(() => Craft(f, 2))).Reason);
        Assert.Equal(2, f.Count(Drawing));
    }

    private static InventoryItemCoreState Weapon() => StackableItemCore.Create(1, Requiem, 1234) with { Durability = 45 };

    private static async Task Seed(ShopFixture f, InventoryItemCoreState? piece, int drawings = 1)
    {
        await f.Seed(101, Drawing, drawings);
        await f.Seed(140, Stone, 30, 3);
        if (piece is not null) await f.SeedCore(9, piece);
    }

    private static Task<RecipeCraftReceipt> Craft(ShopFixture f, int count = 1) =>
        OpeningDomainTestDriver.Recipe(f, f.Session, new(BusinessInventorySpace.Consumable, 36), count);

    private static int Uid(ShopFixture f) => f.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' "
        + "AND owner_id=1 AND list_type=0 AND slot_index=9");

    private static async ValueTask<InventoryItemCoreState?> Read(ShopFixture f)
    {
        var bound = f.Services.GetRequiredService<IBusinessInventorySlotDomainService>().Get(f.Session.CharacterId, Piece);
        return (await bound.Container.GetAsync(bound.Slot.Index, default))?.Snapshot.ToCore();
    }

    private static ShopFixture Fixture(int roll)
    {
        var content = new OpeningContent();
        content.Items[Liberated] = new(Liberated, new(1, 1, new(BusinessInventorySpace.CarriedEquipment, 0, 47))
            { WorldClass = ItemWorldClass.Equipment }, 60, true, [], null, true);
        content.Items[Soul] = new(Soul, new(2, 1000, new(BusinessInventorySpace.Consumable, 0, 1)), 0, false, [], null, true);
        var plan = new RecipePlan([new(Requiem, 1), new(Stone, 25)], [new(Liberated, 1)])
        {
            Chance = new(1000, [new(Requiem, 1), new(Soul, 1)], MaintainAbility: true),
        };
        return new ShopFixture(services =>
        {
            services.AddSingleton<IRecipeContentSource>(new Recipes(plan));
            services.AddSingleton<IEquipmentDefinitionSource>(new Equipment());
            services.AddSingleton<IRecipeChanceRollSource>(new Roll(roll));
            services.AddSingleton<IPackageOpeningDefinitionSource>(content);
            services.AddSingleton<IItemPlacementDefinitionSource>(content);
            services.AddSingleton<IItemUseRuleDefinitionSource>(content);
        });
    }

    private sealed class Recipes(RecipePlan plan) : IRecipeContentSource
    {
        public ValueTask<RecipePlan?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<RecipePlan?>(id == Drawing ? plan : null);
    }

    private sealed class Equipment : IEquipmentDefinitionSource
    {
        public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(0);
        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentDefinition?>(itemId switch
            {
                Requiem => new(85, 90, 6, "weapon", 45) { AttachType = "trade" },
                Liberated => new(85, 90, 6, "weapon", 60) { AttachType = "sealing" },
                _ => null,
            });
    }

    private sealed class Roll(int value) : IRecipeChanceRollSource
    {
        public int Next() => value;
    }
}
