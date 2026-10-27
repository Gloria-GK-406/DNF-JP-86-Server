using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Effects;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.MagicSeal;
using Dfo.Server.Protocol.Game.Forging;
using Dfo.Server.Protocol.Game.Outworld;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentOptionProtocolCommandFlowTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Early_rejection_keeps_the_original_silence_or_ack_without_main(int protocol)
    {
        using var f = World(protocol, out var trace);
        await EffectRequestTransport.Send(f, Input(protocol, early: true));
        var request = Selected(trace, 0);
        Assert.Equal(EffectOperationState.Rejected, request.State.Entry.State);
        Assert.Null(request.State.Entry.Projection); Assert.False(request.HasCommittedUnit);
        Assert.Equal(100000, f.Gold);
        if (protocol == 0) Assert.Empty(f.Sender.Messages); else Assert.Single(f.Sender.Messages);
        Assert.Empty(f.Sender.Batches);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Normal_domain_refusal_commits_minimum_inventory_without_changing_assets(int protocol)
    {
        using var f = World(protocol, out var trace);
        await EffectRequestTransport.Send(f, Input(protocol));
        var request = Selected(trace, 0);
        Assert.Equal(EffectOperationState.Committed, request.State.Entry.State); Assert.True(request.HasCommittedUnit);
        Assert.Equal(ReplayProjectionNeeds.Inventory, request.State.Entry.Projection!.Needs);
        Assert.Empty(request.State.Entry.Projection.TouchedSlots); Assert.Equal(100000, f.Gold);
        if (protocol == 0) Assert.Empty(f.Sender.Messages); else Assert.Single(f.Sender.Messages);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Initially_unselected_cannot_upgrade_when_a_new_selection_appears_before_the_actual_handler(int protocol)
    {
        using var f = World(protocol, out var trace);
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(f.Session));
        trace.Before = () => f.Select(2);
        await EffectRequestTransport.Send(f, Input(protocol));
        var denied = Assert.IsType<CommandRequestContext.ProtocolEffectRejected>(trace.Contexts[0].Request);
        Assert.False(denied.HasCommittedUnit); Assert.Equal(100000, f.Gold);
        if (protocol == 0) Assert.Empty(f.Sender.Messages); else Assert.Single(f.Sender.Messages);
        trace.Before = null;
        await EffectRequestTransport.Send(f, Input(protocol, sequence: 8));
        Assert.Equal(EffectOperationState.Committed, Selected(trace, 1).State.Entry.State);
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Direct_refusal_ack_actual_encoder_failure_retains_default_typed_fencing(int protocol)
    {
        using var f = World(protocol, out var trace);
        var cause = new IOException("option refusal actual encoder"); f.Sender.FailingSends = () => cause;
        var error = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => EffectRequestTransport.Send(f, Input(protocol)).AsTask());
        Assert.Same(cause, error.InnerException);
        var request = Selected(trace, 0); Assert.True(request.HasCommittedUnit);
        Assert.Equal(EffectOperationState.Committed, request.State.Entry.State);
        Assert.True(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.Equal(100000, f.Gold); Assert.Empty(f.Sender.Messages);
    }
    [Fact]
    public async Task A_failed_forging_roll_still_commits_a_receipt_and_actual_material_debit()
    {
        var trace = new Trace();
        using var f = EquipmentForgingHandlerTests.Fixture(5000, services => Configure(services, trace));
        await f.SeedCore(9, Piece(2) with { GenuineUpgrade = 1 }); await f.Seed(121, 3326, 50);
        var original = f.StoredCore(9);
        await EffectRequestTransport.Send(f, Input(2));
        var receipt = Assert.IsType<EquipmentForgingResult>(Assert.Single(trace.Results)).Receipt!;
        Assert.NotNull(receipt); Assert.False(receipt.Succeeded); Assert.Equal(5000, receipt.Roll);
        Assert.Equal(7, f.Count(3326)); Assert.Equal(original, f.StoredCore(9));
        var ack = Assert.IsType<ForgingResponse>(f.Sender.Messages[0]); Assert.True(ack.Success); Assert.False(ack.ForgeSucceeded);
        Assert.Equal((byte)1, ack.NewLevel); Assert.Equal(7, ack.MaterialRemaining);
        var request = Selected(trace, 0); Assert.True(request.HasCommittedUnit);
        Assert.Equal(EffectOperationState.Committed, request.State.Entry.State); Assert.NotEmpty(request.State.Entry.Projection!.TouchedSlots);
    }
    [Fact]
    public async Task Initial_change_notification_encoder_failure_is_isolated_then_replay_reads_current_gold_without_ack()
    {
        using var f = World(1, out var trace); await f.SeedCore(9, Piece(1));
        f.Sender.FailBatch = true;
        await EffectRequestTransport.Send(f, Input(1));
        var request = Selected(trace, 0); Assert.Equal(EffectOperationState.Committed, request.State.Entry.State);
        Assert.Null(request.Failure); Assert.Equal(99800, f.Gold); Assert.Empty(f.Sender.Messages);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        f.Sender.FailBatch = false; f.Sql("UPDATE character_stats SET carried_gold=99000 WHERE character_id=1");
        await EffectRequestTransport.Send(f, Input(1));
        Assert.Single(trace.Contexts); Assert.Equal(99000, EffectRequestTransport.MainValue(f, 0));
        Assert.Empty(f.Sender.Messages.OfType<ChangeRandomOptionResponse>()); Assert.Equal(99000, f.Gold);
        Assert.Equal(EffectOperationState.Committed, request.State.Entry.State);
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Real_SQL_statement_failure_rolls_back_then_retries_the_same_entry_and_ordinal(int protocol)
    {
        using var f = World(protocol, out var trace); await f.SeedCore(9, Piece(protocol));
        var material = protocol == 2 ? 3326 : 1183;
        if (protocol != 1) await f.Seed(121, material, protocol == 2 ? 50 : 2);
        var original = f.StoredCore(9);
        f.Sql("CREATE TRIGGER fail_option BEFORE UPDATE ON items WHEN NEW.item_kind=1 AND NEW.item_core<>OLD.item_core BEGIN SELECT RAISE(ABORT,'option SQL failure'); END");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => EffectRequestTransport.Send(f, Input(protocol)).AsTask());
        var entry = Selected(trace, 0).State.Entry; var ordinal = entry.Ordinal;
        Assert.Equal(EffectOperationState.ConfirmedRollback, entry.State); Assert.Null(entry.Projection);
        Assert.Equal(100000, f.Gold); Assert.Equal(original, f.StoredCore(9)); Assert.Empty(f.Sender.Messages);
        if (protocol != 1) Assert.Equal(protocol == 2 ? 50 : 2, f.Count(material));
        f.Sql("DROP TRIGGER fail_option"); await EffectRequestTransport.Send(f, Input(protocol));
        Assert.Same(entry, Selected(trace, 1).State.Entry); Assert.Equal(ordinal, entry.Ordinal);
        Assert.Equal(EffectOperationState.Committed, entry.State);
        Assert.Equal(protocol == 1 ? 99800 : 100000, f.Gold);
        if (protocol != 1) Assert.Equal(protocol == 2 ? 11 : 1, f.Count(material));
        var acks = f.Sender.Messages.Count(message => message is ChangeRandomOptionResponse or ForgingResponse or PurifyItemResponse);
        await EffectRequestTransport.Send(f, Input(protocol));
        Assert.Equal(2, trace.Contexts.Count); Assert.Equal(ordinal, entry.Ordinal);
        Assert.Equal(acks, f.Sender.Messages.Count(message => message is ChangeRandomOptionResponse or ForgingResponse or PurifyItemResponse));
        Assert.Equal(protocol == 1 ? 99800 : 100000, f.Gold);
        if (protocol != 1) Assert.Equal(protocol == 2 ? 11 : 1, f.Count(material));
    }
    private static InventoryItemCoreState Piece(int protocol) => StackableItemCore.Create(1, 3000, 1234) with
    { Durability = 45, RandomOption0 = protocol == 1 ? new(15, 2, 2) : new(0, 0, 0), AmplifyType = protocol == 3 ? (byte)0x80 : (byte)0 };
    private static IEffectSettlementRequest Input(int protocol, bool early = false, ushort sequence = 7) => protocol switch
    {
        0 => new MagicSealRequest(new(1, 0x191, 0, sequence), false, 9, early ? (short)0 : (short)-1),
        1 => new MagicSealRequest(new(1, 0x1B6, 0, sequence), true, 9, early ? (short)121 : (short)0),
        2 => new ForgingRequest(new(1, 0x1B7, 0, sequence), 0, 9, 3000, 121, [0x41], early ? (byte)1 : (byte)0),
        3 => new OutworldRequest(new(1, 0xCC, 0, sequence), true, 0, 9, 3000, early ? (short)9 : (short)121, 1183, 0),
        4 => new OutworldRequest(new(1, 0xCD, 0, sequence), false, early ? (byte)2 : (byte)0, 9, 3000, 121, 1184, 3),
        _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
    };
    private static CommandRequestContext.ProtocolEffectSelected Selected(Trace trace, int n) => Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(trace.Contexts[n].Request);
    private static ShopFixture World(int protocol, out Trace trace)
    {
        var state = new Trace(); trace = state;
        return protocol < 2 ? MagicSealHandlerTests.Fixture(services => Configure(services, state))
            : protocol == 2 ? EquipmentForgingHandlerTests.Fixture(0, services => Configure(services, state))
            : EquipmentOutworldHandlerTests.Fixture(services => Configure(services, state));
    }
    private static void Configure(IServiceCollection services, Trace trace)
    {
        services.AddSingleton(trace);
        services.AddScoped<GameMagicSealCommandHandler>(); services.AddScoped<GameForgingCommandHandler>(); services.AddScoped<GameOutworldCommandHandler>();
        services.AddScoped<ICommandHandler<GameMagicSealCommand, EquipmentMagicSealResult>, Observe<GameMagicSealCommand, EquipmentMagicSealResult, GameMagicSealCommandHandler>>();
        services.AddScoped<ICommandHandler<GameForgingCommand, EquipmentForgingResult>, Observe<GameForgingCommand, EquipmentForgingResult, GameForgingCommandHandler>>();
        services.AddScoped<ICommandHandler<GameOutworldCommand, EquipmentOutworldResult>, Observe<GameOutworldCommand, EquipmentOutworldResult, GameOutworldCommandHandler>>();
    }
    private sealed class Trace { internal List<CommandExecutionContext> Contexts { get; } = []; internal List<object> Results { get; } = []; internal Action? Before { get; set; } }
    private sealed class Observe<TCommand, TResult, THandler>(THandler actual, Trace trace) : ICommandHandler<TCommand, TResult>
        where TCommand : class, ICommand<TResult> where TResult : notnull where THandler : ICommandHandler<TCommand, TResult>
    {
        public async ValueTask<TResult> HandleAsync(CommandExecutionContext context, TCommand command, CancellationToken ct)
        { trace.Contexts.Add(context); trace.Before?.Invoke(); var result = await actual.HandleAsync(context, command, ct); trace.Results.Add(result); return result; }
    }
}
