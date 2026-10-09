using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol.Game.ResetItemQuality;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentQualityTests
{
    private sealed class Content : IEquipmentDefinitionSource, IEquipmentQualityDefinitionSource, IEquipmentQualitySeedSource
    {
        public bool Highest { get; set; }
        public string EquipmentType { get; set; } = "weapon";
        public IReadOnlySet<string>? Allowed { get; set; }
        public int Next() => 456789;
        public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(1);
        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentDefinition?>(id == 3000 ? new(80, 80, 4, EquipmentType, 100) : null);
        public ValueTask<EquipmentQualityMaterialDefinition?> FindAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentQualityMaterialDefinition?>(id == 4000 ? new(Highest, Allowed) : null);
    }
    private static ShopFixture Fixture(Content? content = null)
    {
        content ??= new();
        return new(s => { s.AddSingleton<IEquipmentDefinitionSource>(content);
            s.AddSingleton<IEquipmentQualityDefinitionSource>(content); s.AddSingleton<IEquipmentQualitySeedSource>(content); });
    }
    private static InventoryItemCoreState Equipment => StackableItemCore.Create(1, 3000, 12345) with
    { Attribute = 11, Durability = 72, SealFlag = 1, EnchantCardId = 3600, AmplifyType = 2, AmplifyValue = 70,
        GenuineUpgrade = 4, RandomOption0 = new(7, 50, 0), TradeRestriction = 1 };
    private static async Task Seed(ShopFixture f, int boxes = 2)
    { await f.SeedCore(9, Equipment); await f.Seed(65, 4000, boxes); }
    private static ResetItemQualityCommand Command(ShopFixture f, int id = 3000) =>
        new(f.OnlineUser, f.Session, new(BusinessInventorySpace.CarriedEquipment, 0), id, new(BusinessInventorySpace.Consumable, 0));
    private static Task<EquipmentQualityResult> Roll(ShopFixture f, int id = 3000) =>
        f.Services.GetRequiredService<ICommandBus>().SendAsync(Command(f, id)).AsTask();
    private static ValueTask Send(ShopFixture f, ushort seq = 7) => EffectRequestTransport.Send(f,
        new ResetItemQualityRequest(new(1, 0x0051, 0, seq), 9, 3000, 65));

    [Fact]
    public async Task Reroll_only_changes_seed_preserves_identity_and_all_other_equipment_fields()
    {
        using var f = Fixture(); await Seed(f); var before = f.StoredCore(9);
        var uid = f.Scalar("SELECT core_uid FROM item_slots WHERE list_type=0 AND slot_index=9");
        var result = await Roll(f);
        Assert.Equal(EquipmentQualityRejection.None, result.Rejection);
        Assert.Equal(456789, result.Receipt!.Equipment.InstanceValue); Assert.Equal(1, f.Count(4000));
        var after = f.StoredCore(9); Assert.NotEqual(before[5..9], after[5..9]);
        after.AsSpan(5, 4).Clear(); before.AsSpan(5, 4).Clear(); Assert.Equal(before, after);
        Assert.Equal(uid, f.Scalar("SELECT core_uid FROM item_slots WHERE list_type=0 AND slot_index=9"));
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("locked")]
    [InlineData("wrongid")]
    [InlineData("expired")]
    [InlineData("boxtype")]
    [InlineData("part")]
    [InlineData("avatar")]
    public async Task Refusal_does_not_consume_or_modify(string reason)
    {
        var content = new Content(); using var f = Fixture(content); await Seed(f);
        if (reason == "missing") await f.Seed(65, 4000, 0);
        if (reason == "locked") await f.SeedCore(9, Equipment with { EquipmentLockId = 1 });
        if (reason == "expired") await f.SeedCore(65, StackableItemCore.Create(2, 4000, 2) with
            { ExpireTime = checked((int)f.Clock.GetUtcNow().AddSeconds(-1).ToUnixTimeSeconds()) });
        if (reason == "boxtype") await f.Seed(65, 1001, 2);
        if (reason == "part") content.Allowed = new HashSet<string> { "ring" };
        if (reason == "avatar") content.EquipmentType = "coat avatar";
        var before = f.StoredCore(9); var count = f.Count(4000);
        var result = await Roll(f, reason == "wrongid" ? 3001 : 3000);
        Assert.NotEqual(EquipmentQualityRejection.None, result.Rejection);
        Assert.Equal(before, f.StoredCore(9)); Assert.Equal(count, f.Count(4000)); Assert.Empty(f.Sender.Messages);
    }
    [Fact]
    public async Task Storage_failure_rolls_back_material_and_seed()
    {
        using var f = Fixture(); await Seed(f); var before = f.StoredCore(9);
        f.Sql("CREATE TRIGGER quality_failure BEFORE UPDATE ON items BEGIN SELECT RAISE(ABORT,'quality-failure'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => Roll(f));
        Assert.Equal(2, f.Count(4000)); Assert.Equal(before, f.StoredCore(9));
    }
    [Fact]
    public async Task Commit_failure_rolls_back_both_mutations()
    {
        using var f = Fixture(); await Seed(f); var before = f.StoredCore(9);
        f.Sql("CREATE TABLE quality_commit_probe(account_id INTEGER REFERENCES accounts(account_id) DEFERRABLE INITIALLY DEFERRED)");
        await using (var tx = ((DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin())
        {
            Assert.NotNull((await f.Services.GetRequiredService<IEquipmentQualityDomainService>().RerollAsync(
                f.Session, Command(f).Target, 3000, Command(f).Material, default)).Receipt);
            await f.Services.GetRequiredService<SqliteDatabase>().WriteAsync(async (cmd, ct) =>
            { cmd.CommandText = "INSERT INTO quality_commit_probe VALUES(99999)"; await cmd.ExecuteNonQueryAsync(ct); }, default);
            await Assert.ThrowsAsync<SqliteException>(async () => await tx.CommitAsync(default));
        }
        Assert.Equal(2, f.Count(4000)); Assert.Equal(before, f.StoredCore(9));
    }
    [Fact]
    public async Task One_box_cannot_be_spent_by_two_concurrent_requests()
    {
        using var f = Fixture(); await Seed(f, 1);
        var results = await Task.WhenAll(Roll(f), Roll(f));
        Assert.Single(results, x => x.Receipt is not null); Assert.Equal(0, f.Count(4000));
    }
    [Fact]
    public async Task Stale_session_cannot_reroll_or_consume()
    {
        using var f = Fixture(); await Seed(f); var command = Command(f); f.Select(2);
        await Assert.ThrowsAnyAsync<Exception>(async () => await f.Services.GetRequiredService<ICommandBus>().SendAsync(command));
        Assert.Equal(2, f.Scalar("SELECT SUM(count) FROM item_slots WHERE owner_id=1 AND list_type=0 AND slot_index=65"));
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Wire_success_is_native_nine_byte_ack_then_one_target_update(int count)
    {
        using var f = Fixture(); await Seed(f, count);
        f.Sender.BeforeSend = () => Assert.Equal(count - 1, f.Count(4000));
        await Send(f); var frames = Frames(f).ToArray(); Assert.Equal(2, frames.Length);
        Assert.Equal((byte)1, frames[0][0]); Assert.Equal((ushort)0x51, BitConverter.ToUInt16(frames[0], 1));
        Assert.Equal(new byte[] { 1, 65, 0, (byte)(count - 1), 0, 0, 0, 2, 0 }, frames[0][15..]);
        Assert.Equal((byte)0, frames[1][0]); Assert.Equal((ushort)0xE, BitConverter.ToUInt16(frames[1], 1));
        Assert.Equal((ushort)1, BitConverter.ToUInt16(frames[1], 16));
        Assert.Equal((ushort)9, BitConverter.ToUInt16(frames[1], 18));
        Assert.Equal(456789, BitConverter.ToInt32(frames[1], 24));
    }
    [Fact]
    public async Task Wire_replay_refreshes_inventory_without_spending_or_acknowledging_twice()
    {
        using var f = Fixture(); await Seed(f); await Send(f); await Send(f);
        Assert.Equal(1, f.Count(4000)); Assert.Single(f.Sender.Messages.OfType<ResetItemQualityResponse>());
    }
    [Fact]
    public async Task Wire_refusal_returns_only_two_byte_envelope()
    {
        using var f = Fixture(); await Seed(f, 0); await Send(f);
        Assert.Equal(new byte[] { 0, 0x11 }, Assert.Single(Frames(f))[15..]);
        Assert.Equal(12345, BitConverter.ToInt32(f.StoredCore(9), 5));
    }
    [Fact]
    public async Task Highest_material_uses_client_reserved_top_band_seed()
    {
        using var f = Fixture(new Content { Highest = true }); await Seed(f);
        Assert.Equal(999999998, (await Roll(f)).Receipt!.Equipment.InstanceValue);
    }
    [SkippableFact]
    public async Task Real_pvf_box_consumption_and_equipment_seed_are_persisted_together()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH unavailable.");
        using var f = new ShopFixture(s => s.AddSingleton<IItemPlacementDefinitionSource>(sp =>
            new PvfItemPlacementDefinitionSource(sp.GetRequiredService<PvfArchiveProvider>())));
        await f.SeedCore(9, Equipment with { ItemId = 450004 }); await f.Seed(65, 15, 1);
        var receipt = (await Roll(f, 450004)).Receipt;
        Assert.NotNull(receipt); Assert.Equal(0, f.Count(15));
        Assert.Equal(receipt.Equipment.InstanceValue, BitConverter.ToInt32(f.StoredCore(9), 5));
        Assert.Equal(11, receipt.Equipment.ReinforcementLevel);
    }
    [SkippableFact]
    public async Task Real_pvf_standard_and_liberated_boxes_are_resolved_and_random_seeds_are_not_fixed()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH unavailable.");
        using var f = new ShopFixture(); var definitions = f.Services.GetRequiredService<IEquipmentQualityDefinitionSource>();
        foreach (var id in new[] { 15, 897 })
        { var box = await definitions.FindAsync(id, default); Assert.NotNull(box); Assert.False(box.Highest); }
        var rule = f.Services.GetRequiredService<IEquipmentQualityRuleService>();
        for (var n = 0; n < 100; n++)
        { var seed = await rule.RollAsync(450004, 15, f.Services.GetRequiredService<IEquipmentQualitySeedSource>().Next(), default); Assert.NotNull(seed); Assert.InRange(seed.Value, 1, int.MaxValue - 1); Assert.False(EquipmentQualitySeeds.IsFixed(seed.Value)); }
    }
    [Theory]
    [InlineData("0900B80B00004100", true)]
    [InlineData("0900B80B00004100885241FD", true)]
    [InlineData("0900B80B0000440000000000", false)]
    [InlineData("0900", false)]
    public void Decoder_rejects_truncation_or_unverified_trailing_bytes(string hex, bool valid)
    {
        using var f = Fixture(); var registration = f.Services.GetServices<GameMessageDecoderRegistration>().Single(x => x.Selector == 1 && x.Type == 0x51);
        var decoder = (IGameMessageDecoder)f.Services.GetRequiredService(registration.DecoderType);
        var request = Assert.IsType<ResetItemQualityRequest>(decoder.Decode(GameMessageMetadata.Empty, Convert.FromHexString(hex)));
        Assert.Equal(valid, request.Valid);
        if (valid) { Assert.Equal(9, request.TargetSlot); Assert.Equal(3000, request.ExpectedItemId); Assert.Equal(65, request.MaterialSlot); }
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
