using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol.Game.SecretShop;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class DungeonSecretShopTests
{
    [Theory]
    [InlineData(1002, 1002)]
    [InlineData(1003, 1003)]
    [InlineData(1004, 1004)]
    [InlineData(1000, 1000)]
    public void Dungeon_weights_override_level_and_choose_a_matching_pool(int configured, int expected)
    {
        var catalog = new DungeonSecretShopCatalog([new(0, 1, 90)],
            [new(144, configured, [1])], [new(0, 1000, [1, 1, 1, 1, 1])],
            [new(configured, 144, true, 1, [new(new(2000, 8, 0, 3), 1)])]);
        var rules = new DungeonSecretShopRuleService(new SecretShopContent(catalog));
        var dungeon = rules.Choose(Layout(), 1, _ => 0);
        var fallback = rules.Choose(Layout() with { DungeonId = 999 }, 1, _ => 0);

        Assert.Equal(expected, dungeon.NpcId);
        Assert.Equal(expected != 1000, dungeon.Pool is not null);
        Assert.Equal((1000, null), fallback);
    }

    [Fact]
    public void Group_size_controls_level_weights_and_duplicate_candidates_do_not_duplicate_products()
    {
        var catalog = new DungeonSecretShopCatalog([new(0, 17, 70)], [],
            [new(0, 1000, [10, 0, 0, 0, 0]), new(0, 1002, [0, 10, 10, 10, 10])],
            [new(1002, 0, false, 2, [new(new(2000, 8, 0, 3), 1), new(new(2000, 9, 0, 3), 1), new(new(5000, 8, 0, 3), 1)])]);
        var rules = new DungeonSecretShopRuleService(new SecretShopContent(catalog));
        Assert.Equal(1000, rules.Choose(Layout(), 1, _ => 0).NpcId);
        Assert.Equal(1000, rules.Choose(Layout() with { BasisLevel = 16 }, 2, _ => 0).NpcId);
        var group = rules.Choose(Layout(), 2, _ => 0);
        Assert.Equal(1002, group.NpcId);
        Assert.Equal(new[] { 2000, 5000 }, DungeonSecretShopRuleService.DrawOffer(group.NpcId, group.Pool!, _ => 0)
            .Items.Select(item => item.ItemId));
        Assert.Equal(1000, rules.Choose(Layout() with { Mode = new DungeonRunMode.Plain(DungeonRunKind.Dimension) }, 2, _ => 0).NpcId);
    }

    [Fact]
    public async Task Gold_purchase_survives_reopen_and_replay_without_a_second_debit_and_sold_out_cannot_buy()
    {
        using var fixture = Fixture(new(2000, 35, 0, 3));
        fixture.Sql("""
            INSERT INTO character_condition(character_id,pc_room_state) VALUES(1,0);
            INSERT INTO character_pvp_record(character_id) VALUES(1);
            INSERT INTO character_pvp_skill_state(character_id) VALUES(1);
            INSERT INTO character_settings(character_id) VALUES(1);
            INSERT INTO character_expert_job(character_id) VALUES(1);
            """);
        var run = Begin(fixture);
        var wire = new GameInboundFrameCounter();
        await Buy(fixture, wire, 1, 2000, 1);
        Assert.Equal(99_965, fixture.Gold);
        Assert.Equal(1, fixture.Count(2000));
        Assert.Equal(2, Stock(fixture));
        var first = Assert.Single(fixture.Sender.Messages.OfType<SecretShopBuyResponse>());
        Assert.Equal(2, first.Result.RemainingStock);

        await Window(fixture, false);
        await Window(fixture, true);
        Assert.Null(TestGameplay.Run(fixture.Services, () => fixture.Services.DungeonRuns()
            .TryOpenClear(fixture.Session.CharacterId, run.RunId, run.RoomVersion)));
        await Buy(fixture, wire, 1, 2000, 1);
        Assert.Equal(99_965, fixture.Gold);
        Assert.Equal(1, fixture.Count(2000));
        Assert.Equal(2, Stock(fixture));
        Assert.Single(fixture.Sender.Messages.OfType<SecretShopBuyResponse>());

        await Buy(fixture, wire, 2, 2000, 2);
        await Buy(fixture, wire, 3, 2000, 1);
        Assert.Equal(99_895, fixture.Gold);
        Assert.Equal(3, fixture.Count(2000));
        Assert.Equal(0, Stock(fixture));
        Assert.Equal(NpcShopRejection.PurchaseLimitReached,
            fixture.Sender.Messages.OfType<SecretShopBuyResponse>().Last().Result.Rejection);
    }

    [SkippableFact]
    public async Task Real_black_cube_purchase_updates_the_account_material_and_ack_slot()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var fixture = Fixture(new(3033, 100, 0, 3), services =>
        {
            services.AddSingleton<INpcShopDefinitionSource, PvfNpcShopDefinitionSource>();
            services.AddSingleton<IItemPlacementDefinitionSource, PvfItemPlacementDefinitionSource>();
        });
        Begin(fixture);
        await Buy(fixture, new GameInboundFrameCounter(), 1, 3033, 1);

        var receipt = Assert.Single(fixture.Sender.Messages.OfType<SecretShopBuyResponse>()).Result;
        Assert.Equal(NpcShopRejection.None, receipt.Rejection);
        Assert.Equal(99_900, fixture.Gold);
        Assert.Equal(2, Stock(fixture));
        var held = fixture.InTransaction(() => fixture.Shared().GetAllAsync(default).AsTask().GetAwaiter().GetResult());
        Assert.Equal(1, Assert.Single(held, item => item.Snapshot.ItemId == 3033).Count);
        var ack = SecretShopBuyEncoder.EncodeBody(receipt);
        Assert.Equal(30, ack.Length);
        Assert.Equal(354, BinaryPrimitives.ReadUInt16LittleEndian(ack.AsSpan(5)));
        Assert.Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(ack.AsSpan(18)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(ack.AsSpan(26)));
    }

    [Fact]
    public async Task The_rolled_material_price_and_current_run_govern_purchase()
    {
        using var fixture = Fixture(new(5000, 2, 1000, 2));
        await fixture.Seed(121, 1000, 3, 3);
        var run = Begin(fixture);
        var wire = new GameInboundFrameCounter();

        await Buy(fixture, wire, 1, 5000, 2);
        Assert.Equal(NpcShopRejection.InsufficientMaterials,
            fixture.Sender.Messages.OfType<SecretShopBuyResponse>().Last().Result.Rejection);
        Assert.Equal(3, fixture.Count(1000));
        Assert.Equal(0, fixture.Count(5000));
        Assert.Equal(2, Stock(fixture));

        await Buy(fixture, wire, 2, 5000, 1);
        var receipt = fixture.Sender.Messages.OfType<SecretShopBuyResponse>().Last().Result;
        Assert.Equal(1000, receipt.CostItemId);
        Assert.Equal(1, receipt.CostItemRemaining);
        Assert.Equal(1, fixture.Count(1000));
        Assert.Equal(1, fixture.Count(5000));
        Assert.Equal(100_000, fixture.Gold);
        Assert.Equal(1, Stock(fixture));

        Assert.True(TestGameplay.Run(fixture.Services, () => fixture.Services.DungeonRuns()
            .End(fixture.Session.CharacterId, run.RunId)));
        await Buy(fixture, wire, 3, 5000, 1);
        Assert.Equal(NpcShopRejection.ShopUnavailable,
            fixture.Sender.Messages.OfType<SecretShopBuyResponse>().Last().Result.Rejection);
        Assert.Equal(1, fixture.Count(5000));
    }

    [Fact]
    public async Task Gold_and_capacity_rejections_preserve_the_offered_stock()
    {
        using var fixture = Fixture(new(2000, 35, 0, 3));
        fixture.Sql("UPDATE character_stats SET carried_gold=34 WHERE character_id=1");
        Begin(fixture);
        var wire = new GameInboundFrameCounter();
        await Buy(fixture, wire, 1, 2000, 1);
        Assert.Equal(NpcShopRejection.InsufficientGold,
            fixture.Sender.Messages.OfType<SecretShopBuyResponse>().Last().Result.Rejection);
        Assert.Equal(34, fixture.Gold);
        Assert.Equal(3, Stock(fixture));

        using var crowded = Fixture(new(3000, 35, 0, 1));
        await crowded.Seed(9, 3000, 100, 1);
        await crowded.Seed(10, 3000, 101, 1);
        Begin(crowded);
        await Buy(crowded, new GameInboundFrameCounter(), 1, 3000, 1);
        Assert.Equal(NpcShopRejection.NoRoom,
            crowded.Sender.Messages.OfType<SecretShopBuyResponse>().Last().Result.Rejection);
        Assert.Equal(100_000, crowded.Gold);
        Assert.Equal(2, crowded.Count(3000));
        Assert.Equal(1, Stock(crowded));
    }

    [Fact]
    public async Task Party_members_share_the_npc_and_keep_independent_stock()
    {
        var catalog = new SecretShopContent(new([], [new(144, 1003, [1])], [],
            [new(1003, 144, true, 1, [new(new(2000, 35, 0, 3), 1)])]));
        using var fixture = new ShopFixture(services => services.AddDungeonRunsAs(provider =>
            TestParties.Runs(provider.GetRequiredService<IDomainTransactionAccessor>(), secretShops: catalog)));
        var runs = fixture.Services.DungeonRuns();
        var actor = fixture.Session;
        var helper = new Dfo.Server.Data.Characters.CharacterId(2);
        var run = fixture.InTransaction(() => runs.BeginParty(
            [new(actor.AccountId, actor.CharacterId, 0, true), new(actor.AccountId, helper, 1, false)],
            Layout(), DungeonDifficulty.Normal));
        Assert.NotNull(TestGameplay.Run(fixture.Services, () => runs.TryOpenClear(actor.CharacterId, run.RunId, run.RoomVersion)));

        await Buy(fixture, new GameInboundFrameCounter(), 1, 2000, 1);

        Assert.Equal(1003, runs.Find(actor.CharacterId)!.SecretShop!.NpcId);
        var memberOffer = runs.Find(helper)!.SecretShop!;
        Assert.Equal(1003, memberOffer.NpcId);
        Assert.Equal(2, Stock(fixture));
        Assert.Equal(3, Assert.Single(memberOffer.Items).RemainingCount);
    }

    [Fact]
    public async Task Rolling_back_an_enlisted_purchase_preserves_gold_bag_and_stock()
    {
        using var fixture = Fixture(new(2000, 35, 0, 3));
        Begin(fixture);
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using var tx = fixture.Services.GetRequiredService<DomainTransactionScope>().Begin();
            var result = await TestEffectFootprints.Run([fixture.Session], footprint =>
                fixture.Services.GetRequiredService<INpcShopDomainService>().PurchaseSecretAsync(
                    fixture.Session, 2000, 2, fixture.Clock.GetUtcNow(), footprint, default));
            Assert.Equal(NpcShopRejection.None, result.Rejection);
            Assert.Equal(1, result.RemainingStock);
            Assert.Equal(3, Stock(fixture));
        });

        Assert.Equal(100_000, fixture.Gold);
        Assert.Equal(0, fixture.Count(2000));
        Assert.Equal(3, Stock(fixture));
    }

    private static ShopFixture Fixture(DungeonSecretShopItem item, Action<IServiceCollection>? configure = null) => new(services =>
    {
        services.AddSingleton<IDungeonSecretShopContentSource>(new SecretShopContent(
            new([], [new(144, 1003, [1])], [], [new(1003, 144, true, 1, [new(item, 1)])])));
        configure?.Invoke(services);
    });

    private static DungeonRunState Begin(ShopFixture fixture)
    {
        var runs = fixture.Services.DungeonRuns();
        var run = fixture.InTransaction(() => runs.Begin(fixture.Session.CharacterId, Layout(), DungeonDifficulty.Normal));
        Assert.NotNull(TestGameplay.Run(fixture.Services, () =>
            runs.TryOpenClear(fixture.Session.CharacterId, run.RunId, run.RoomVersion)));
        return run;
    }

    private static DungeonLayout Layout() => new(144, 0, 1, 1, new(0, 0), new(0, 0), 55611, 0,
        new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(55611) }, BasisLevel: 20);

    private static int Stock(ShopFixture fixture) =>
        fixture.Services.DungeonRuns().Find(fixture.Session.CharacterId)!.SecretShop!.Items.Single().RemainingCount;

    private static ValueTask Buy(ShopFixture fixture, GameInboundFrameCounter counter, ushort sequence, int item, int count)
    {
        var (ordinal, fresh) = counter.Observe(sequence);
        var body = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(body, item);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), count);
        var metadata = new GameMessageMetadata(1, 0x0128, 0, sequence)
        {
            InboundOrdinal = ordinal,
            SequenceIsFresh = fresh,
        }.WithRawBody(body);
        return Dispatch(fixture, new SecretShopBuyDecoder().Decode(metadata, body));
    }

    private static ValueTask Window(ShopFixture fixture, bool open) =>
        Dispatch(fixture, new SecretShopWindowDecoder().Decode(GameMessageMetadata.Empty, [open ? (byte)1 : (byte)0]));

    private static ValueTask Dispatch(ShopFixture fixture, IClientNetworkMessage input) =>
        fixture.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(fixture.Connection), input, default);

    private sealed record SecretShopContent(DungeonSecretShopCatalog Catalog) : IDungeonSecretShopContentSource
    {
        public DungeonSecretShopCatalog Read() => Catalog;
    }
}
