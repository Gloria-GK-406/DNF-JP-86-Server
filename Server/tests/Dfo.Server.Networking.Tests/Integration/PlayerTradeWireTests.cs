using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Networking.Tests.Characterization;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Networking.Tests.Characterization.CharacterizationSteps;

namespace Dfo.Server.Networking.Tests.Integration;

[Collection(CharacterizationCollection.Name)]
public sealed class PlayerTradeWireTests
{
    private const int Material = 3030;

    [PvfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Trade_gold_and_crystals_move_once_or_both_roll_back(bool failSecondWrite)
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        var ct = timeout.Token;
        await using var server = await CharacterizationHost.StartAsync(null, ct);
        var (first, second) = await TwoPlayersInTownAsync(server, ct);
        using var left = first;
        using var right = second;
        await server.ExecuteSqlAsync("UPDATE character_stats SET carried_gold = 1000", ct);
        await TestGameplay.RunAsync(server.Services, () => TestUseCase.RunAsync(server.Services, () =>
            server.Services.GetRequiredService<IItemGrantDomainService>().GrantAllPersistentAsync(
                new(right.WireUserId), 3036, 20, ct)));
        var grant = await TestGameplay.RunAsync(server.Services, () => TestUseCase.RunAsync(server.Services, () =>
            server.Services.GetRequiredService<IItemGrantDomainService>().GrantAllPersistentAsync(
                new(left.WireUserId), Material, 10, ct)));
        var itemSlot = checked((ushort)A21InventoryLayout.ToGameSlot(Assert.Single(grant.Items).Slot).Index);

        await left.SendAsync(0x000A, [.. U16(right.WireUserId), 1, 64, 0, 0, 0], ct);
        await FenceAllAsync(ct, left, right);
        Assert.Contains(left.Frames, frame => frame.Is(1, 0x000A) && frame.Body.SequenceEqual(new byte[] { 1 }));
        Assert.Contains(right.Frames, frame => frame.Is(0, 0x0007) && frame.Body[2] == 1);
        await right.SendAsync(0x000B, [.. U16(left.WireUserId), 1, 42, 0, 0, 0, 0], ct);
        await FenceAllAsync(ct, right, left);
        Assert.Contains(left.Frames, frame => frame.Is(0, 0x0008) && frame.Body[2] == 1);

        byte[] offer = [0, 0, 0, .. U32(0), .. U32(200), 4, 0, 0, .. new byte[14]];
        await left.SendAsync(0x0013, offer, ct);
        await FenceAllAsync(ct, left, right);
        Assert.Contains(right.Frames, frame => frame.Is(0, 0x000F) && frame.Body.Length == 101
            && frame.Body.Slice(6, 4).SequenceEqual(U32(200)));
        Assert.Equal(1000, await GoldAsync(server, left.WireUserId, ct));
        Assert.Equal(1000, await GoldAsync(server, right.WireUserId, ct));
        await right.SendAsync(0x0013, [36, .. U16(357), .. U32(3036), .. U32(7), 4, 0, 0, .. new byte[14]], ct);
        await FenceAllAsync(ct, right, left);
        Assert.Contains(left.Frames, frame => frame.Is(0, 0x000F) && frame.Body.Slice(2, 4).SequenceEqual(U32(3036)));
        Assert.Equal(0, await CrystalsAsync(server, left.WireUserId, ct));
        Assert.Equal(20, await CrystalsAsync(server, right.WireUserId, ct));
        await left.SendAsync(0x0013, [0, .. U16(itemSlot), .. U32(Material), .. U32(4), 4, 0, 0, .. new byte[14]], ct);
        await FenceAllAsync(ct, left, right);
        Assert.Contains(right.Frames, frame => frame.Is(0, 0x000F) && frame.Body.Slice(6, 4).SequenceEqual(U32(4)));
        await left.SendAsync(0x0013, [4, 3, 0, .. U32(Material), .. U32(1), 0, 0, 0, .. new byte[14]], ct);
        await FenceAllAsync(ct, left, right);
        Assert.Contains(right.Frames, frame => frame.Is(0, 0x000F)
            && frame.Body.Slice(2, 4).SequenceEqual(U32(Material)) && frame.Body.Slice(6, 4).SequenceEqual(U32(3)));

        foreach (byte state in new byte[] { 5, 1 })
        {
            await left.SendAsync(0x0018, [state], ct);
            await FenceAllAsync(ct, left, right);
            await right.SendAsync(0x0018, [state], ct);
            await FenceAllAsync(ct, right, left);
        }
        Assert.Contains(left.Frames, frame => frame.Is(0, 0x028F));
        Assert.Contains(right.Frames, frame => frame.Is(0, 0x028F));
        await left.SendAsync(0x0018, [3], ct);
        await FenceAllAsync(ct, left, right);
        Assert.DoesNotContain(left.Frames, frame => frame.Is(0, 0x0012));
        Assert.Equal(1000, await GoldAsync(server, left.WireUserId, ct));
        Assert.Equal(20, await CrystalsAsync(server, right.WireUserId, ct));
        var leftSession = server.Services.GetRequiredService<ISelectedSessionDomainService>().FindByWireUserId(left.WireUserId)!;
        var rightSession = server.Services.GetRequiredService<ISelectedSessionDomainService>().FindByWireUserId(right.WireUserId)!;

        if (failSecondWrite)
        {
            await server.ExecuteSqlAsync($"""
                CREATE TRIGGER refuse_trade_credit BEFORE UPDATE OF carried_gold ON character_stats
                WHEN old.character_id = {right.WireUserId}
                BEGIN SELECT RAISE(ABORT, 'test write failure'); END;
                """, ct);
        }
        await right.SendAsync(0x0018, [3], ct);
        if (failSecondWrite)
        {
            await right.ReadUntilClosedAsync(ct);
            Assert.Equal(1000, await GoldAsync(server, left.WireUserId, ct));
            Assert.Equal(1000, await GoldAsync(server, right.WireUserId, ct));
            var inventory = server.Services.GetRequiredService<IAccountInventoryEntryDomainService>();
            var l = await inventory.LoadAsync(leftSession.AccountId, leftSession.CharacterId, ct);
            var r = await inventory.LoadAsync(rightSession.AccountId, rightSession.CharacterId, ct);
            Assert.Equal(0, CrystalCount(l));
            Assert.Equal(20, r.VirtualResources.Single(item => item.SlotIndex == 357).Value);
            Assert.Equal(10, await MaterialAsync(server, left.WireUserId, ct));
            Assert.Equal(0, await MaterialAsync(server, right.WireUserId, ct));
            return;
        }

        await FenceAllAsync(ct, right, left);
        Assert.Contains(left.Frames, frame => frame.Is(0, 0x0012));
        Assert.Contains(right.Frames, frame => frame.Is(0, 0x0012));
        await right.SendAsync(0x0018, [3], ct);
        await FenceAllAsync(ct, right, left);
        Assert.Equal(800, await GoldAsync(server, left.WireUserId, ct));
        Assert.Equal(1200, await GoldAsync(server, right.WireUserId, ct));
        Assert.Equal(7, await CrystalsAsync(server, left.WireUserId, ct));
        Assert.Equal(13, await CrystalsAsync(server, right.WireUserId, ct));
        Assert.Equal(7, await MaterialAsync(server, left.WireUserId, ct));
        Assert.Equal(3, await MaterialAsync(server, right.WireUserId, ct));
    }

