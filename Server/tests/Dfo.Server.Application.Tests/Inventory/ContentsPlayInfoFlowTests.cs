using System.Buffers.Binary;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

[Trait("Category", "Unit")]
public sealed class ContentsPlayInfoFlowTests
{
    [Fact]
    public async Task Hover_reads_requested_account_roster_position_with_gaps_and_needs_no_selected_character()
    {
        using var f = Create();
        f.Sql("UPDATE characters SET slot_index=7 WHERE character_id=2; INSERT INTO accounts(account_id,m_id) VALUES(2,'other'); INSERT INTO characters(character_id,account_id,name,job,level,slot_index) VALUES(3,2,'other',12,86,0)");
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<OnlineStateRegistry>().ReleaseExact(f.Session));
        Seed(f, 2, mirror: 7, dimension: 3, tower: 1, floor: 1, ruins: 1);

        Assert.Equal(new[] { 1, 1, 2, 0, 7, 1 }, Fields(await Query(f, 1)));
        Assert.Equal(new[] { 0, 0, 5, 0, 0, 2 }, Fields(await Query(f, 0)));
        Assert.Equal(new byte[] { 0, 0 }, await Query(f, 2)); // Another account's character is not part of this roster.
        Assert.Empty(f.Sender.Outbound.Closes);
    }

    [Fact]
    public async Task Exhausted_quotas_reset_at_the_domain_day_boundary_and_floor_progress_survives()
    {
        using var f = Create();
        Seed(f, 1, mirror: 10, dimension: 5, tower: 1, floor: 1, ruins: 2);
        Assert.Equal(new[] { 1, 1, 0, 0, 10, 0 }, Fields(await Query(f, 0)));
        f.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(new[] { 0, 1, 5, 0, 0, 2 }, Fields(await Query(f, 0)));
        Assert.Equal(new[] { 0, 1, 5, 0, 0, 2 }, Fields(await Query(f, 0))); // Query does not consume a quota.
    }

    [Theory]
    [InlineData(2)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(255)]
    public async Task Invalid_positions_return_a_refusal_without_a_fabricated_character(byte index)
    {
        using var f = Create();
        Assert.Equal(new byte[] { 0, 0 }, await Query(f, index));
    }

    private static ShopFixture Create() => new(RosterRuinsContent.Register);

    private static async Task<byte[]> Query(ShopFixture f, byte index)
    {
        f.Sender.Messages.Clear();
        await EffectRequestTransport.SendRaw(f, new GameMessageMetadata(1, 0x02C1, 0,
            EffectRequestTransport.FreshSequence(f)), [index]);
        var frame = Assert.Single(f.Sender.Outbound.Frames);
        Assert.Equal(0x02C1, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        var body = frame[15..];
        if (body[0] == 1)
        {
            Assert.Equal(27, body.Length);
            Assert.Equal(index, body[1]);
            Assert.Equal(0, body[2]);
        }
        return body;
    }

    private static int[] Fields(byte[] body) => Enumerable.Range(0, 6)
        .Select(i => BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(3 + i * 4))).ToArray();

    private static void Seed(ShopFixture f, int character, int mirror, int dimension, int tower, int floor, int ruins)
    {
        var day = f.Services.GetRequiredService<IDailyResetRuleService>().PeriodDay(f.Clock.GetUtcNow());
        f.InTransaction(() =>
        {
            var store = f.Services.GetRequiredService<IUsageQuotaStore>();
            foreach (var (key, count) in new[] { (UsageQuotaKey.Mirror("entries", day), mirror),
                (UsageQuotaKey.Dimension(day), dimension), (UsageQuotaKey.Despair("entries", day), tower) })
                Assert.True(store.CompareExchangeAsync(new(new(character), key, store.ReadAsync(new(character), key, default).AsTask().GetAwaiter().GetResult(), new(count, null), new(1)), default).AsTask().GetAwaiter().GetResult());
            var ruinsStore = f.Services.GetRequiredService<IDarkElfRuinsDomainStore>();
            Assert.True(ruinsStore.CompareExchangeAsync(new(character), new(),
                new(Day: day, Entries: ruins, Month: new(day.Year, day.Month, 1), Revision: 1), default).AsTask().GetAwaiter().GetResult());
            return true;
        });
        f.Sql($"INSERT INTO character_despair_tower(character_id,highest_cleared_floor,last_clear_run) VALUES({character},{floor},'seed')");
    }
}

internal sealed class RosterRuinsContent : IDarkElfRuinsContentSource, IDespairTowerContentSource, IDimensionDungeonContentSource
{
    internal static void Register(IServiceCollection services)
    {
        var content = new RosterRuinsContent();
        services.AddSingleton<IDarkElfRuinsContentSource>(content);
        services.AddSingleton<IDespairTowerContentSource>(content);
        services.AddSingleton<IDimensionDungeonContentSource>(content);
    }

    DespairTowerCatalog IDespairTowerContentSource.Load() => new(70, 1,
        Enumerable.Range(1, 100).Select(i => new DespairTowerFloor(i, 1000 + i, 0, [])).ToArray());
    DimensionDungeonContent IDimensionDungeonContentSource.Load() => new(new HashSet<int>(), 5, 0,
        new Dictionary<(int, int), DimensionChroniclePool>(), new(3037, 1, DropRarity.Common, DropItemKind.Stackable));
    public DarkElfRuinsCatalog Load() => new([], new Dictionary<int, IReadOnlyList<int>>(),
        new Dictionary<int, byte>(), 2, 2, [], [], [], new Dictionary<int, Dfo.Server.Abstractions.Inventory.ItemDeliveryGrant>());
}
