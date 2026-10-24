using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Sessions;
using Dfo.Server.Protocol.Game.BuyItem;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Game;

public sealed class BuyItemCommandFlowTests
{
    [Fact]
    public async Task Ordinary_decoded_purchases_have_no_effect_entry_and_fresh_requests_each_pay_once()
    {
        using var f = World(out var observations);
        await f.Seed(121, 1000, 100, 3);
        Assert.IsType<BuyItemRequest>(WholeBuy.Input(f, 2000, 1));
        await WholeBuy.Send(f, 2000, 1);
        await WholeBuy.Send(f, 2000, 1);
        Assert.Equal(98, f.Count(1000)); Assert.Equal(2, f.Count(2000));
        Assert.Equal(2, f.Sender.Messages.OfType<BuyItemResponse>().Count());
        Assert.Equal(2, observations.Contexts.Count);
        Assert.All(observations.Contexts, context => Assert.IsType<CommandRequestContext.ProtocolNonEffect>(context.Request));
        Assert.NotSame(observations.Contexts[0].Request, observations.Contexts[1].Request);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
    }
    [Fact]
    public async Task A_fresh_wire_purchase_uses_the_current_selection_without_accepting_an_old_owner_input()
    {
        using var f = World(out var observations);
        await f.Seed(121, 1000, 100, 3);
        var old = f.Session;
        f.Select(2);
        await f.Seed(121, 1000, 100, 3);
        await WholeBuy.Send(f, 2000, 1);
        var receipt = Assert.Single(observations.Results).Receipt!;
        Assert.Equal(f.Session, receipt.Session); Assert.NotEqual(old, receipt.Session);
        Assert.Equal(99, f.Count(1000)); Assert.Equal(1, f.Count(2000));
        Assert.Equal(100, f.Scalar("SELECT SUM(count) FROM item_slots WHERE owner_scope='character' AND owner_id=1"));
    }
    [Fact]
    public async Task Ordinary_SQL_failure_rolls_back_the_purchase_and_a_fresh_request_is_independent()
    {
        using var f = World(out var observations);
        await f.Seed(121, 1000, 100, 3);
        f.Sql("CREATE TRIGGER fail_whole_buy BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT,'whole buy failure'); END");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => WholeBuy.Send(f, 2000, 1).AsTask());
        Assert.Equal(100000, f.Gold); Assert.Equal(100, f.Count(1000)); Assert.Equal(0, f.Count(2000));
        Assert.Empty(f.Sender.Messages);
        f.Sql("DROP TRIGGER fail_whole_buy");
        await WholeBuy.Send(f, 2000, 1);
        Assert.Equal(99, f.Count(1000)); Assert.Equal(1, f.Count(2000));
        Assert.Equal(2, observations.Contexts.Count);
        Assert.All(observations.Contexts, ctx => Assert.IsType<CommandRequestContext.ProtocolNonEffect>(ctx.Request));
    }
    [Fact]
    public async Task Housing_notification_failure_is_isolated_and_replay_reads_changed_storage_without_buy_ack()
    {
        using var f = World(out var observations, housing: true);
        await HousingNetworkSettlementTests.Marry(f);
        var first = Assert.IsType<HousingBuyItemRequest>(WholeBuy.Input(f, 10002688, 1, 1206, 20011));
        f.Sender.FailBatch = true;
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection), first, default);
        var selected = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(Assert.Single(observations.Contexts).Request);
        var entry = selected.State.Entry;
        Assert.Equal(EffectOperationState.Committed, entry.State);
        Assert.Equal(98, f.Scalar("SELECT points FROM marriage_personal_points WHERE character_id=1"));
        Assert.Equal(1, f.Scalar("SELECT SUM(count) FROM marriage_housing_storage"));
        Assert.Empty(f.Sender.Messages);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.Null(selected.Failure);
        f.Sender.FailBatch = false;
        f.Sql("UPDATE marriage_housing_storage SET count=3");
        await WholeBuy.Send(f, 10002688, 1, 1206, 20011, 1);
        Assert.Single(observations.Contexts); // Framework replay never enters the purchase handler again.
        Assert.Equal(98, f.Scalar("SELECT points FROM marriage_personal_points WHERE character_id=1"));
        Assert.Equal(3, f.Scalar("SELECT SUM(count) FROM marriage_housing_storage"));
        var frame = Assert.Single(f.Sender.Batches.SelectMany(x => x.Frames), bytes => bytes[0] == 0 && BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(1)) == 0x03D9);
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(15)));
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(25)));
        Assert.DoesNotContain(f.Sender.Messages, message => message is BuyItemResponse);
        Assert.Equal(EffectOperationState.Committed, entry.State);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
    }
    [Fact]
    public async Task A_named_housing_refusal_confirms_rollback_and_the_same_entry_retries_without_extending_its_ordinal()
    {
        using var f = World(out var observations, housing: true);
        await HousingNetworkSettlementTests.Marry(f);
        foreach (var slot in Enumerable.Range(3, 6).Concat(Enumerable.Range(65, 55))) await f.Seed(slot, 1000, 1000);
        await WholeBuy.Send(f, 10002611, 1, 1207, 20011, 7);
        var selected = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(Assert.Single(observations.Contexts).Request);
        var entry = selected.State.Entry; var ordinal = entry.Ordinal;
        Assert.Equal(EffectOperationState.ConfirmedRollback, entry.State);
        Assert.Null(entry.Projection);
        Assert.Equal(1000000, f.Gold); Assert.Equal(100, f.Scalar("SELECT points FROM marriage_personal_points WHERE character_id=1"));
        Assert.Equal(0, f.Count(10002611));
        Assert.Equal(4, Assert.Single(f.Sender.Messages.OfType<BuyItemResponse>()).ErrorCode);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        await f.Seed(65, 1000, 0);
        await WholeBuy.Send(f, 10002611, 1, 1207, 20011, 7);
        var retry = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(observations.Contexts[1].Request);
        Assert.Same(entry, retry.State.Entry); Assert.Equal(ordinal, entry.Ordinal);
        Assert.Equal(EffectOperationState.Committed, entry.State);
        Assert.Equal(1, f.Count(10002611));
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
    }
    [Fact]
    public async Task Unselected_housing_is_rejected_without_main_or_purchase()
    {
        using var f = World(out var observations);
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(f.Session));
        await WholeBuy.Send(f, 10002688, 1, 1206, 20011, 1);
        Assert.IsType<CommandRequestContext.ProtocolEffectRejected>(Assert.Single(observations.Contexts).Request);
        Assert.Equal(NpcShopRejection.NoSession, Assert.Single(observations.Results).Rejection);
        Assert.Null(Assert.Single(f.Sender.Messages.OfType<BuyItemResponse>()).Receipt);
        Assert.Equal(100000, f.Gold);
    }
    [Fact]
    public async Task An_unknown_buy_derived_shape_is_rejected_by_both_entry_points_before_purchase()
    {
        using var f = World(out var observations);
        await f.Seed(121, 1000, 100, 3);
        var input = WholeBuy.Input(f, 2000, 1);
        var unknown = new UnknownBuy(input);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection), unknown, default).AsTask());
        Assert.Empty(observations.Contexts);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Services.GetRequiredService<ICommandBus>().SendAsync(new GameBuyItemCommand(new(f.Connection), unknown)).AsTask());
        Assert.Empty(observations.Contexts);
        Assert.Equal(100, f.Count(1000)); Assert.Equal(0, f.Count(2000)); Assert.Equal(100000, f.Gold);
    }
    [Fact]
    public async Task A_normally_committed_refusal_ack_encoder_failure_uses_default_typed_fencing()
    {
        using var f = World(out var observations);
        var cause = new IOException("actual refusal ACK encoder");
        f.Sender.FailingSends = () => cause;
        var error = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => WholeBuy.Send(f, 999999, 1).AsTask());
        Assert.Same(cause, error.InnerException);
        var context = Assert.Single(observations.Contexts);
        Assert.IsType<CommandRequestContext.ProtocolNonEffect>(context.Request);
        Assert.True(context.Request!.HasCommittedUnit);
        Assert.True(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.Equal(100000, f.Gold);
        Assert.Empty(f.Sender.Messages);
    }
    private static ShopFixture World(out Observations observations, bool housing = false)
    {
        var state = new Observations(); observations = state;
        void Configure(IServiceCollection services)
        {
            services.AddSingleton(state);
            services.AddScoped<GameBuyItemCommandHandler>();
            services.AddScoped<ICommandHandler<GameBuyItemCommand, BuyItemOutcome>, Observer>();
        }
        return housing ? HousingNetworkSettlementTests.Fixture(Configure) : new ShopFixture(Configure);
    }
    private sealed class Observations
    {
        internal List<CommandExecutionContext> Contexts { get; } = [];
        internal List<BuyItemOutcome> Results { get; } = [];
    }
    private sealed class Observer(GameBuyItemCommandHandler actual, Observations state) : ICommandHandler<GameBuyItemCommand, BuyItemOutcome>
    {
        public async ValueTask<BuyItemOutcome> HandleAsync(CommandExecutionContext context, GameBuyItemCommand command, CancellationToken ct)
        {
            state.Contexts.Add(context);
            var result = await actual.HandleAsync(context, command, ct);
            state.Results.Add(result);
            return result;
        }
    }
    private sealed record UnknownBuy(BuyItemRequest Original) : BuyItemRequest(Original.Metadata, Original.ItemId, Original.RawCount, Original.ShopId, Original.NpcId);
}