    private static async Task<int> CrystalsAsync(CharacterizationHost server, ushort characterId, CancellationToken ct)
    {
        var session = server.Services.GetRequiredService<ISelectedSessionDomainService>().FindByWireUserId(characterId)!;
        var inventory = await server.Services.GetRequiredService<IAccountInventoryEntryDomainService>()
            .LoadAsync(session.AccountId, session.CharacterId, ct);
        return CrystalCount(inventory);
    }

    private static int CrystalCount(AccountInventoryEntryState inventory) =>
        inventory.VirtualResources.SingleOrDefault(item => item.SlotIndex == 357)?.Value ?? 0;

    private static Task<long> GoldAsync(CharacterizationHost server, ushort characterId, CancellationToken ct) =>
        ReadAsync(server, "SELECT carried_gold FROM character_stats WHERE character_id = $id", characterId, ct);

    private static Task<long> MaterialAsync(CharacterizationHost server, ushort characterId, CancellationToken ct) =>
        ReadAsync(server, $"SELECT COALESCE(SUM(count),0) FROM item_slots WHERE owner_scope='character' AND owner_id=$id AND id={Material}", characterId, ct);

    private static async Task<long> ReadAsync(CharacterizationHost server, string sql, ushort characterId, CancellationToken ct)
    {
        await using var connection = new SqliteConnection($"Data Source={server.DatabasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", characterId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }
}
