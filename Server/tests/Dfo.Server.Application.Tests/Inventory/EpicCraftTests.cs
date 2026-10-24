using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.EpicCraft;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EpicCraftTests
{
    private const int Piece = 10300000;
    private sealed class Catalog : IEpicBookDefinitionSource
    {
        public IReadOnlyList<EpicPieceDefinition> Load() => [new(Piece, 3000)];
        public IReadOnlyList<EpicCraftRecipe> LoadRecipes() => [new(3000, Piece, 1000, [new(3244, 25), new(3262, 100)])];
    }
    private static ShopFixture Fixture()
    {
        var content = new OpeningContent();
        content.Items[3000] = content.Items[3000] with { Placement = new(1, 1,
            new(BusinessInventorySpace.CarriedEquipment, 0, 1)) { WorldClass = ItemWorldClass.Equipment } };
        return new(s => { s.AddSingleton<IEpicBookDefinitionSource>(new Catalog());
            s.AddSingleton<IPackageOpeningDefinitionSource>(content); s.AddSingleton<IItemPlacementDefinitionSource>(content); });
    }
    private static async Task Seed(ShopFixture f, int count = 1)
    {
        f.Sql($"INSERT INTO account_epic_pieces(account_id,piece_id,count) VALUES(1,{Piece},{1000 * count})");
        await f.Seed(140, 3244, 25 * count, 3);
        await f.SeedShared(3262, 100 * count);
    }
    private static int Balance(ShopFixture f) => f.Scalar("SELECT COALESCE(SUM(count),0) FROM account_epic_pieces WHERE account_id=1");
    private static Task<EpicCraftReceipt> Craft(ShopFixture f, int id = 3000) => f.Services.GetRequiredService<ICommandBus>()
        .SendAsync(new EpicCraftCommand(f.OnlineUser, f.Session, id)).AsTask();
    private static ValueTask Send(ShopFixture f, ushort seq = 7, int id = 3000) => EffectRequestTransport.Send(f,
        new EpicCraftRequest(new(1, 0x0313, 0, seq), id));

    [Fact]
    public async Task SQLite_commit_failure_rolls_back_pieces_materials_and_new_equipment()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(); await Seed(f);
            f.Sql("CREATE TABLE craft_commit_probe(account_id INTEGER REFERENCES accounts(account_id) DEFERRABLE INITIALLY DEFERRED)");
            await using (var tx = ((DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin())
            {
                await TestEffectFootprints.Run(f.Session, cap => f.Services.GetRequiredService<IEpicCraftDomainService>().CraftAsync(f.Session, 3000, f.Clock.GetUtcNow(), cap, default));
                await f.Services.GetRequiredService<SqliteDatabase>().WriteAsync(async (command, ct) =>
                {
                    command.CommandText = "INSERT INTO craft_commit_probe VALUES(99999)";
                    await command.ExecuteNonQueryAsync(ct);
                }, default);
                await Assert.ThrowsAsync<SqliteException>(async () => await tx.CommitAsync(default));
            }
            Assert.Equal(1000, Balance(f)); Assert.Equal(25, f.Count(3244)); Assert.Equal(100, f.SharedCount(3262));
            Assert.Equal(0, f.Count(3000)); Assert.Empty(f.Sender.Messages);
        });
    }
    [Fact]
    public async Task Stale_selection_cannot_consume_the_account_balance()
    {
        using var f = Fixture(); await Seed(f); var stale = f.Session; f.Select(2);
        await Assert.ThrowsAnyAsync<Exception>(async () => await f.Services.GetRequiredService<ICommandBus>()
            .SendAsync(new EpicCraftCommand(f.OnlineUser, stale, 3000)));
        Assert.Equal(1000, Balance(f)); Assert.Equal(100, f.SharedCount(3262));
    }
    [Fact]
    public async Task Locked_or_expired_material_does_not_satisfy_the_recipe()
    {
        foreach (var expired in new[] { false, true })
        {
            using var f = Fixture(); await Seed(f);
            await f.SeedCore(140, StackableItemCore.Create(3, 3244, 25) with
            { EquipmentLockId = expired ? (byte)0 : (byte)1,
                ExpireTime = expired ? checked((int)f.Clock.GetUtcNow().AddSeconds(-1).ToUnixTimeSeconds()) : 0 });
            var failure = await Assert.ThrowsAsync<OpeningRejectedException>(() => Craft(f));
            Assert.Equal(OpeningRejection.MissingMaterial, failure.Reason);
            Assert.Equal(1000, Balance(f)); Assert.Equal(25, f.Count(3244)); Assert.Equal(100, f.SharedCount(3262));
        }
    }
    [SkippableFact]
    public async Task Real_pvf_golden_stone_creates_equipment_and_debits_all_seven_costs()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is unavailable.");
        using var f = new ShopFixture(s => s.AddSingleton<IItemPlacementDefinitionSource>(sp =>
            new PvfItemPlacementDefinitionSource(sp.GetRequiredService<PvfArchiveProvider>())));
        var recipe = f.Services.GetRequiredService<IEpicBookRuleService>().FindRecipe(450004)!;
        f.Sql($"INSERT INTO account_epic_pieces(account_id,piece_id,count) VALUES(1,{recipe.PieceId},{recipe.PieceCount})");
        var materialSlot = 140;
        foreach (var cost in recipe.Materials)
        {
            if (AccountMaterialCatalog.TryGetSlotIndex(cost.ItemId, out _)) await f.SeedShared(cost.ItemId, cost.Count);
            else await f.Seed(materialSlot++, cost.ItemId, cost.Count, 3);
        }
        var receipt = await Craft(f, 450004);
        Assert.Equal(0, Balance(f)); Assert.Equal(1, f.Count(450004));
        foreach (var cost in recipe.Materials)
            Assert.Equal(0, AccountMaterialCatalog.TryGetSlotIndex(cost.ItemId, out _) ? f.SharedCount(cost.ItemId) : f.Count(cost.ItemId));
        Assert.Equal(450004, Assert.Single(receipt.Inventory.Rewards).ItemId);
    }
    [Fact]
    public async Task Success_atomically_debits_account_and_carried_materials_and_creates_an_independent_equipment()
    {
        using var f = Fixture(); await Seed(f);
        var result = await Craft(f);
        Assert.Equal(0, Balance(f)); Assert.Equal(0, f.Count(3244)); Assert.Equal(0, f.SharedCount(3262));
        Assert.Equal(1, f.Count(3000)); Assert.Equal(new(Piece, 0), result.Pieces);
        Assert.Equal(3000, Assert.Single(result.Inventory.Rewards).ItemId);
        Assert.False(Assert.Single(result.Inventory.Updates, x => x.Core.ItemId == 3000).Core.IsPile);
    }
    [Theory]
    [InlineData("pieces")]
    [InlineData("materials")]
    [InlineData("unknown")]
    [InlineData("full")]
    [InlineData("storage")]
    public async Task Refusals_and_storage_failure_leave_all_costs_and_output_unchanged(string reason)
    {
        using var f = Fixture(); await Seed(f);
        if (reason == "pieces") f.Sql("UPDATE account_epic_pieces SET count=999");
        if (reason == "materials") await f.SeedShared(3262, 99);
        if (reason == "full") { await f.Seed(9, 3000, 1, 1); await f.Seed(10, 3000, 1, 1); }
        if (reason == "storage") f.Sql("CREATE TRIGGER reject_epic BEFORE INSERT ON item_slots BEGIN SELECT RAISE(ABORT,'epic-test'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => Craft(f, reason == "unknown" ? 123 : 3000));
        Assert.Equal(reason == "pieces" ? 999 : 1000, Balance(f)); Assert.Equal(25, f.Count(3244));
        Assert.Equal(reason == "materials" ? 99 : 100, f.SharedCount(3262));
        Assert.Equal(reason == "full" ? 2 : 0, f.Count(3000)); Assert.Empty(f.Sender.Messages);
    }
    [Fact]
    public async Task Concurrent_requests_cannot_spend_the_same_piece_or_material_twice()
    {
        using var f = Fixture(); await Seed(f);
        var results = await Task.WhenAll(Attempt(), Attempt());
        Assert.Equal(1, results.Count(x => x)); Assert.Equal(0, Balance(f)); Assert.Equal(1, f.Count(3000));
        async Task<bool> Attempt() { try { await Craft(f); return true; } catch (OpeningRejectedException) { return false; } }
    }
    [Fact]
    public async Task Committed_projection_sends_ack_then_absolute_piece_balance_then_inventory_including_empty_shared_slot()
    {
        using var f = Fixture(); await Seed(f);
        f.Sender.BeforeSend = () => { Assert.Equal(0, Balance(f)); Assert.Equal(1, f.Count(3000)); };
        await Send(f);
        var frames = Frames(f).ToArray();
        Assert.Equal((byte)1, frames[0][0]); Assert.Equal((ushort)0x0313, BitConverter.ToUInt16(frames[0], 1));
        Assert.Equal(new byte[] { 1 }, frames[0][15..]);
        Assert.Equal((ushort)0x0465, BitConverter.ToUInt16(frames[1], 1));
        Assert.Equal(0, BitConverter.ToInt32(frames[1], 24));
        var rows = frames.Where(x => x[0] == 0 && BitConverter.ToUInt16(x, 1) == 0xE)
            .SelectMany(x => Enumerable.Range(0, BitConverter.ToUInt16(x, 16)).Select(n => x.AsSpan(18 + n * 101, 101).ToArray())).ToArray();
        Assert.Contains(rows, x => BitConverter.ToInt16(x) == 354 + 5 && BitConverter.ToInt32(x, 2) == -1);
    }
    [Fact]
    public async Task Wire_replay_does_not_craft_or_acknowledge_a_second_time()
    {
        using var f = Fixture(); await Seed(f, 2);
        await Send(f); await Send(f);
        Assert.Single(f.Sender.Messages.OfType<EpicCraftResponse>());
        Assert.Equal(1000, Balance(f)); Assert.Equal(1, f.Count(3000)); Assert.Equal(25, f.Count(3244));
        var balance = Frames(f).Last(x => x[0] == 0 && BitConverter.ToUInt16(x, 1) == 0x0465);
        Assert.Equal(0, balance[15]); Assert.Equal(1000, BitConverter.ToInt32(balance, 24));
    }
    [Fact]
    public async Task Missing_material_returns_only_failure_and_cannot_emit_success_or_inventory_deltas()
    {
        using var f = Fixture(); await Seed(f); await f.SeedShared(3262, 99);
        await Send(f);
        var reply = Assert.Single(Frames(f)); Assert.Equal(new byte[] { 0, 0x16 }, reply[15..]);
        Assert.Equal(1000, Balance(f)); Assert.Equal(0, f.Count(3000));
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
