using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class MonsterCardUpgradeProtocolTests
{
    private const ushort Upgrade = 0x025C;
    private const int Card = 3619;
    private const int SourceSlot = 235;

    [SkippableTheory]
    [InlineData(2, 0, 100000, 0)]
    [InlineData(4, 0, 100000, 0)]
    [InlineData(1, 0, 100000, 0x13)]
    [InlineData(2, 2, 100000, 0x13)]
    [InlineData(2, 0, 4999, 0x0A)]
    public async Task A21_same_slot_upgrade_validates_and_settles_the_real_card(int count, byte rank, int gold, byte error)
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var fixture = new ShopFixture(services =>
            services.AddSingleton<IItemPlacementDefinitionSource, PvfItemPlacementDefinitionSource>());
        var placement = await fixture.Services.GetRequiredService<IItemPlacementRuleService>().PlacementForAsync(Card, default);
        Assert.NotNull(placement);
        await fixture.SeedCore(SourceSlot, StackableItemCore.Create(placement.ItemKind, Card, count) with { EnchantUpgradeCount = rank });
        fixture.Sql($"UPDATE character_stats SET carried_gold={gold} WHERE character_id=1");

        await EffectRequestTransport.SendRaw(fixture, new(1, Upgrade, 0, EffectRequestTransport.FreshSequence(fixture)),
            Convert.FromHexString("00EB000100EB00"));

        var frames = fixture.Sender.Outbound.Frames;
        var ack = frames.Single(frame => frame[0] == 1 && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == Upgrade);
        if (error != 0)
        {
            Assert.Equal(new byte[] { 0, error }, ack[15..]);
            Assert.Equal(gold, fixture.Gold);
            Assert.Equal(count, fixture.Count(Card));
            Assert.Equal(rank, ReadRank(fixture, SourceSlot));
            return;
        }

        Assert.Equal(Convert.FromHexString("0101230E000001"), ack[15..]);
        Assert.Equal(gold - 5000, fixture.Gold);
        Assert.Equal(count - 1, fixture.Count(Card));
        Assert.Equal(1, ReadRank(fixture, count == 2 ? SourceSlot : 233));
        if (count > 2) Assert.Equal(0, ReadRank(fixture, SourceSlot));
        Assert.Contains(frames, frame => frame[0] == 0
            && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == 0x000D && frame[15] == 0);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A21_split_upgrade_reuses_the_material_slot_or_retains_the_existing_upgraded_pile(bool hasUpgradedPile)
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var fixture = new ShopFixture(services =>
            services.AddSingleton<IItemPlacementDefinitionSource, PvfItemPlacementDefinitionSource>());
        var placement = await fixture.Services.GetRequiredService<IItemPlacementRuleService>().PlacementForAsync(Card, default);
        Assert.NotNull(placement);
        const int targetSlot = 233;
        const int materialSlot = 234;
        const int upgradedSlot = 235;
        await fixture.SeedCore(targetSlot, StackableItemCore.Create(placement.ItemKind, Card, 2));
        await fixture.SeedCore(materialSlot, StackableItemCore.Create(placement.ItemKind, Card, 1));
        if (hasUpgradedPile)
            await fixture.SeedCore(upgradedSlot, StackableItemCore.Create(placement.ItemKind, Card, 3) with { EnchantUpgradeCount = 1 });
        var originalUpgradedCore = CoreAt(fixture, upgradedSlot);

        await EffectRequestTransport.SendRaw(fixture, new(1, Upgrade, 0, EffectRequestTransport.FreshSequence(fixture)),
            Convert.FromHexString("00E9000100EA00"));

        var ack = fixture.Sender.Outbound.Frames.Single(frame =>
            frame[0] == 1 && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == Upgrade);
        Assert.Equal(Convert.FromHexString("0101230E000001"), ack[15..]);
        Assert.Equal(95000, fixture.Gold);
        Assert.Equal(hasUpgradedPile ? 5 : 2, fixture.Count(Card));
        Assert.Equal(0, ReadRank(fixture, targetSlot));
        if (hasUpgradedPile)
        {
            Assert.Equal(0, CoreAt(fixture, materialSlot));
            Assert.Equal(originalUpgradedCore, CoreAt(fixture, upgradedSlot));
            Assert.Equal(1, ReadRank(fixture, upgradedSlot));
        }
        else
        {
            Assert.Equal(1, ReadRank(fixture, materialSlot));
            Assert.Equal(0, CoreAt(fixture, upgradedSlot));
        }
    }

    [SkippableFact]
    public async Task A21_full_card_bag_refuses_a_split_without_charging_or_consuming()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var fixture = new ShopFixture(services =>
            services.AddSingleton<IItemPlacementDefinitionSource, PvfItemPlacementDefinitionSource>());
        var placement = await fixture.Services.GetRequiredService<IItemPlacementRuleService>().PlacementForAsync(Card, default);
        Assert.NotNull(placement);
        var begin = A21InventoryLayout.ToGameSlot(new(placement.Destination.Space, placement.Destination.FirstIndex)).Index;
        var capacity = await fixture.Services.GetRequiredService<IInventoryCapacityDomainService>().OpenSlotCountAsync(
            ContainerId.ForCharacter(fixture.Session.CharacterId, InventoryContainerType.Main), placement.Destination.Space, default);
        for (var index = begin; index < begin + capacity; index++)
            await fixture.SeedCore(index, StackableItemCore.Create(placement.ItemKind, Card, index == SourceSlot ? 4 : 1));

        await EffectRequestTransport.SendRaw(fixture, new(1, Upgrade, 0, EffectRequestTransport.FreshSequence(fixture)),
            Convert.FromHexString("00EB000100EB00"));

        var ack = fixture.Sender.Outbound.Frames.Single(frame =>
            frame[0] == 1 && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == Upgrade);
        Assert.Equal(new byte[] { 0, 0x04 }, ack[15..]);
        Assert.Equal(100000, fixture.Gold);
        Assert.Equal(capacity + 3, fixture.Count(Card));
        Assert.Equal(0, ReadRank(fixture, SourceSlot));
    }

    [SkippableFact]
    public async Task A21_failed_chance_consumes_only_the_material_and_gold()
    {
        const int materialCard = 3600;
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var fixture = new ShopFixture(services =>
        {
            services.AddSingleton<IItemPlacementDefinitionSource, PvfItemPlacementDefinitionSource>();
            services.AddSingleton<IMonsterCardUpgradeDefinitionSource>(new GuaranteedFailureContent());
        });
        var placements = fixture.Services.GetRequiredService<IItemPlacementRuleService>();
        var target = await placements.PlacementForAsync(Card, default);
        var material = await placements.PlacementForAsync(materialCard, default);
        Assert.NotNull(target);
        Assert.NotNull(material);
        var materialSlot = A21InventoryLayout.ToGameSlot(new(material.Destination.Space, material.Destination.FirstIndex + 5)).Index;
        await fixture.SeedCore(SourceSlot, StackableItemCore.Create(target.ItemKind, Card, 2));
        await fixture.SeedCore(materialSlot, StackableItemCore.Create(material.ItemKind, materialCard, 1));
        var payload = Convert.FromHexString("00EB0001000000");
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(5), checked((short)materialSlot));

        await EffectRequestTransport.SendRaw(fixture, new(1, Upgrade, 0, EffectRequestTransport.FreshSequence(fixture)), payload);

        var ack = fixture.Sender.Outbound.Frames.Single(frame =>
            frame[0] == 1 && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == Upgrade);
        Assert.Equal(Convert.FromHexString("0100230E000000"), ack[15..]);
        Assert.Equal(95000, fixture.Gold);
        Assert.Equal(0, fixture.Count(materialCard));
        Assert.Equal(2, fixture.Count(Card));
        Assert.Equal(0, ReadRank(fixture, SourceSlot));
    }

    private sealed class GuaranteedFailureContent : IMonsterCardUpgradeDefinitionSource
    {
        public ValueTask<MonsterCardUpgradeDefinition?> GetUpgradeDefinitionAsync(CancellationToken ct) =>
            ValueTask.FromResult<MonsterCardUpgradeDefinition?>(new(0, 5000));

        public ValueTask<MonsterCardUpgradeItem?> GetCardAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<MonsterCardUpgradeItem?>(new(0, [0, 1]));
    }

    private static int CoreAt(ShopFixture fixture, int slot) => fixture.Scalar(
        $"SELECT COALESCE(MAX(core_uid),0) FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index={slot}");

    private static byte ReadRank(ShopFixture fixture, int slot) =>
        InventoryItemCore.Decode(fixture.StoredCore(slot)).EnchantUpgradeCount;
}
