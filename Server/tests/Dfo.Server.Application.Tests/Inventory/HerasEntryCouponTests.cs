using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol.Game.QuestClearCoupon;
using Dfo.Server.Protocol.Game.UseStackable;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class HerasEntryCouponTests
{
    private const int Coupon = 490002847;
    private static readonly BusinessSlot Source = new(BusinessInventorySpace.Consumable, 4);
    private sealed class Content : IStackableActionDefinitionSource
    {
        public StackableActionDefinition Rule = new("[add enter limit dungeonincount]", 50, 85, 0, true)
            { EntryDungeonId = 3900, EntryIncrease = 1 };
        public ValueTask<StackableActionDefinition?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<StackableActionDefinition?>(Rule);
    }
    private static CeraShopFixture Fixture(Content? content = null)
    {
        var f = new CeraShopFixture(configure: s =>
        {
            s.AddSingleton<IStackableActionDefinitionSource>(content ?? new Content());
            s.AddSingleton<IItemUseRuleDefinitionSource>(new FakeItemUseRules((Coupon, ItemUseRules.Potion(0))));
        });
        f.Content.Placements[Coupon] = new(2, 1000, new(BusinessInventorySpace.Consumable, 0, 55));
        return f;
    }
    private static ValueTask<HerasQuotaSnapshot> Quota(CeraShopFixture f) => f.Host.Services.GetRequiredService<IHerasDungeonDomainService>().ReadQuotaAsync(f.Host.Session.CharacterId, default);
    private static Task<ItemUseResult> Use(CeraShopFixture f) => f.Host.Services.GetRequiredService<ICommandBus>()
        .SendAsync(new UseStackableCommand(f.Host.Session, Source, Coupon)).AsTask();

    [Fact]
    public async Task Coupons_are_not_usage_limited_to_two_and_both_counters_reset_at_six()
    {
        using var f = Fixture();
        await f.Host.Seed(69, Coupon, 4);
        f.Host.Clock.Advance(new DateTimeOffset(2026, 9, 9, 5, 59, 0, TimeSpan.FromHours(8)) - f.Host.Clock.GetUtcNow());
        for (var i = 0; i < 3; i++) Assert.Equal(ItemUseStatus.Used, (await Use(f)).Status);
        Assert.Equal(1, f.Host.Count(Coupon));
        var q = await Quota(f);
        Assert.Equal(6, q.Remaining);
        await TestGameplay.RunAsync(f.Host.Services, async () =>
        {
            await using (var tx = f.Host.Services.GetRequiredService<DomainTransactionScope>().Begin())
            {
                await f.Host.Services.GetRequiredService<IEventService>().DirectAsync(new HerasQuotaRecorded(f.Host.Session.CharacterId, q.Period, "entries", null, 4), default);
                await tx.CommitAsync(default);
            }
        });
        Assert.Equal(2, (await Quota(f)).Remaining);
        f.Host.Clock.Advance(TimeSpan.FromMinutes(1));
        var fresh = await Quota(f);
        Assert.Equal(q.Period.AddDays(1), fresh.Period);
        Assert.Equal(3, fresh.Remaining);
        Assert.Equal(0, fresh.Granted);
        Assert.Equal(0, fresh.Used);
        Assert.Equal(1, f.Host.Count(Coupon));
    }

    [Fact]
    public async Task Quota_write_failure_rolls_back_the_coupon_and_bonus()
    {
        using var f = Fixture();
        await f.Host.Seed(69, Coupon, 2);
        f.Host.Sql("CREATE TRIGGER fail_heras BEFORE INSERT ON character_daily_counters BEGIN SELECT RAISE(ABORT,'heras quota'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Use(f));
        Assert.Equal(2, f.Host.Count(Coupon));
        Assert.Equal(3, (await Quota(f)).Remaining);
    }

    [Theory]
    [InlineData(49, 3900, 1, true)]
    [InlineData(85, 3700, 1, true)]
    [InlineData(85, 3900, 2, true)]
    [InlineData(85, 3900, 1, false)]
    public async Task Refused_coupon_definitions_preserve_items(byte level, int dungeon, int increase, bool readable)
    {
        var content = new Content();
        content.Rule = content.Rule with { EntryDungeonId = dungeon, EntryIncrease = increase, IsReadable = readable };
        using var f = Fixture(content);
        f.Host.SetLevel(level);
        await f.Host.Seed(69, Coupon, 2);
        Assert.NotEqual(ItemUseStatus.Used, (await Use(f)).Status);
        Assert.Equal(2, f.Host.Count(Coupon));
        Assert.Equal(3, (await Quota(f)).Remaining);
    }

    [Fact]
    public async Task Byte_limit_refuses_without_consumption()
    {
        using var f = Fixture();
        await f.Host.Seed(69, Coupon, 2);
        var q = await Quota(f);
        await TestGameplay.RunAsync(f.Host.Services, async () =>
        {
            await using (var tx = f.Host.Services.GetRequiredService<DomainTransactionScope>().Begin())
            {
                await f.Host.Services.GetRequiredService<IEventService>().DirectAsync(new HerasQuotaRecorded(f.Host.Session.CharacterId, q.Period, "bonus", null, 252), default);
                await tx.CommitAsync(default);
            }
        });
        Assert.NotEqual(ItemUseStatus.Used, (await Use(f)).Status);
        Assert.Equal(2, f.Host.Count(Coupon));
        Assert.Equal(255, (await Quota(f)).Remaining);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(58)]
    [InlineData(77)]
    public async Task Generic_action_uses_the_owned_item_then_replay_only_refreshes_current_state(uint action)
    {
        using var f = Fixture();
        await f.Host.Seed(69, Coupon, 2);
        var counter = new GameInboundFrameCounter();
        QuestClearCouponRequest Request()
        {
            var (ordinal, fresh) = counter.Observe(1);
            var body = new byte[19];
            BinaryPrimitives.WriteInt16LittleEndian(body, 69);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(7), action);
            var metadata = new GameMessageMetadata(1, 0x0204, 0, 1) { InboundOrdinal = ordinal, SequenceIsFresh = fresh }.WithRawBody(body);
            var registration = f.Host.Services.GetServices<GameMessageDecoderRegistration>().Single(x => x.Type == 0x0204 && x.Selector == 1);
            return Assert.IsType<QuestClearCouponRequest>(((IGameMessageDecoder)f.Host.Services.GetRequiredService(registration.DecoderType)).Decode(metadata, body));
        }
        var dispatcher = f.Host.Services.GetRequiredService<INetworkMessageDispatcher>();
        await dispatcher.DispatchAsync(new(f.Host.Connection), Request(), default);
        Assert.True(Assert.Single(f.Host.Sender.Messages.OfType<QuestClearCouponResponse>()).Succeeded);
        Assert.Equal(1, f.Host.Count(Coupon));
        Assert.Equal(4, (await Quota(f)).Remaining);
        Assert.Contains(f.Host.Sender.Messages.Select(f.Encode), frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == 0x0227 && frame[^1] == 4);
        await dispatcher.DispatchAsync(new(f.Host.Connection), Request(), default);
        var replay = Assert.Single(f.Host.Sender.Batches, EffectRequestTransport.IsStateReplay);
        Assert.Equal(2, Assert.Single(f.Host.Sender.Batches, batch => batch.Messages.Any(message => message is QuestClearCouponResponse)).Messages.Length);
        Assert.DoesNotContain(replay.Messages, m => m is QuestClearCouponResponse);
        Assert.Contains(replay.Frames, frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == 0x0227 && frame[^1] == 4);
        Assert.Equal(1, f.Host.Count(Coupon));
        Assert.Equal(4, (await Quota(f)).Remaining);
    }

    [SkippableFact]
    public async Task Real_archive_coupon_and_unsupported_MLA_coupon_take_distinct_paths()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH")), "PVF unavailable");
        using var f = new CeraShopFixture(useArchive: true);
        f.Host.SetLevel(70);
        await f.Host.Seed(69, Coupon, 2);
        Assert.Equal(ItemUseStatus.Used, (await Use(f)).Status);
        Assert.Equal(1, f.Host.Count(Coupon));
        Assert.Equal(4, (await Quota(f)).Remaining);
        await f.Host.Seed(69, 490002848, 1);
        var result = await f.Host.Services.GetRequiredService<ICommandBus>().SendAsync(new UseStackableCommand(f.Host.Session, Source, 490002848));
        Assert.NotEqual(ItemUseStatus.Used, result.Status);
        Assert.Equal(1, f.Host.Count(490002848));
    }

    [Theory]
    [InlineData(1, 42)]
    [InlineData(0, 256)]
    public async Task Malformed_requests_finish_with_refusal_without_touching_the_bag(uint reserved, uint action)
    {
        using var f = Fixture();
        await f.Host.Seed(69, Coupon, 2);
        await TestGameplay.RunAsync(f.Host.Services, async () =>
            await f.Host.Services.GetRequiredService<QuestClearCouponHandler>().HandleAsync(new(f.Host.Connection),
                new QuestClearCouponRequest(GameMessageMetadata.Empty, 69, 0, reserved, action, 0, 0, true), default));
        Assert.False(Assert.Single(f.Host.Sender.Messages.OfType<QuestClearCouponResponse>()).Succeeded);
        Assert.Equal(2, f.Host.Count(Coupon));
        Assert.Equal(3, (await Quota(f)).Remaining);
    }
    [Fact]
    public async Task Wrong_action_declaration_never_falls_through_to_bare_consumption()
    {
        var content = new Content { Rule = new("[unknown]", 50, 85, 0, true) };
        using var f = Fixture(content);
        await f.Host.Seed(69, Coupon, 2);
        Assert.NotEqual(ItemUseStatus.Used, (await Use(f)).Status);
        Assert.Equal(2, f.Host.Count(Coupon));
        Assert.Equal(3, (await Quota(f)).Remaining);
    }
}
