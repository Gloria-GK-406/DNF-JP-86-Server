using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Effects;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.ReinforceItem;
using Dfo.Server.Protocol.Game.DisjointItem;
using Dfo.Server.Protocol.Game.EnchantByBead;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentProtocolCommandFlowTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task A_domain_refusal_commits_the_original_minimum_inventory_without_spending(int protocol)
    {
        using var f = World(out var trace);
        await EffectRequestTransport.Send(f, Input(protocol, early: false));
        var request = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(Assert.Single(trace.Contexts).Request);
        Assert.Equal(EffectOperationState.Committed, request.State.Entry.State);
        Assert.NotNull(request.State.Entry.Projection);
        Assert.Equal(ReplayProjectionNeeds.Inventory, request.State.Entry.Projection.Needs);
        Assert.Empty(request.State.Entry.Projection.TouchedSlots);
        Assert.True(request.HasCommittedUnit);
        Assert.Equal(100000, f.Gold);
        Assert.Empty(f.Sender.Batches);
        Assert.Single(f.Sender.Messages);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Pre_domain_refusal_does_not_claim_main_or_freeze_a_projection(int protocol)
    {
        using var f = World(out var trace);
        await EffectRequestTransport.Send(f, Input(protocol, early: true));
        var request = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(Assert.Single(trace.Contexts).Request);
        Assert.Equal(EffectOperationState.Rejected, request.State.Entry.State);
        Assert.Null(request.State.Entry.Projection);
        Assert.False(request.HasCommittedUnit);
        Assert.Equal(100000, f.Gold);
        Assert.Single(f.Sender.Messages); Assert.Empty(f.Sender.Batches);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Initially_unselected_request_cannot_upgrade_when_selection_appears_before_the_handler(int protocol)
    {
        using var f = World(out var trace);
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(f.Session));
        trace.Before = () => f.Select(2);
        await EffectRequestTransport.Send(f, Input(protocol, early: false));
        var request = Assert.IsType<CommandRequestContext.ProtocolEffectRejected>(Assert.Single(trace.Contexts).Request);
        Assert.False(request.HasCommittedUnit);
        Assert.Single(f.Sender.Messages); Assert.Equal(100000, f.Gold);
        trace.Before = null;
        await EffectRequestTransport.Send(f, Input(protocol, early: false) switch
        {
            ReinforceItemRequest x => x with { Metadata = x.Metadata with { Sequence = 8 } },
            DisjointItemRequest x => x with { Metadata = x.Metadata with { Sequence = 8 } },
            EnchantByBeadRequest x => x with { Metadata = x.Metadata with { Sequence = 8 } },
            _ => throw new InvalidOperationException(),
        });
        var selected = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(trace.Contexts[1].Request);
        Assert.Equal(EffectOperationState.Committed, selected.State.Entry.State);
    }
    [Fact]
    public async Task Ambiguous_portable_provider_never_claims_or_spends_and_next_fresh_request_still_works()
    {
        var trace = new Trace();
        using var f = EquipmentDisjointTests.Fixture(configure: services => Configure(services, trace));
        await f.SeedCore(9, StackableItemCore.Create(1, 3000, 1234) with { Durability = 45 });
        await f.Seed(65, 2600469, 1);
        await EffectRequestTransport.Send(f, Input(1, early: true));
        Assert.Equal((ushort)45, await f.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(1, f.Count(2600469)); Assert.Equal(0, f.Count(1000));
        var denied = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(trace.Contexts[0].Request);
        Assert.Equal(EffectOperationState.Rejected, denied.State.Entry.State); Assert.Null(denied.State.Entry.Projection);
        Assert.False(denied.HasCommittedUnit);
        Assert.Equal(0x13, Assert.Single(f.Sender.Messages.OfType<DisjointItemResponse>()).ErrorCode);
        await EffectRequestTransport.Send(f, new DisjointItemRequest(new(1, 0x1A, 0, 8), new(InventoryContainerType.Main, 9), 65, 0));
        Assert.Null(await f.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(0, f.Count(2600469)); Assert.Equal(12, f.Count(1000));
        var selected = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(trace.Contexts[1].Request);
        Assert.Equal(EffectOperationState.Committed, selected.State.Entry.State);
        Assert.NotEmpty(selected.State.Entry.Projection!.TouchedSlots);
    }
    [Fact]
    public async Task Disjoint_mapping_failure_still_precedes_a_missing_selection_refusal()
    {
        using var f = World(out var trace);
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(f.Session));
        var input = new DisjointItemRequest(new(1, 0x1A, 0, 7), new(InventoryContainerType.Main, -1), -1, 1000);
        await Assert.ThrowsAsync<InventorySlotMappingException>(() => EffectRequestTransport.Send(f, input).AsTask());
        var request = Assert.IsType<CommandRequestContext.ProtocolEffectRejected>(Assert.Single(trace.Contexts).Request);
        Assert.False(request.HasCommittedUnit); Assert.Empty(f.Sender.Messages);
        Assert.Equal(100000, f.Gold);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Committed_domain_refusal_ack_encoder_failure_keeps_default_typed_fencing(int protocol)
    {
        using var f = World(out var trace);
        var cause = new IOException("equipment refusal actual encoder"); f.Sender.FailingSends = () => cause;
        var error = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => EffectRequestTransport.Send(f, Input(protocol, early: false)).AsTask());
        Assert.Same(cause, error.InnerException);
        var request = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(Assert.Single(trace.Contexts).Request);
        Assert.True(request.HasCommittedUnit); Assert.Equal(EffectOperationState.Committed, request.State.Entry.State);
        Assert.True(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.Equal(100000, f.Gold); Assert.Empty(f.Sender.Messages);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Real_disjoint_SQL_rollback_retries_the_same_entry_without_extending_ordinal(bool portable)
    {
        var trace = new Trace();
        using var f = EquipmentDisjointTests.Fixture(configure: services => Configure(services, trace));
        await f.SeedCore(9, StackableItemCore.Create(1, 3000, 1234) with { Durability = 45 });
        await f.Seed(65, 2600469, 2);
        var input = new DisjointItemRequest(new(1, 0x1A, 0, 7), new(InventoryContainerType.Main, 9), portable ? (short)65 : (short)-1, portable ? 0 : 1000);
        f.Sql("CREATE TRIGGER fail_whole_disjoint BEFORE INSERT ON item_slots WHEN NEW.id=1000 BEGIN SELECT RAISE(ABORT,'whole disjoint failure'); END");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => EffectRequestTransport.Send(f, input).AsTask());
        var first = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(trace.Contexts[0].Request);
        var entry = first.State.Entry; var ordinal = entry.Ordinal;
        Assert.Equal(EffectOperationState.ConfirmedRollback, entry.State); Assert.Null(entry.Projection);
        Assert.False(first.HasCommittedUnit); Assert.Equal((ushort)45, await f.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(2, f.Count(2600469)); Assert.Equal(0, f.Count(1000)); Assert.Empty(f.Sender.Messages);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        f.Sql("DROP TRIGGER fail_whole_disjoint");
        await EffectRequestTransport.Send(f, input);
        var retry = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(trace.Contexts[1].Request);
        Assert.Same(entry, retry.State.Entry); Assert.Equal(ordinal, entry.Ordinal); Assert.Equal(EffectOperationState.Committed, entry.State);
        Assert.Null(await f.Durability(InventoryContainerType.Main, 9)); Assert.Equal(portable ? 1 : 2, f.Count(2600469)); Assert.Equal(12, f.Count(1000));
        Assert.Single(f.Sender.Messages.OfType<DisjointItemResponse>());
        await EffectRequestTransport.Send(f, input);
        Assert.Equal(2, trace.Contexts.Count); Assert.Equal(portable ? 1 : 2, f.Count(2600469)); Assert.Equal(12, f.Count(1000));
        Assert.Single(f.Sender.Messages.OfType<DisjointItemResponse>()); Assert.Equal(ordinal, entry.Ordinal);
    }

    private static IEffectSettlementRequest Input(int protocol, bool early) => protocol switch
    {
        0 => new ReinforceItemRequest(new(1, 0x50, 0, 7), early ? (byte)2 : (byte)0, 9, 3000, 121, -1, []),
        1 => new DisjointItemRequest(new(1, 0x1A, 0, 7), new(InventoryContainerType.Main, 9), early ? (short)65 : (short)-1, 1000),
        2 => new EnchantByBeadRequest(new(1, 0x110, 0, 7), early ? InventoryContainerType.Pet : InventoryContainerType.Main, 121, InventoryContainerType.Equipment, 12),
        _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
    };
    private static ShopFixture World(out Trace trace)
    {
        var state = new Trace(); trace = state;
        return new ShopFixture(services => Configure(services, state));
    }
    private static void Configure(IServiceCollection services, Trace trace)
    {
        services.AddSingleton(trace);
        services.AddScoped<GameReinforceItemCommandHandler>();
        services.AddScoped<GameDisjointItemCommandHandler>();
        services.AddScoped<GameEnchantByBeadCommandHandler>();
        services.AddScoped<ICommandHandler<GameReinforceItemCommand, EquipmentReinforcementResult>, Observe<GameReinforceItemCommand, EquipmentReinforcementResult, GameReinforceItemCommandHandler>>();
        services.AddScoped<ICommandHandler<GameDisjointItemCommand, EquipmentDisjointResult>, Observe<GameDisjointItemCommand, EquipmentDisjointResult, GameDisjointItemCommandHandler>>();
        services.AddScoped<ICommandHandler<GameEnchantByBeadCommand, EquipmentEnchantResult>, Observe<GameEnchantByBeadCommand, EquipmentEnchantResult, GameEnchantByBeadCommandHandler>>();
    }

    private sealed class Trace { internal List<CommandExecutionContext> Contexts { get; } = []; internal Action? Before { get; set; } }
    private sealed class Observe<TCommand, TResult, THandler>(THandler actual, Trace trace) : ICommandHandler<TCommand, TResult>
        where TCommand : class, ICommand<TResult> where TResult : notnull where THandler : ICommandHandler<TCommand, TResult>
    {
        public async ValueTask<TResult> HandleAsync(CommandExecutionContext context, TCommand command, CancellationToken ct)
        { trace.Contexts.Add(context); trace.Before?.Invoke(); return await actual.HandleAsync(context, command, ct); }
    }
}
