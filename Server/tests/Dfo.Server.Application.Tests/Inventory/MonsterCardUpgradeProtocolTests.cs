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

    private static byte ReadRank(ShopFixture fixture, int slot) =>
        InventoryItemCore.Decode(fixture.StoredCore(slot)).EnchantUpgradeCount;
}
