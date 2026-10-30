using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol.Game.ResetRandomOption;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class MagicSealGradeTests
{
    private sealed class Content : IEquipmentDefinitionSource, IEquipmentQualityDefinitionSource,
        IEquipmentQualitySeedSource, IEquipmentMagicSealGradeDefinitionSource
    {
        public int Next() => 7654321;
        public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(1);
        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentDefinition?>(id == 3000 ? new(75, 75, 2, "weapon", 100) : null);
        ValueTask<EquipmentQualityMaterialDefinition?> IEquipmentQualityDefinitionSource.FindAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentQualityMaterialDefinition?>(id == 4000 ? new(false, null) : null);
        ValueTask<EquipmentMagicSealGradeDefinition?> IEquipmentMagicSealGradeDefinitionSource.FindAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentMagicSealGradeDefinition?>(id == 3000
                ? new([new(30, 31, 1)], new HashSet<byte> { 15, 16, 17, 180 }, new HashSet<byte> { 180 }) : null);
    }
    internal static ShopFixture Fixture(Action<IServiceCollection>? configure = null) => new(s =>
    {
        var content = new Content();
        s.AddSingleton<IEquipmentDefinitionSource>(content);
        s.AddSingleton<IEquipmentQualityDefinitionSource>(content);
        s.AddSingleton<IEquipmentMagicSealGradeDefinitionSource>(content);
        s.AddSingleton<IEquipmentQualitySeedSource>(content); configure?.Invoke(s);
    });
    internal static InventoryItemCoreState Original => StackableItemCore.Create(1, 3000, 12345) with
    {
        Attribute = 11, Durability = 72, SealFlag = 1, EnchantCardId = 3600, AmplifyType = 2, AmplifyValue = 70,
        GenuineUpgrade = 4, RandomOption0 = new(15, 2, 9), RandomOption1 = new(16, 12, 50),
        RandomOption2 = new(17, 21, 30), RandomOptionChangedIndex = 1, RandomOptionChangeState = 1,
        RandomOptionChange = new(18, 5, 7), TradeRestriction = 1,
    };
    internal static async Task Seed(ShopFixture f, int count = 2)
    { await f.SeedCore(9, Original); await f.Seed(65, 4000, count); }
    private static Task<EquipmentQualityResult> Roll(ShopFixture f, ushort index = 1) => EquipmentQualityDomainTestDriver.Grade(f, f.Session,
        new(BusinessInventorySpace.CarriedEquipment, 0), new(BusinessInventorySpace.Consumable, 0), index);
    private static ValueTask Send(ShopFixture f, ushort index = 1) => EffectRequestTransport.Send(f,
        new ResetRandomOptionRequest(new(1, 0x01C8, 0, 7), 9, 65, index));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Changes_only_selected_grade_and_persists_uid_seed_and_other_fields(int index)
    {
        using var f = Fixture(); await Seed(f);
        var uid = f.Scalar("SELECT core_uid FROM item_slots WHERE list_type=0 AND slot_index=9");
        var result = await Roll(f, (ushort)index);
        Assert.Equal(EquipmentQualityRejection.None, result.Rejection);
        var expected = index switch
        {
            0 => Original with { RandomOption0 = new(15, 30, 30) },
            1 => Original with { RandomOption1 = new(16, 30, 30) },
            _ => Original with { RandomOption2 = new(17, 30, 30) },
        };
        Assert.Equal(expected, result.Receipt!.Equipment.ToCore());
        Assert.Equal(1, f.Count(4000)); Assert.Equal(100000, f.Gold);
        Assert.Equal(uid, f.Scalar("SELECT core_uid FROM item_slots WHERE list_type=0 AND slot_index=9"));
        var persisted = f.StoredCore(9);
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(f.Session.CharacterId)); f.Select(1);
        Assert.Equal(expected, (await f.Services.GetRequiredService<IBusinessInventorySlotDomainService>()
            .Find(f.Session.CharacterId, new(BusinessInventorySpace.CarriedEquipment, 0))!.Container.GetAsync(0, default))!.Snapshot.ToCore());
        Assert.Equal(persisted, f.StoredCore(9));
    }

    [Theory]
    [InlineData("index", 0x13)]
    [InlineData("indexHighByte", 0x13)]
    [InlineData("empty", 0x13)]
    [InlineData("blocked", 0x17)]
    [InlineData("unknown", 0x17)]
    [InlineData("locked", 0xD5)]
    [InlineData("expired", 0x11)]
    [InlineData("missingBox", 0x15)]
    [InlineData("wrongBox", 0x15)]
    [InlineData("wrongTarget", 0x11)]
    public async Task Refuses_without_debit_or_equipment_changes(string reason, int error)
    {
        using var f = Fixture(); await Seed(f);
        ushort index = reason == "index" ? (ushort)3 : reason == "indexHighByte" ? (ushort)256 : (ushort)1;
        if (reason == "empty") await f.SeedCore(9, Original with { RandomOption1 = new(0, 0, 0) });
        if (reason == "blocked") await f.SeedCore(9, Original with { RandomOption1 = new(180, 3, 4) });
        if (reason == "unknown") await f.SeedCore(9, Original with { RandomOption1 = new(99, 3, 4) });
        if (reason == "locked") await f.SeedCore(9, Original with { EquipmentLockId = 1 });
        if (reason == "expired") await f.SeedCore(9, Original with { ExpireTime = (int)f.Clock.GetUtcNow().ToUnixTimeSeconds() - 1 });
        if (reason == "missingBox") await f.Seed(65, 4000, 0);
        if (reason == "wrongBox") await f.Seed(65, 1001, 2);
        if (reason == "wrongTarget") await f.SeedCore(9, Original with { ItemId = 3001 });
        var before = f.StoredCore(9); var count = f.Count(4000);
        await Send(f, index);
        Assert.Equal(before, f.StoredCore(9)); Assert.Equal(count, f.Count(4000));
        Assert.Equal(new byte[] { 0, (byte)error }, Assert.Single(Frames(f))[15..]);
    }

    [Fact]
    public async Task Write_failure_rolls_back_consumed_material_and_target()
    {
        using var f = Fixture(); await Seed(f); var before = f.StoredCore(9);
        f.Sql("CREATE TRIGGER grade_failure BEFORE UPDATE ON items BEGIN SELECT RAISE(ABORT,'grade-failure'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => Roll(f));
        Assert.Equal(2, f.Count(4000)); Assert.Equal(before, f.StoredCore(9));
    }

    [Fact]
    public async Task Concurrent_requests_cannot_spend_one_box_twice()
    {
        using var f = Fixture(); await Seed(f, 1);
        var results = await Task.WhenAll(Roll(f), Roll(f));
        Assert.Single(results, x => x.Receipt is not null); Assert.Equal(0, f.Count(4000));
    }

    [Fact]
    public async Task Quickslot_material_is_consumed_and_the_domain_fixture_rejects_an_old_owner()
    {
        using var f = Fixture(); await Seed(f); await f.Seed(3, 4000, 1);
        var owner = f.Session;
        var target = new BusinessSlot(BusinessInventorySpace.CarriedEquipment, 0);
        var material = new BusinessSlot(BusinessInventorySpace.QuickSlot, 0);
        var receipt = (await EquipmentQualityDomainTestDriver.Grade(f, owner, target, material, 0)).Receipt;
        Assert.NotNull(receipt); Assert.Equal(0, receipt.MaterialRemaining);
        var before = f.StoredCore(9); f.Select(2);
        await Assert.ThrowsAnyAsync<Exception>(async () => await EquipmentQualityDomainTestDriver.Grade(f, owner, target, material, 0));
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(owner.CharacterId));
        f.Select(1);
        Assert.Equal(before, f.StoredCore(9));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Committed_item_and_material_updates_precede_one_byte_ack(int count)
    {
        using var f = Fixture(); await Seed(f, count);
        f.Sender.BeforeSend = () => Assert.Equal(count - 1, f.Count(4000));
        await Send(f); var frames = Frames(f).ToArray(); Assert.Equal(3, frames.Length);
        Assert.Equal((ushort)0xE, BitConverter.ToUInt16(frames[0], 1));
        Assert.Equal((ushort)9, BitConverter.ToUInt16(frames[0], 18));
        Assert.Equal((ushort)0xE, BitConverter.ToUInt16(frames[1], 1));
        Assert.Equal((ushort)65, BitConverter.ToUInt16(frames[1], 18));
        Assert.Equal(count == 1 ? -1 : 4000, BitConverter.ToInt32(frames[1], 20));
        Assert.Equal(count - 1, BitConverter.ToInt32(frames[1], 24));
        Assert.Equal((ushort)0x1C8, BitConverter.ToUInt16(frames[2], 1));
        Assert.Equal(new byte[] { 1 }, frames[2][15..]);
    }

    [Fact]
    public async Task Retransmitted_sequence_does_not_reroll_or_consume_twice()
    {
        using var f = Fixture(); await Seed(f); await Send(f); var before = f.StoredCore(9); await Send(f);
        Assert.Equal(1, f.Count(4000)); Assert.Equal(before, f.StoredCore(9));
        Assert.Single(f.Sender.Messages.OfType<ResetRandomOptionResponse>());
    }

    [Theory]
    [InlineData("090041000200", true, 2)]
    [InlineData("090041000001", true, 256)]
    [InlineData("0900410002", false, 0)]
    [InlineData("090041000200FF", false, 0)]
    [InlineData("09004100020000000000", false, 0)]
    public void Decoder_uses_six_byte_A21_layout_and_keeps_full_option_index(string hex, bool valid, int index)
    {
        using var f = Fixture(); var registration = f.Services.GetServices<GameMessageDecoderRegistration>().Single(x => x.Selector == 1 && x.Type == 0x1C8);
        var request = Assert.IsType<ResetRandomOptionRequest>(((IGameMessageDecoder)f.Services.GetRequiredService(registration.DecoderType))
            .Decode(GameMessageMetadata.Empty, Convert.FromHexString(hex)));
        Assert.Equal(valid, request.Valid); Assert.Equal(index, request.OptionIndex);
        if (valid) { Assert.Equal(9, request.TargetSlot); Assert.Equal(65, request.MaterialSlot); }
    }

    [SkippableFact]
    public async Task Real_pvf_grade_reroll_consumes_one_box_and_persists_only_selected_quality()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH unavailable.");
        using var f = new ShopFixture(s => s.AddSingleton<IItemPlacementDefinitionSource>(sp =>
            new PvfItemPlacementDefinitionSource(sp.GetRequiredService<PvfArchiveProvider>())), useArchive: true);
        var original = Original with { ItemId = 101010339 };
        await f.SeedCore(9, original); await f.Seed(65, 15, 1);
        var result = await Roll(f, 0); var receipt = result.Receipt;
        Assert.NotNull(receipt); Assert.Equal(0, f.Count(15));
        var grade = receipt.Equipment.RandomOption0;
        Assert.Equal(grade.Value1, grade.Value2); Assert.InRange(grade.Value1, (byte)0, (byte)55);
        Assert.Equal(original with { RandomOption0 = grade }, receipt.Equipment.ToCore());
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(f.Session.CharacterId)); f.Select(1);
        Assert.Equal(original with { RandomOption0 = grade }, (await f.Services.GetRequiredService<IBusinessInventorySlotDomainService>()
            .Find(f.Session.CharacterId, new(BusinessInventorySpace.CarriedEquipment, 0))!.Container.GetAsync(0, default))!.Snapshot.ToCore());
    }

    [SkippableFact]
    public async Task Real_pvf_uses_reset_weights_exclusions_and_quality_bytes_instead_of_stat_endpoints()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH unavailable.");
        using var f = new ShopFixture(useArchive: true); var source = f.Services.GetRequiredService<IEquipmentMagicSealGradeDefinitionSource>();
        var definition = await source.FindAsync(101010339, default); Assert.NotNull(definition);
        Assert.Equal(new MagicSealGradeBand[] { new(0, 14, 330), new(14, 37, 520), new(37, 47, 100), new(47, 56, 50) }, definition.Bands);
        Assert.Contains((byte)180, definition.UnmodifiableOptions); Assert.Contains((byte)15, definition.KnownOptions);
        var rules = f.Services.GetRequiredService<IEquipmentMagicSealGradeRuleService>();
        var values = new HashSet<byte>();
        for (var seed = 1; seed <= 1000; seed++)
        {
            var result = await rules.RollAsync(101010339, 15, new(15, 13, 98), seed, default);
            Assert.Equal(EquipmentQualityRejection.None, result.Rejection);
            Assert.Equal((byte)15, result.Option!.Type); Assert.Equal(result.Option.Value1, result.Option.Value2);
            Assert.InRange(result.Option.Value1, (byte)0, (byte)55); values.Add(result.Option.Value1);
        }
        foreach (var range in new[] { (0, 13), (14, 36), (37, 46), (47, 55) })
            Assert.Contains(values, x => x >= range.Item1 && x <= range.Item2);
        Assert.Equal(EquipmentQualityRejection.UnmodifiableOption,
            (await rules.RollAsync(101010339, 15, new(180, 13, 98), 5, default)).Rejection);
    }

    private static IEnumerable<byte[]> Frames(ShopFixture f)
    {
        foreach (var message in f.Sender.Messages)
        {
            var registration = f.Services.GetServices<ServerMessageEncoderRegistration>().Single(x => x.MessageType == message.GetType());
            foreach (var frame in ((IServerMessageEncoder)f.Services.GetRequiredService(registration.EncoderType)).Encode(message)) yield return frame;
        }
    }
}
