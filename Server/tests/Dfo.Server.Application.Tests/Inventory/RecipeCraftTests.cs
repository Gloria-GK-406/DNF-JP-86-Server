using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.CompoundItem;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class RecipeCraftTests
{
    [Fact]
    public async Task Shared_material_output_stays_unsupported_and_cannot_consume_recipe_costs()
    {
        var content = new OpeningContent();
        content.Items[3037] = new(3037, new(11, 1000, new(BusinessInventorySpace.Material, 0, 55)), 0, false, [], null, true);
        using var f = new ShopFixture(s =>
        {
            s.AddSingleton<IRecipeContentSource>(new Recipes { Plan = new([new(3244, 25), new(3262, 100)], [new(3037, 2)]) });
            s.AddSingleton<IPackageOpeningDefinitionSource>(content);
            s.AddSingleton<IItemUseRuleDefinitionSource>(content);
            s.AddSingleton<IItemPlacementDefinitionSource>(content);
        });
        await Seed(f);
        var refusal = await Assert.ThrowsAsync<OpeningRejectedException>(() => Craft(f));
        Assert.Equal(OpeningRejection.UnsupportedContent, refusal.Reason);
        Assert.Equal(1, f.Count(1287)); Assert.Equal(25, f.Count(3244));
        Assert.Equal(100, f.SharedCount(3262)); Assert.Equal(0, f.SharedCount(3037));
    }

    [Fact]
    public async Task Event_projection_filters_only_exhausted_crystals_and_keeps_positive_balances()
    {
        using var f = Fixture(new Recipes { Plan = new([new(3262, 100), new(3037, 10)], [new(1286, 1)]) });
        await Seed(f); await f.SeedShared(3037, 20, 1);
        await EffectRequestTransport.Send(f,
            new CompoundItemRequest(new GameMessageMetadata(1, 0x19, 0, 7)
                { InboundOrdinal = 1, SequenceIsFresh = true }, 101, 0, 1));
        var receipt = Assert.Single(f.Sender.Messages.OfType<CompoundItemResponse>()).Receipt!;
        Assert.Equal(2, receipt.Consumed.Count(x => x.Slot.Space == BusinessInventorySpace.SharedMaterial)); // Publishers keep complete event facts, including zero.
        var entries = f.Sender.Messages.SelectMany(message => ResourceEntries(f, message)).ToArray();
        Assert.DoesNotContain(entries, entry => BitConverter.ToInt32(entry, 2) == 3262);
        var clear = Assert.Single(entries, entry => BitConverter.ToInt32(entry, 2) == 3037);
        Assert.Equal(10, BitConverter.ToInt32(clear, 6));
    }

    [Fact]
    public async Task Last_crystal_is_consumed_by_ack_without_a_zero_quantity_notification_and_next_craft_fails()
    {
        using var f = Fixture(); await Seed(f, 2);
        await f.SeedShared(3262, 100, 1);
        await EffectRequestTransport.Send(f, new CompoundItemRequest(new GameMessageMetadata(1, 0x19, 0, 7)
            { InboundOrdinal = 1, SequenceIsFresh = true }, 101, 0, 1));
        var receipt = Assert.Single(f.Sender.Messages.OfType<CompoundItemResponse>()).Receipt!;
        Assert.Contains(new BusinessSlot(BusinessInventorySpace.SharedMaterial, 5), receipt.Inventory.Cleared);
        Assert.DoesNotContain(f.Sender.Messages, message => ResourceEntries(f, message)
            .Any(entry => BitConverter.ToInt32(entry, 2) == 3262 && BitConverter.ToInt32(entry, 6) == 0));
        await EffectRequestTransport.Send(f, new CompoundItemRequest(new GameMessageMetadata(1, 0x19, 0, 8)
            { InboundOrdinal = 2, SequenceIsFresh = true }, 101, 0, 1));
        Assert.Null(f.Sender.Messages.OfType<CompoundItemResponse>().Last().Receipt);
        Assert.Equal(1, f.Count(1286)); Assert.Equal(1, f.Count(1287)); Assert.Equal(25, f.Count(3244));
        Assert.Equal(0, f.SharedCount(3262, 1));
    }

    private static IEnumerable<byte[]> ResourceEntries(ShopFixture f, IServerNetworkMessage message)
    {
        var registration = f.Services.GetServices<Dfo.Server.Abstractions.Networking.ServerMessageEncoderRegistration>()
            .Single(x => x.MessageType == message.GetType());
        var encoder = (Dfo.Server.Abstractions.Networking.IServerMessageEncoder)f.Services.GetRequiredService(registration.EncoderType);
        foreach (var frame in encoder.Encode(message))
        {
            if (frame[0] != 0 || BitConverter.ToUInt16(frame, 1) != 0x0E) continue;
            for (var index = 0; index < BitConverter.ToUInt16(frame, 16); index++)
                yield return frame.AsSpan(18 + index * 101, 101).ToArray();
        }
    }

    private sealed class Recipes : IRecipeContentSource
    {
        public RecipePlan? Plan { get; set; } = new([new(3244, 25), new(3262, 100)], [new(1286, 1)]);
        public ValueTask<RecipePlan?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult(id == 1287 ? Plan : null);
    }
    private static ShopFixture Fixture(Recipes? recipes = null, int limit = 1000, Action<IServiceCollection>? configure = null)
    {
        var content = new OpeningContent();
        content.Items[1286] = new(1286, new(2, limit, new(BusinessInventorySpace.Consumable, 0, 1)), 0, false, [], null, true);
        return new ShopFixture(services =>
        {
            services.AddSingleton<IRecipeContentSource>(recipes ?? new Recipes());
            services.AddSingleton<IPackageOpeningDefinitionSource>(content);
            services.AddSingleton<IItemPlacementDefinitionSource>(content);
            services.AddSingleton<IItemUseRuleDefinitionSource>(content);
            configure?.Invoke(services);
        });
    }
    internal static ShopFixture ProtocolFixture(Action<IServiceCollection> configure) => Fixture(configure: configure);
    private static Task<RecipeCraftReceipt> Craft(ShopFixture f, int count = 1) =>
        OpeningDomainTestDriver.Recipe(f, f.Session, new(BusinessInventorySpace.Consumable, 36), count);
    internal static async Task Seed(ShopFixture f, int count = 1)
    {
        await f.Seed(101, 1287, count); await f.Seed(140, 3244, 25 * count, 3);
        await f.SeedShared(3262, 100 * count, 1);
    }
    [SkippableFact]
    public async Task Real_pvf_crafting_enforces_level_and_persists_book_with_real_placement()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var f = new ShopFixture(s => s.AddSingleton<IItemPlacementDefinitionSource>(sp =>
            new PvfItemPlacementDefinitionSource(sp.GetRequiredService<PvfArchiveProvider>())), useArchive: true);
        await Seed(f);
        Assert.Equal(OpeningRejection.ItemUnusable, (await Assert.ThrowsAsync<OpeningRejectedException>(() => Craft(f))).Reason);
        Assert.Equal(1, f.Count(1287)); Assert.Equal(100, f.SharedCount(3262, 1));
        f.Sql($"UPDATE characters SET level=85,exp={f.ExperienceForLevel(85)} WHERE character_id=1");
        var result = await Craft(f);
        Assert.Equal(1, f.Count(1286)); Assert.Equal(0, f.Count(1287)); Assert.Equal(0, f.Count(3244));
        Assert.Equal(1286, Assert.Single(result.Inventory.Rewards).ItemId);
    }
    [Fact]
    public async Task Success_persists_all_costs_and_book_without_charging_gold()
    {
        using var f = Fixture(); await Seed(f);
        var receipt = await Craft(f);
        Assert.Equal(0, f.Count(1287)); Assert.Equal(0, f.Count(3244)); Assert.Equal(1, f.Count(1286));
        Assert.Equal(0, f.SharedCount(3262, 1)); Assert.Equal(100000, f.Gold);
        Assert.Equal(3, receipt.Consumed.Length); Assert.Equal(1, Assert.Single(receipt.Inventory.Rewards).Count);
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(f.Session.CharacterId));
        f.Select(1);
        var bag = f.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>().GetStore(f.Session.CharacterId, BusinessInventoryStoreKind.Item)
            .GetContainer(Assert.Single(receipt.Inventory.Rewards).Slot!.Value.Space);
        Assert.Equal(1, (await bag.GetAllAsync(default)).Where(x => x.ItemId == 1286).Sum(x => x.Count));
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Missing_material_never_debits_any_asset(bool account)
    {
        using var f = Fixture(); await f.Seed(101, 1287, 1); await f.Seed(140, 3244, account ? 25 : 24, 3);
        await f.SeedShared(3262, (account ? 99 : 100), 1);
        var rejected = await Assert.ThrowsAsync<OpeningRejectedException>(() => Craft(f));
        Assert.Equal(OpeningRejection.MissingMaterial, rejected.Reason); Assert.Equal(1, f.Count(1287));
        Assert.Equal(account ? 25 : 24, f.Count(3244)); Assert.Equal(account ? 99 : 100, f.SharedCount(3262, 1));
        Assert.Equal(0, f.Count(1286));
    }
    [Fact]
    public async Task Materials_across_piles_are_combined_and_batch_scales_costs()
    {
        using var f = Fixture(); await Seed(f, 2); await f.Seed(140, 3244, 20, 3); await f.Seed(141, 3244, 30, 3);
        await Craft(f, 2); Assert.Equal(2, f.Count(1286)); Assert.Equal(0, f.Count(3244)); Assert.Equal(0, f.Count(1287));
    }
    [Fact]
    public async Task No_room_rolls_back_account_debit_as_well_as_physical_costs()
    {
        using var f = Fixture(); await Seed(f, 2);
        await f.Seed(65, 6000, 1); await f.Seed(66, 6000, 1);
        for (var slot = 3; slot <= 8; slot++) await f.Seed(slot, 6000, 1);
        var rejected = await Assert.ThrowsAsync<OpeningRejectedException>(() => Craft(f));
        Assert.Equal(OpeningRejection.NoRoom, rejected.Reason); Assert.Equal(200, f.SharedCount(3262, 1));
        Assert.Equal(2, f.Count(1287)); Assert.Equal(50, f.Count(3244)); Assert.Equal(0, f.Count(1286));
    }
    [Fact]
    public async Task The_slot_freed_by_the_drawing_can_receive_the_book()
    {
        var content = new OpeningContent(); content.Items[1286] = new(1286, new(2, 1000,
            new(BusinessInventorySpace.Consumable, 36, 36)), 0, false, [], null, true);
        using var f = new ShopFixture(s => { s.AddSingleton<IRecipeContentSource>(new Recipes());
            s.AddSingleton<IPackageOpeningDefinitionSource>(content); s.AddSingleton<IItemUseRuleDefinitionSource>(content);
            s.AddSingleton<IItemPlacementDefinitionSource>(content); });
        f.Sql("INSERT INTO character_container_state(character_id,list_type,list_param16) VALUES(1,0,8)");
        await Seed(f); for (var i = 3; i <= 8; i++) await f.Seed(i, 6000, 1);
        var receipt = await Craft(f); Assert.Equal(new(BusinessInventorySpace.Consumable, 36), Assert.Single(receipt.Inventory.Rewards).Slot);
    }
    [Fact]
    public async Task Expired_inputs_and_insufficient_drawing_count_fail_before_debit()
    {
        using var f = Fixture(); await Seed(f); await f.SeedCore(140, StackableItemCore.Create(3, 3244, 25)
            with { ExpireTime = checked((int)f.Clock.GetUtcNow().AddSeconds(-1).ToUnixTimeSeconds()) });
        Assert.Equal(OpeningRejection.MissingMaterial, (await Assert.ThrowsAsync<OpeningRejectedException>(() => Craft(f))).Reason);
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Craft(f, 2));
        Assert.Equal(100, f.SharedCount(3262, 1)); Assert.Equal(1, f.Count(1287));
    }
    [Fact]
    public async Task Storage_failure_rolls_back_and_sends_no_success_ack()
    {
        using var f = Fixture(); await Seed(f);
        f.Sql("CREATE TRIGGER reject_recipe BEFORE INSERT ON item_slots BEGIN SELECT RAISE(ABORT,'recipe-test'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => Craft(f));
        Assert.Equal(1, f.Count(1287)); Assert.Equal(25, f.Count(3244));
        Assert.Equal(100, f.SharedCount(3262, 1)); Assert.Empty(f.Sender.Messages);
    }
    [Fact]
    public async Task Replay_refreshes_absolute_state_without_another_delta_ack_or_craft()
    {
        using var f = Fixture(); await Seed(f, 2);
        f.Sender.BeforeSend = () => { Assert.Equal(1, f.Count(1286)); Assert.Equal(100, f.SharedCount(3262, 1)); };
        await EffectRequestTransport.Send(f, new CompoundItemRequest(new GameMessageMetadata(1, 0x19, 0, 7)
            { InboundOrdinal = 1, SequenceIsFresh = true }, 101, 0, 1));
        await EffectRequestTransport.Send(f, new CompoundItemRequest(new GameMessageMetadata(1, 0x19, 0, 7)
            { InboundOrdinal = 2, SequenceIsFresh = false }, 101, 0, 1));
        Assert.Single(f.Sender.Messages.OfType<CompoundItemResponse>()); Assert.Equal(1, f.Count(1286)); Assert.Equal(1, f.Count(1287));
    }
    [Fact]
    public async Task A_late_replay_refreshes_current_balances_instead_of_the_original_receipt()
    {
        using var f = Fixture(); await Seed(f, 2);
        await EffectRequestTransport.Send(f, new CompoundItemRequest(new GameMessageMetadata(1, 0x19, 0, 7)
            { InboundOrdinal = 1, SequenceIsFresh = true }, 101, 0, 1));
        await EffectRequestTransport.Send(f, new CompoundItemRequest(new GameMessageMetadata(1, 0x19, 0, 8)
            { InboundOrdinal = 2, SequenceIsFresh = true }, 101, 0, 1));
        var prior = Assert.IsType<CompoundItemResponse>(f.Sender.Messages[0]).Receipt!;
        var refreshed = await f.Services.GetRequiredService<IRecipeCraftDomainService>().RefreshAsync(prior, default);
        Assert.Contains(new BusinessSlot(BusinessInventorySpace.SharedMaterial, 5), refreshed.Inventory.Cleared);
        Assert.Equal(2, Assert.Single(refreshed.Inventory.Updates.Where(x => x.Core.ItemId == 1286)).Core.ToCore().Value);
        await EffectRequestTransport.Send(f, new CompoundItemRequest(new GameMessageMetadata(1, 0x19, 0, 7)
            { InboundOrdinal = 3, SequenceIsFresh = false }, 101, 0, 1));
        Assert.Equal(2, f.Sender.Messages.OfType<CompoundItemResponse>().Count()); Assert.Equal(2, f.Count(1286));
    }
    [Fact]
    public async Task Item_id_mode_and_invalid_tail_cannot_bypass_inventory_drawing()
    {
        using var f = Fixture(); await Seed(f);
        await EffectRequestTransport.Send(f,
            new CompoundItemRequest(new GameMessageMetadata(1, 0x19, 0, 7) { InboundOrdinal = 1, SequenceIsFresh = true }, 1287, 1, 1));
        Assert.Null(Assert.Single(f.Sender.Messages.OfType<CompoundItemResponse>()).Receipt);
        Assert.Equal(1, f.Count(1287)); Assert.Equal(0, f.Count(1286));
    }
}
