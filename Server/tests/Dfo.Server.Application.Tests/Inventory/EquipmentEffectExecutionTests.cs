using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentEffectExecutionTests
{
    private static readonly BusinessSlot TargetSlot = new(BusinessInventorySpace.CarriedEquipment, 0);

    [Fact]
    public async Task Registered_inventory_effect_without_preview_is_rejected_before_any_write()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = new ShopFixture(services => services.AddSingleton<IEffectHandler, UnpreviewedHandler>());
            await using var tx = Scope(f).Begin();
            await Assert.ThrowsAsync<NotSupportedException>(() => Apply(f, [new SpendGoldEffect(100), new UnpreviewedEffect()]).AsTask());
            await tx.CommitAsync(default);
            Assert.Equal(100000, f.Gold);
        });
    }

    [Fact]
    public async Task Multiple_equipment_effects_keep_the_uid_and_each_others_fields()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = new ShopFixture();
            var core = StackableItemCore.Create(1, 3000, 1234) with { Attribute = 0xA0, Durability = 20, EnchantUpgradeCount = 7 };
            await f.SeedCore(9, core);
            await f.SeedCore(10, core);
            await using var tx = Scope(f).Begin();
            var target = await Target(f);
            var result = await Apply(f, [new SetEquipmentUpgradeEffect(target, 12),
                new SetEquipmentEnchantmentEffect(target, 7654), new SetEquipmentDimensionalEffect(target, 2, 25)]);
            var final = result.Changes<EquipmentEffectChange>().Last().After!;
            Assert.Equal(target.Reference.ItemUid, final.ItemUid);
            Assert.Equal((byte)0xAC, final.Attribute);
            Assert.Equal(7654, final.EnchantCardId);
            Assert.Equal((byte)2, final.AmplifyType);
            Assert.Equal((ushort)25, final.AmplifyValue);
            Assert.Equal((byte)7, final.EnchantUpgradeCount);
            Assert.Equal((ushort)20, final.Durability);
            await tx.CommitAsync(default);
            Assert.Equal(core, (await f.Services.GetRequiredService<IBusinessInventorySlotDomainService>()
                .Get(f.Session.CharacterId, TargetSlot with { Index = 1 }).Container.GetAsync(1, default))!.Snapshot.ToCore());
        });
    }

    [Fact]
    public async Task Account_material_preview_uses_cumulative_debits_and_rewards()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = new ShopFixture();
            await f.SeedShared(3037, 2147483647, 1);
            await using var tx = Scope(f).Begin();
            var result = await Apply(f, [new ConsumeFromContainerEffect(new(BusinessInventorySpace.SharedMaterial, 4), 3037, 20),
                new GrantStackableEffect(3037, 10),
                new GrantStackableEffect(3037, 10)]);
            Assert.Equal(int.MaxValue, (await f.Shared().GetAsync(4, default))!.Count);
            await tx.CommitAsync(default);
            Assert.Equal(int.MaxValue, f.SharedCount(3037, 1));
        });
    }

    [Fact]
    public async Task Combined_account_consumption_is_rejected_before_any_write_even_when_caller_commits()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = new ShopFixture();
            await f.SeedShared(3037, 5, 1);
            await using var tx = Scope(f).Begin();
            await Assert.ThrowsAsync<InventoryEffectException>(() => Apply(f, [
                new ConsumeFromContainerEffect(new(BusinessInventorySpace.SharedMaterial, 4), 3037, 3),
                new ConsumeFromContainerEffect(new(BusinessInventorySpace.SharedMaterial, 4), 3037, 3)]).AsTask());
            await tx.CommitAsync(default);
            Assert.Equal(5, f.SharedCount(3037, 1));
        });
    }

    [Fact]
    public async Task Failed_later_equipment_commit_rolls_back_prior_gold_material_and_field_effects()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = new ShopFixture();
            await f.SeedCore(9, StackableItemCore.Create(1, 3000, 1234));
            await f.Seed(65, 2000, 5);
            var original = f.StoredCore(9);
            f.Sql("CREATE TRIGGER refuse_effect_equipment BEFORE UPDATE ON items WHEN substr(OLD.item_core,1,1)=X'01' "
                + "BEGIN SELECT RAISE(ABORT,'forced equipment effects failure'); END;");
            await using (var tx = Scope(f).Begin())
            {
                var target = await Target(f);
                await Assert.ThrowsAsync<SqliteException>(async () =>
                {
                    await Apply(f, [new SpendGoldEffect(100),
                        new ConsumeFromContainerEffect(new(BusinessInventorySpace.Consumable, 0), 2000, 2),
                        new SetEquipmentUpgradeEffect(target, 12), new SetEquipmentEnchantmentEffect(target, 7654)]);
                    await tx.CommitAsync(default);
                });
            }
            Assert.Equal(original, f.StoredCore(9));
            Assert.Equal(100000, f.Gold);
            Assert.Equal(5, f.Count(2000));
        });
    }

    [Fact]
    public async Task Old_equipment_identity_cannot_be_modified_after_destruction_and_reusing_its_slot()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = new ShopFixture();
            await f.SeedCore(3, StackableItemCore.Create(1, 3000, 1234));
            await f.Seed(121, 1000, 1000, 3);
            await f.Seed(122, 1000, 1000, 3);
            for (var slot = 4; slot <= 8; slot++) await f.Seed(slot, 2000, 1);
            await using var tx = Scope(f).Begin();
            var instance = (await f.Services.GetRequiredService<IEquipmentDomainService>().Of(f.Session.CharacterId)
                .FindAtAsync(new(BusinessInventorySpace.QuickSlot, 0), default))!;
            var target = new EquipmentEffectTarget(instance.Reference, 3000);
            await Assert.ThrowsAsync<InventoryEffectException>(() => Apply(f,
                [new DestroyEquipmentEffect(target), new GrantStackableEffect(1000, 12),
                    new SetEquipmentUpgradeEffect(target, 12)]).AsTask());
            await tx.CommitAsync(default);
            Assert.NotNull(await f.Durability(InventoryContainerType.Main, 3));
            Assert.Equal(2000, f.Count(1000));
        });
    }

    private static DomainTransactionScope Scope(ShopFixture f) => (DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>();
    private static ValueTask<ApplyEffectsResult> Apply(ShopFixture f, IReadOnlyList<DomainEffect> effects) =>
        Dfo.Server.Application.Tests.Support.TestEffectFootprints.Run(f.Session, footprint =>
            f.Services.GetRequiredService<IApplyEffectsDomainService>().ApplyAsync(
                new(f.Session.AccountId, f.Session.CharacterId, f.Clock.GetUtcNow(), footprint, f.Session), effects, default));
    private static async ValueTask<EquipmentEffectTarget> Target(ShopFixture f)
    {
        var instance = (await f.Services.GetRequiredService<IEquipmentDomainService>().Of(f.Session.CharacterId)
            .FindAtAsync(TargetSlot, default))!;
        return new(instance.Reference, 3000);
    }
    private sealed record UnpreviewedEffect : InventoryEffect;
    private sealed class UnpreviewedHandler : EffectHandler<UnpreviewedEffect>
    {
        public override ValueTask ValidateAsync(EffectExecutionContext context, UnpreviewedEffect effect, CancellationToken ct) => ValueTask.CompletedTask;
        public override ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, UnpreviewedEffect effect, CancellationToken ct) =>
            ValueTask.FromResult<EffectChange>(new GoldEffectChange(0));
    }
}
