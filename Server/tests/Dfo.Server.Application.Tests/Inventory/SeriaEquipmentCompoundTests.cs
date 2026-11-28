using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Protocol.Game.EquipmentCompound;
using Dfo.Server.Application.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class SeriaEquipmentCompoundTests
{
    private const int Source = 3000;
    private const int Result = 3001;
    private const int Soul = 10100115;
    private const int Crystal = 3244;
    private static readonly byte[] SingleItem = Convert.FromHexString("090000000700");

    [Fact]
    public async Task Settlement_projects_replacement_and_exact_costs_before_ack_and_deduplicates_same_frame()
    {
        using var fixture = Create();
        await Seed(fixture, 2);
        var sourceUid = fixture.Scalar("SELECT item_uid FROM items WHERE substr(hex(item_core),3,8)='B80B0000'");
        await Send(fixture, 7, SingleItem);
        Assert.Equal(0, fixture.Count(Source));
        Assert.Equal(1, fixture.Count(Result));
        Assert.Equal(1, fixture.Count(Crystal));
        Assert.Equal(1, fixture.SharedCount(Soul));
        Assert.NotEqual(sourceUid, fixture.Scalar("SELECT item_uid FROM items WHERE substr(hex(item_core),3,8)='B90B0000'"));

        var first = fixture.Sender.Messages.ToArray();
        var reply = Assert.IsType<EquipmentCompoundResponse>(first[^1]);
        Assert.Equal((ushort)9, reply.ResultSlot);
        var frames = Assert.Single(fixture.Sender.Batches).Frames;
        Assert.Equal(new byte[] { 1, 9, 0 }, frames[^1][15..]);
        Assert.All(frames[..^1], frame => Assert.Equal(0x000E, BitConverter.ToUInt16(frame, 1)));
        Assert.Equal(9, BitConverter.ToUInt16(frames[0], 18));
        Assert.Equal(-1, BitConverter.ToInt32(frames[0], 20));
        var rows = frames[..^1].SelectMany(frame => Enumerable.Range(0, BitConverter.ToUInt16(frame, 16))
            .Select(index => frame.AsSpan(18 + index * 101, 101).ToArray())).ToArray();
        Assert.Contains(rows, row => BitConverter.ToUInt16(row) == 9 && BitConverter.ToInt32(row, 2) == Result);
        Assert.Contains(rows, row => BitConverter.ToUInt16(row) == 360
            && BitConverter.ToInt32(row, 2) == Soul && BitConverter.ToInt32(row, 6) == 1);

        await Send(fixture, 7, SingleItem);
        Assert.Single(fixture.Sender.Messages.OfType<EquipmentCompoundResponse>());
        Assert.Equal(1, fixture.SharedCount(Soul));
        Assert.Equal(1, fixture.Count(Crystal));

        await Send(fixture, 8, SingleItem);
        Assert.Equal(2, fixture.Sender.Messages.OfType<EquipmentCompoundResponse>().Count());
        Assert.Equal(0, fixture.SharedCount(Soul));
        Assert.Equal(0, fixture.Count(Crystal));
        Assert.Equal(1, fixture.Count(Result));
    }

    [Theory]
    [InlineData("eight-bytes")]
    [InlineData("short")]
    [InlineData("missing")]
    [InlineData("invalid-mode")]
    [InlineData("wrong-group")]
    [InlineData("storage")]
    [InlineData("empty-pool")]
    [InlineData("locked")]
    public async Task Refused_compound_never_leaves_partial_asset_changes(string scenario)
    {
        using var fixture = Create(scenario == "empty-pool");
        await Seed(fixture, 1);
        var body = scenario switch
        {
            "eight-bytes" => Convert.FromHexString("0900000007000000"),
            "short" => Convert.FromHexString("0900000007"),
            "invalid-mode" => Convert.FromHexString("090002000700"),
            "wrong-group" => Convert.FromHexString("090000000800"),
            _ => SingleItem,
        };
        if (scenario == "locked") await fixture.SeedCore(9, StackableItemCore.Create(1, Source, 1) with { EquipmentLockId = 1 });
        if (scenario == "missing") await fixture.SeedShared(Soul, 0);
        if (scenario == "storage")
            fixture.Sql("CREATE TRIGGER reject_equipment_compound BEFORE INSERT ON item_slots BEGIN SELECT RAISE(ABORT,'equipment-compound-test'); END");
        await Send(fixture, 7, body);

        var reply = Assert.IsType<EquipmentCompoundResponse>(Assert.Single(fixture.Sender.Messages));
        Assert.Null(reply.ResultSlot);
        Assert.Equal(1, fixture.Count(Source));
        Assert.Equal(0, fixture.Count(Result));
        Assert.Equal(1, fixture.Count(Crystal));
        Assert.Equal(scenario == "missing" ? 0 : 1, fixture.SharedCount(Soul));
    }

    [SkippableFact]
    public void Actual_pvf_drives_all_four_cost_bands_and_the_two_step_result_pool()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is unavailable.");
        using var fixture = new ShopFixture(useArchive: true);
        var rules = fixture.Services.GetRequiredService<EquipmentCompoundRuleService>();
        var content = fixture.Services.GetRequiredService<IEquipmentCompoundDefinitionSource>().Load();

        var rare = content.Pools[(2, 65)].First(x => !x.Legacy && x.Group == "cl coat" && x.CreationRate > 0);
        var random = rules.Plan(rare.ItemId, false, 7);
        Assert.Equal([1, 1, 1], random.Materials.Select(x => x.Count));
        Assert.Contains(random.Bands.SelectMany(x => x.Candidates), x => content.Templates[x.ItemId].Legacy);
        var directed = rules.Plan(rare.ItemId, true, 7);
        Assert.Equal([2, 2, 2], directed.Materials.Select(x => x.Count));
        Assert.All(directed.Bands.SelectMany(x => x.Candidates), x => Assert.Equal("cl coat", content.Templates[x.ItemId].Group));
        Assert.Equal([65, 70], directed.Bands.Select(x => x.Level));
        Assert.Equal([50000, 50000], directed.Bands.Select(x => x.Weight));

        var legacy = rules.Plan(10165, true, 7);
        Assert.Equal([60, 40, 40, 2000], legacy.Materials.Select(x => x.Count));
        Assert.Equal([20, 20, 20, 1000], rules.Plan(10165, false, 7).Materials.Select(x => x.Count));
        Assert.Equal([67000, 33000], legacy.Bands.Select(x => x.Weight));
        Assert.Contains(legacy.Bands.Single(x => x.Level == 70).Candidates, x => x.ItemId == 100050061);
        Assert.All(Enumerable.Range(0, 50), seed => Assert.True(content.Templates[EquipmentCompoundRuleService.Draw(legacy, seed)].Legacy));

        VerifyHigherRarity(3, 65, [40, 30, 3, 50], [100, 70, 5, 100]);
        VerifyHigherRarity(6, 85, [600, 400, 4, 400], [600, 480, 6, 1500]);

        void VerifyHigherRarity(int rarity, int level, int[] randomCost, int[] directedCost)
        {
            var source = content.Pools[(rarity, level)].First(x => !x.GeneratedUnique && !x.Legacy && (rarity == 6 || x.CreationRate > 0));
            var part = content.Parts.Single(x => x.Value.Contains(source.Group)).Key;
            Assert.Equal(randomCost, rules.Plan(source.ItemId, false, part).Materials.Select(x => x.Count));
            Assert.Equal(directedCost, rules.Plan(source.ItemId, true, part).Materials.Select(x => x.Count));
        }
    }

    private static ShopFixture Create(bool emptyPool = false)
    {
        var content = new OpeningContent();
        content.Items[Result] = content.Items[Source] with { ItemId = Result };
        content.Items[Crystal] = new(Crystal, new(3, 10000, new(BusinessInventorySpace.Material, 0, 55)), 0, false, [], null, true);
        return new(services =>
        {
            services.AddSingleton<IEquipmentCompoundDefinitionSource>(new SampleContent(emptyPool));
            services.AddSingleton<IPackageOpeningDefinitionSource>(content);
            services.AddSingleton<IItemPlacementDefinitionSource>(content);
        });
    }

    private static async Task Seed(ShopFixture fixture, int count)
    {
        await fixture.Seed(9, Source, 1, 1);
        await fixture.Seed(140, Crystal, count, 3);
        await fixture.SeedShared(Soul, count);
    }

    private static ValueTask Send(ShopFixture fixture, ushort sequence, byte[] body) =>
        EffectRequestTransport.SendRaw(fixture, new(1, 0x0197, 0, sequence), body);

    private sealed class SampleContent(bool emptyPool) : IEquipmentCompoundDefinitionSource
    {
        public EquipmentCompoundContent Load()
        {
            var source = new EquipmentCompoundTemplate(Source, 2, 65, "cl coat", false, "sealing", 100, false);
            var result = source with { ItemId = Result };
            return new(
                new Dictionary<int, IReadOnlySet<string>> { [7] = new HashSet<string> { "cl coat" } },
                new HashSet<int>(), new Dictionary<int, double>(), 1,
                new Dictionary<(int, bool), IReadOnlyList<RecipeAmount>> { [(2, true)] = [new(Soul, 1), new(Crystal, 1)] },
                new Dictionary<(int, bool), IReadOnlyDictionary<int, int>> { [(2, true)] = new Dictionary<int, int> { [65] = 1 } },
                new Dictionary<int, IReadOnlyList<EquipmentCompoundLevel>> { [2] = [new(65, 1, 0)] },
                new Dictionary<int, EquipmentCompoundTemplate> { [Source] = source, [Result] = result },
                new Dictionary<(int, int), IReadOnlyList<EquipmentCompoundTemplate>> { [(2, 65)] = emptyPool ? [] : [result] });
        }
    }
}
