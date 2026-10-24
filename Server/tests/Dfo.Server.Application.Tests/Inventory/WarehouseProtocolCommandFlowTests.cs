using Dfo.Server.Application.Sessions;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Tests.Support;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol.Game.Warehouse;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class WarehouseProtocolCommandFlowTests(ITestOutputHelper output)
{
    private static string Ddl(bool main, bool native) => native
        ? "CREATE TABLE warehouse_parent(id INTEGER PRIMARY KEY); CREATE TABLE warehouse_child(id INTEGER REFERENCES warehouse_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER warehouse_fault AFTER UPDATE OF " + (main ? "list_param16 ON character_container_state WHEN NEW.list_type=0" : "value32 ON account_cargo_state") + " BEGIN INSERT INTO warehouse_child VALUES(404); END;"
        : "CREATE TRIGGER warehouse_fault BEFORE UPDATE OF " + (main ? "list_param16 ON character_container_state WHEN NEW.list_type=0" : "value32 ON account_cargo_state") + " BEGIN SELECT RAISE(ABORT,'warehouse action SQL failure'); END;";

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Old_and_new_wire_keep_actual_action_SQL_or_native_Commit_assets_ack_silence_and_original_entry_retry(bool main, bool native)
    {
        var trace = new Trace(); using var f = Fixture(trace); await Seed(f, main); var before = await Assets(f); f.Sql(Ddl(main, native));
        var input = Input(main ? (ushort)0x01AF : (ushort)0x0133, main ? 65 : 100); var body = EffectRequestTransport.Body(input);
        await EffectRequestTransport.Send(f, input); var attempt = Assert.Single(trace.Attempts); Assert.Equal(1, attempt.ForeignKeys);
        var errors = trace.Errors.Where(x => x.Cause is SqliteException).ToArray(); Assert.Single(errors);
        Assert.EndsWith("GameWarehouseCommandHandler", errors[0].Category);
        var cause = Assert.IsType<SqliteException>(errors[0].Cause); Assert.All(errors, x => { Assert.Same(cause, x.Cause); Assert.Equal(LogLevel.Error, x.Level); });
        Assert.Equal(19, cause.SqliteErrorCode); Assert.Equal(native ? 787 : 1811, cause.SqliteExtendedErrorCode);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, attempt.Transaction.Completion);
        Assert.Equal(native, attempt.Receipt is not null); Assert.Equal(native ? 1 : 0, attempt.Children);
        Assert.Equal(native && !main ? 100 : 0, attempt.CargoGold); Assert.Equal(native && !main ? 99900 : 100000, attempt.Gold);
        Assert.Equal(main && native ? 8 : 0, attempt.MainParameter); Assert.Equal(main ? 1 : 0, attempt.Tickets);
        if (native) { Assert.Null(attempt.ActionFailure); Assert.Contains("SqliteTransaction.Commit", cause.StackTrace!, StringComparison.Ordinal); }
        else { Assert.Same(cause, attempt.ActionFailure); Assert.DoesNotContain("SqliteTransaction.Commit", cause.StackTrace!, StringComparison.Ordinal); }
        var entry = Entry(f); var ordinal = entry.Ordinal; Assert.Equal(EffectOperationState.ConfirmedRollback, entry.State); Assert.Equal(native, entry.Projection is not null);
        Assert.Equal(input.Metadata.Type, entry.Type); Assert.Equal(input.Metadata.Sequence, entry.Sequence); Assert.Equal(body.Length, entry.Length); Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)), entry.Hash, ignoreCase: true);
        Assert.Equal(before, await Assets(f)); Assert.Empty(f.Sender.Batches);
        if (main) { Assert.Empty(f.Sender.Messages); Assert.Empty(f.Sender.Outbound.Frames); }
        else { Assert.Single(f.Sender.Messages); Assert.Equal(new byte[] { 0, 10 }, Assert.Single(f.Sender.Outbound.Frames)[15..]); }
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Empty(f.Sender.Outbound.Closes);
        var failure = new { warehouseOracle = true, main, native, ddl = Ddl(main, native), body = Convert.ToHexString(body), entry.Hash, entry.Length, entry.Selector, entry.Type, entry.Sequence, ordinal,
            ack = f.Sender.Outbound.Frames.Select(Convert.ToHexString).ToArray(), cause.SqliteErrorCode, cause.SqliteExtendedErrorCode, cause.StackTrace, attempt.ForeignKeys,
            receipt = Receipt(attempt.Receipt), attempt.Gold, attempt.CargoGold, attempt.MainParameter, attempt.Tickets, attempt.Children, attempt.Items, attempt.Slots,
            completion = attempt.Transaction.Completion.ToString(), state = entry.State.ToString(), projection = entry.Projection is not null, before,
            logs = errors.Select(x => new { x.Category, level = x.Level.ToString() }), sameCause = errors.All(x => ReferenceEquals(cause, x.Cause)), sourceCloses = f.Sender.Outbound.Closes.Count };
        f.Sql("DROP TRIGGER warehouse_fault"); await EffectRequestTransport.Send(f, input); Assert.Same(entry, Entry(f)); Assert.Equal(ordinal, entry.Ordinal); Assert.Equal(EffectOperationState.Committed, entry.State);
        Assert.Equal(main ? 1 : 0, f.Count(2660296)); Assert.Equal(main ? 8 : 0, MainParameter(f)); Assert.Equal(main ? 100000 : 99900, f.Gold); Assert.Equal(main ? 0 : 100, CargoGold(f));
        Assert.Equal(main ? 1 : 3, f.Sender.Batches.Last().Frames.Length); Assert.Equal(main ? 0 : 1, f.Sender.Batches.Last().Frames[0][0]);
        var committed = await Assets(f); var receipt = trace.Attempts[1].Receipt!; Assert.Equal(f.Clock.GetUtcNow(), receipt.At); var acks = f.Sender.Messages.OfType<WarehouseResponse>().Count();
        var firstFrames = f.Sender.Batches.Last().Frames.Select(Convert.ToHexString).ToArray();
        await EffectRequestTransport.Send(f, input); Assert.Equal(2, trace.Attempts.Count); Assert.Equal(committed, await Assets(f)); Assert.Equal(acks, f.Sender.Messages.OfType<WarehouseResponse>().Count());
        Assert.DoesNotContain(f.Sender.Batches.Last().Frames, frame => frame[0] == 1); Assert.Equal(ordinal, entry.Ordinal); Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Empty(f.Sender.Outbound.Closes);
        output.WriteLine(JsonSerializer.Serialize(new { failure, retryReceipt = Receipt(receipt), committed, firstFrames, replayFrames = f.Sender.Batches.Last().Frames.Select(Convert.ToHexString).ToArray(), sameEntry = true, originalOrdinal = entry.Ordinal }));
    }

    [Theory]
    [InlineData(0x0131)] [InlineData(0x0132)] [InlineData(0x0133)] [InlineData(0x0134)] [InlineData(0x0198)] [InlineData(0x01AF)]
    public async Task Six_types_commit_one_Main_original_receipt_and_exact_ACK_Main_cargo_order(int rawType)
    {
        var type = (ushort)rawType; var trace = new Trace(); using var f = Fixture(trace); await PrepareType(f, type, true);
        var beforeGold = f.Gold; var beforeTicket = f.Count(type == 0x0198 ? 50 : 2660296);
        await EffectRequestTransport.Send(f, Input(type, type is 0x0198 or 0x01AF ? 65 : 100));
        var result = Assert.Single(trace.Results); Assert.Equal(InventoryExpansionRejection.None, result.Rejection); var receipt = result.Receipt!; Assert.NotNull(receipt);
        var selected = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(Assert.Single(trace.Contexts).Request);
        Assert.True(selected.HasCommittedUnit); Assert.Equal(EffectOperationState.Committed, Entry(f).State); Assert.Equal(ReplayProjectionNeeds.Inventory, Entry(f).Projection!.Needs); Assert.Empty(Entry(f).Projection!.TouchedSlots);
        Assert.Single(trace.Attempts); Assert.Same(receipt, trace.Attempts[0].Receipt); Assert.Equal(DomainTransactionCompletion.Committed, trace.Attempts[0].Transaction.Completion); Assert.Equal(f.Clock.GetUtcNow(), receipt.At);
        var batch = Assert.Single(f.Sender.Batches); Assert.Equal(type == 0x01AF ? 1 : 3, batch.Frames.Length);
        Assert.Equal(type == 0x01AF ? 0 : 1, batch.Frames[0][0]); Assert.Equal((byte)0, batch.Frames[type == 0x01AF ? 0 : 1][15]);
        if (type != 0x01AF)
        {
            var ack = Assert.IsType<WarehouseResponse>(batch.Messages[0]); Assert.True(ack.Success); Assert.Equal(type, ack.Type); Assert.Equal(receipt.Account.Cargo.Value32, ack.Gold);
            Assert.Equal(type == 0x0198 ? (byte)2 : (byte)12, batch.Frames[2][15]);
        }
        else Assert.Empty(f.Sender.Messages.OfType<WarehouseResponse>());
        Assert.Equal(type switch { 0x0131 => beforeGold - 100000, 0x0132 => beforeGold - 2000000, 0x0133 => beforeGold - 100, 0x0134 => beforeGold + 100, _ => beforeGold }, f.Gold);
        if (type is 0x0198 or 0x01AF) Assert.Equal(beforeTicket - 1, f.Count(type == 0x0198 ? 50 : 2660296));
        Assert.Equal(type == 0x01AF ? 8 : 0, MainParameter(f));
        var first = await Assets(f); var ackCount = f.Sender.Messages.OfType<WarehouseResponse>().Count();
        await EffectRequestTransport.Send(f, Input(type, type is 0x0198 or 0x01AF ? 65 : 100)); Assert.Single(trace.Attempts); Assert.Equal(first, await Assets(f)); Assert.Equal(ackCount, f.Sender.Messages.OfType<WarehouseResponse>().Count());
    }

    [Theory]
    [InlineData(0x0131, 22)] [InlineData(0x0132, 22)] [InlineData(0x0133, 10)] [InlineData(0x0134, 10)] [InlineData(0x0198, 19)] [InlineData(0x01AF, 0)]
    public async Task Six_normal_domain_refusals_still_commit_minimum_Main_without_assets(int rawType, int error)
    {
        var type = (ushort)rawType; var trace = new Trace(); using var f = Fixture(trace); await PrepareType(f, type, false); var before = await Assets(f);
        await EffectRequestTransport.Send(f, Input(type, type is 0x0198 or 0x01AF ? 65 : -100)); Assert.Equal(before, await Assets(f));
        Assert.Null(Assert.Single(trace.Results).Receipt); Assert.Single(trace.Attempts); Assert.Equal(DomainTransactionCompletion.Committed, trace.Attempts[0].Transaction.Completion);
        Assert.Equal(EffectOperationState.Committed, Entry(f).State); Assert.Equal(ReplayProjectionNeeds.Inventory, Entry(f).Projection!.Needs);
        if (type == 0x01AF) { Assert.Empty(f.Sender.Messages); Assert.Empty(f.Sender.Outbound.Frames); }
        else Assert.Equal(new byte[] { 0, (byte)error }, Assert.Single(f.Sender.Outbound.Frames)[15..]);
    }

    [Theory]
    [InlineData(true, 95)] [InlineData(false, 94)]
    public async Task Original_gold_limit_codes_remain_distinct(bool deposit, int code)
    {
        var trace = new Trace(); using var f = Fixture(trace); await Seed(f, false);
        if (deposit) f.Sql("UPDATE account_cargo_state SET value32=10000000");
        else f.Sql("UPDATE account_cargo_state SET value32=100; UPDATE character_stats SET carried_gold=400000000");
        var before = await Assets(f); await EffectRequestTransport.Send(f, Input(deposit ? (ushort)0x0133 : (ushort)0x0134, 1)); Assert.Equal(before, await Assets(f));
        Assert.Equal(InventoryExpansionRejection.GoldLimit, Assert.Single(trace.Results).Rejection); Assert.Equal(new byte[] { 0, (byte)code }, Assert.Single(f.Sender.Outbound.Frames)[15..]);
    }

    [Fact]
    public async Task Signed_negative_deposit_is_refused_without_gold_movement_and_positive_controls_work()
    {
        var trace = new Trace(); using var f = Fixture(trace); await Seed(f, false); var before = await Assets(f);
        await EffectRequestTransport.Send(f, Input(value: -100)); var after = await Assets(f);
        output.WriteLine(JsonSerializer.Serialize(new { warehouseAmountMutation = true, before, actual = after, frames = f.Sender.Outbound.Frames.Select(Convert.ToHexString).ToArray() }));
        Assert.Equal(before, after); Assert.Equal(InventoryExpansionRejection.InvalidRequest, Assert.Single(trace.Results).Rejection); Assert.Equal(new byte[] { 0, 10 }, Assert.Single(f.Sender.Outbound.Frames)[15..]); Assert.Equal(EffectOperationState.Committed, Entry(f).State);
    }

    [Theory]
    [InlineData("mapping")] [InlineData("none")]
    public async Task Early_mapping_or_no_actor_rejection_never_claims_Main(string kind)
    {
        var trace = new Trace(); using var f = Fixture(trace); await Seed(f, true); var before = await Assets(f);
        if (kind == "none") TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(f.Session));
        await EffectRequestTransport.Send(f, Input(0x0198, ushort.MaxValue)); Assert.Empty(trace.Attempts); Assert.Null(Assert.Single(trace.Results).Receipt); Assert.Equal(before, await Assets(f));
        Assert.False(trace.Contexts[0].Request!.HasCommittedUnit); Assert.Equal(new byte[] { 0, 19 }, Assert.Single(f.Sender.Outbound.Frames)[15..]);
        if (kind == "mapping") { Assert.Equal(EffectOperationState.Rejected, Entry(f).State); Assert.Null(Entry(f).Projection); }
    }

    [Fact]
    public async Task Initial_Rejected_cannot_upgrade_after_selection_and_next_fresh_succeeds()
    {
        var trace = new Trace(); using var f = Fixture(trace); await Seed(f, false);
        TestGameplay.Run(f.Services, () => { f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(f.Session); f.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(f.Session.CharacterId); });
        trace.Before = () => { f.Select(1); WarehouseStorageTests.Bind(f); }; await EffectRequestTransport.Send(f, Input());
        Assert.IsType<CommandRequestContext.ProtocolEffectRejected>(trace.Contexts[0].Request); Assert.Empty(trace.Attempts); Assert.Equal(0, CargoGold(f));
        trace.Before = null; await EffectRequestTransport.Send(f, Input(sequence: 8)); Assert.Single(trace.Attempts); Assert.Equal(100, CargoGold(f)); Assert.Equal(99900, f.Gold);
    }

    [Theory]
    [InlineData("tower")] [InlineData("owner")] [InlineData("world")] [InlineData("dungeon")]
    public async Task Current_domain_restrictions_stay_inside_Main(string restriction)
    {
        var trace = new Trace { NotInWorld = restriction == "world" }; var tower = TowerPresenceProxy.Create(out var presence); using var f = WarehouseStorageTests.Fixture(s => { Configure(s, trace); s.AddSingleton(tower); }); await Seed(f, false);
        if (restriction == "tower") presence.Members.Add(f.Session.CharacterId);
        if (restriction == "owner") f.Sql("UPDATE characters SET account_id=99 WHERE character_id=1");
        if (restriction == "dungeon") f.InTransaction(() =>
        {
            var cell = new MazeCell(0, 0);
            return f.Services.GetRequiredService<IDungeonRunDomainService>().Begin(f.Session.CharacterId,
                new DungeonLayout(144, 0, 1, 1, cell, cell, 55611, 0, new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(55611) }), difficulty: 0);
        });
        await EffectRequestTransport.Send(f, Input()); Assert.Single(trace.Attempts); Assert.Equal(InventoryExpansionRejection.NoSession, Assert.Single(trace.Results).Rejection);
        Assert.Equal(EffectOperationState.Committed, Entry(f).State); Assert.True(trace.Contexts[0].Request!.HasCommittedUnit); Assert.Equal(0, CargoGold(f)); Assert.Equal(100000, f.Gold);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)] [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)]
    public async Task Named_domain_and_clock_errors_map_after_rollback_with_original_log_classification(int kind, bool clock)
    {
        var cause = Cause(kind); var trace = new Trace { Injected = clock ? null : cause, ClockFailure = clock ? cause : null }; using var f = Fixture(trace); await Seed(f, false); var before = await Assets(f);
        await EffectRequestTransport.Send(f, Input()); Assert.Equal(before, await Assets(f)); Assert.Equal(EffectOperationState.ConfirmedRollback, Entry(f).State);
        Assert.Equal(kind == 0 ? InventoryExpansionRejection.InvalidRequest : kind == 1 ? InventoryExpansionRejection.GoldLimit : InventoryExpansionRejection.StateChanged, Assert.Single(trace.Results).Rejection);
        Assert.Equal(new byte[] { 0, kind == 1 ? (byte)95 : (byte)10 }, Assert.Single(f.Sender.Outbound.Frames)[15..]);
        var errors = trace.Errors.Where(x => ReferenceEquals(x.Cause, cause) && x.Level == LogLevel.Error).ToArray(); if (kind < 2) Assert.Empty(errors); else { Assert.Single(errors); Assert.EndsWith("GameWarehouseCommandHandler", errors[0].Category); }
        Assert.Contains(trace.Info, x => x.Contains("refused", StringComparison.Ordinal)); Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Empty(f.Sender.Outbound.Closes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Cancellation_and_typed_uncertainty_are_not_mapped_to_normal_refusal(bool typed)
    {
        Exception cause = typed ? new TransactionOutcomeUncertainException(TransactionUncertaintyReason.PostCommitPublicationFailure, "injected typed", new IOException("inner")) : new OperationCanceledException("injected cancel");
        var trace = new Trace { Injected = cause }; using var f = Fixture(trace); await Seed(f, false); var before = await Assets(f);
        var thrown = await Record.ExceptionAsync(() => EffectRequestTransport.Send(f, Input()).AsTask()); Assert.NotNull(thrown); Assert.True(Contains(thrown, cause)); Assert.Empty(trace.Results); Assert.Empty(f.Sender.Messages); Assert.Equal(before, await Assets(f)); Assert.NotEmpty(f.Sender.Outbound.Closes);
    }

    [Fact]
    public async Task Notification_Failed_is_isolated_then_replay_uses_changed_current_assets_without_ACK()
    {
        var trace = new Trace(); using var f = Fixture(trace); await Seed(f, false); f.Sender.FailBatch = true; await EffectRequestTransport.Send(f, Input());
        Assert.Equal(EffectOperationState.Committed, Entry(f).State); Assert.Equal(100, CargoGold(f)); Assert.Equal(99900, f.Gold); Assert.Empty(f.Sender.Messages); Assert.Empty(f.Sender.Outbound.Closes); Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        f.Sql("UPDATE account_cargo_state SET value32=333; UPDATE character_stats SET carried_gold=77777"); await f.Seed(65, 2000, 9); f.Sender.FailBatch = false; await EffectRequestTransport.Send(f, Input());
        Assert.Single(trace.Attempts); Assert.Equal(333, CargoGold(f)); Assert.Equal(77777, f.Gold); Assert.Equal(9, EffectRequestTransport.MainValue(f, 65)); Assert.Empty(f.Sender.Messages.OfType<WarehouseResponse>()); Assert.DoesNotContain(f.Sender.Batches.Last().Frames, x => x[0] == 1);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)] [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)]
    public async Task Direct_ACK_encoder_errors_including_mapping_are_outside_business_catch_and_keep_default_protection(int stage, bool mapping)
    {
        var trace = new Trace(); using var f = Fixture(trace); await Seed(f, false); if (stage == 2) f.Sql(Ddl(false, false)); var before = await Assets(f);
        Exception cause = mapping ? Cause(0) : new IOException("warehouse direct ACK encoder"); f.Sender.FailSends(() => cause);
        async Task Send() => await EffectRequestTransport.Send(f, stage == 0 ? Input(0x0198, ushort.MaxValue) : Input(value: stage == 1 ? -100 : 100));
        var thrown = await Record.ExceptionAsync(Send); Assert.NotNull(thrown);
        if (stage == 1) { Assert.Same(cause, Assert.IsType<TransactionOutcomeUncertainException>(thrown).InnerException); Assert.True(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); }
        else { Assert.Same(cause, thrown); Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); }
        Assert.Equal(stage == 0 ? EffectOperationState.Rejected : stage == 1 ? EffectOperationState.Committed : EffectOperationState.ConfirmedRollback, Entry(f).State);
        Assert.Equal(before, await Assets(f)); Assert.Empty(f.Sender.Messages); Assert.Equal(stage == 1 ? 2 : 1, f.Sender.Outbound.Closes.Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_action_SQL_E_and_independent_rollback_X_preserve_both_occurrences_and_block(bool sameObject)
    {
        var trace = new Trace { RollbackFault = true, SameObject = sameObject }; using var f = Fixture(trace); await Seed(f, false); var before = await Assets(f); f.Sql(Ddl(false, false));
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => EffectRequestTransport.Send(f, Input()).AsTask()); var original = Assert.Single(trace.Attempts).ActionFailure;
        Assert.Equal(1811, Assert.IsType<SqliteException>(original).SqliteExtendedErrorCode); var causes = Assert.IsType<AggregateException>(failure.InnerException).InnerExceptions;
        Assert.Equal(2, causes.Count); Assert.Same(original, causes[0]); Assert.Same(sameObject ? original : trace.CleanupFailure, causes[1]); Assert.Equal(before, await Assets(f)); Assert.Empty(f.Sender.Messages); Assert.Equal(EffectOperationState.Blocked, Entry(f).State); Assert.True(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Equal(2, f.Sender.Outbound.Closes.Count);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Named_exception_logging_observation_distinguishes_original_pipeline_from_whole_handling(int kind)
    {
        var cause = Cause(kind); var trace = new Trace { Injected = cause }; using var f = Fixture(trace); await Seed(f, false); var before = await Assets(f);
        var whole = f.Services.GetServices<CommandHandlerRegistration>().Any(x => x.CommandType == typeof(GameWarehouseCommand));
        await EffectRequestTransport.Send(f, Input()); Assert.Equal(before, await Assets(f)); Assert.Equal(EffectOperationState.ConfirmedRollback, Entry(f).State);
        var errors = trace.Errors.Where(x => ReferenceEquals(x.Cause, cause) && x.Level == LogLevel.Error).ToArray();
        Assert.Equal((kind == 2 ? 1 : 0) + (whole ? 0 : 1), errors.Length);
        Assert.All(errors, x => Assert.Same(cause, x.Cause)); Assert.Contains(trace.Info, x => x.Contains("refused", StringComparison.Ordinal));
        output.WriteLine(JsonSerializer.Serialize(new { warehouseNamedLogs = true, reconstructedOldRoute = !whole, kind, errorCount = errors.Length, sameCause = true, logs = errors.Select(x => new { x.Category, level = x.Level.ToString() }), infos = trace.Info, ack = f.Sender.Outbound.Frames.Select(Convert.ToHexString).ToArray() }));
    }

    private static Exception Cause(int kind) => kind switch { 0 => new InventorySlotMappingException(new GameSlot(InventoryContainerType.Main, ushort.MaxValue)), 1 => new InventoryExpansionException(InventoryExpansionRejection.GoldLimit), _ => new IOException("warehouse ordinary action") };
    private static bool Contains(Exception value, Exception cause) => ReferenceEquals(value, cause) || value is AggregateException a && a.InnerExceptions.Any(x => Contains(x, cause)) || value.InnerException is { } inner && Contains(inner, cause);
    private static async Task PrepareType(ShopFixture f, ushort type, bool success)
    {
        f.SetLevel(60); f.Sql("UPDATE character_stats SET carried_gold=" + (success ? 3000000 : 0));
        if (type != 0x0131) f.Sql("INSERT INTO account_cargo_state(account_id,selection_key,value32) VALUES(1,1,1000)");
        if (type is 0x0198 or 0x01AF && success) await f.Seed(65, type == 0x0198 ? 50 : 2660296, 2);
    }

    private static object? Receipt(InventoryExpansionReceipt? r) => r is null ? null : new { r.At, resources = r.Character.VirtualResources, cargo = r.Account.Cargo.Value32, containers = r.Character.Containers.Select(x => new { x.ContainerType, x.Parameter }), accountParameter = r.Account.Cargo.Container.Parameter };
    private static ShopFixture Fixture(Trace trace) => WarehouseStorageTests.Fixture(s => Configure(s, trace));
    private static async Task Seed(ShopFixture f, bool main)
    {
        f.Sql("INSERT INTO account_cargo_state(account_id,selection_key,value32) VALUES(1,1,0)");
        if (!main) return;
        var grant = await f.Services.GetRequiredService<IInventoryExpansionRuleService>().ItemAsync(2660296, default); Assert.Equal(new InventoryExpansionGrant(InventoryExpansionKind.Main, 8), grant);
        var placement = await f.Services.GetRequiredService<IItemPlacementRuleService>().PlacementForAsync(2660296, default); Assert.Equal(BusinessInventorySpace.Consumable, placement!.Destination.Space);
        Assert.Equal(0, MainParameter(f)); await f.Seed(65, 2660296, 2); Assert.Equal(2, f.Count(2660296));
    }
    private static WarehouseRequest Input(ushort type = 0x0133, int value = 100, ushort sequence = 7) => new(new(1, type, 0, sequence), value);
    private static int MainParameter(ShopFixture f) => f.Scalar("SELECT COALESCE((SELECT list_param16 FROM character_container_state WHERE character_id=1 AND list_type=0),0)");
    private static int CargoGold(ShopFixture f) => f.Scalar("SELECT value32 FROM account_cargo_state WHERE account_id=1");
    private sealed record AssetState(string Items, string Slots, string Containers, string Account, int Gold, int Tickets, int Events, int Deliveries, string Cooldowns, string Effects);
    private static Task<AssetState> Assets(ShopFixture f) => TestGameplay.RunAsync(f.Services, async () => new AssetState(
        await Rows(f.Services, "SELECT item_uid,hex(item_core) AS item_core,cargo_character_id,created_at,updated_at,avatar_expire_date,avatar_clear_id,avatar_jewel_socket,avatar_color1,avatar_color2,avatar_delete_date,creature_field04,creature_mode_flag,creature_progress_value,creature_mode1_field0a,creature_mode1_field0b,creature_field_after_value,creature_text,creature_tail_flag,is_mirror,mirror_source_uid,appearance_item_id FROM items ORDER BY item_uid", default), await Rows(f.Services, "SELECT * FROM item_slots ORDER BY owner_scope,owner_id,list_type,slot_index", default),
        await Rows(f.Services, "SELECT * FROM character_container_state ORDER BY character_id,list_type", default), await Rows(f.Services, "SELECT account_id,selection_key,value32 FROM account_cargo_state ORDER BY account_id", default), f.Gold, f.Count(2660296),
        f.Scalar("SELECT count(*) FROM domain_events"), f.Scalar("SELECT count(*) FROM domain_event_deliveries"), await Rows(f.Services, "SELECT * FROM character_item_cooldowns ORDER BY sort_order", default), await Rows(f.Services, "SELECT * FROM character_item_effects ORDER BY sort_order", default)));
    private static ValueTask<string> Rows(IServiceProvider services, string sql, CancellationToken ct) => services.GetRequiredService<SqliteDatabase>().ReadAsync(async (cmd, token) =>
    {
        cmd.CommandText = sql; await using var reader = await cmd.ExecuteReaderAsync(token); var rows = new List<string>();
        while (await reader.ReadAsync(token)) rows.Add(JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i) is byte[] blob ? Convert.ToHexString(blob) : Convert.ToString(reader.GetValue(i))).ToArray())); return string.Join("\n", rows);
    }, ct);
    private static ValueTask<int> Scalar(IServiceProvider services, string sql, CancellationToken ct) => services.GetRequiredService<SqliteDatabase>().ReadAsync(async (cmd, token) => { cmd.CommandText = sql; return Convert.ToInt32(await cmd.ExecuteScalarAsync(token)); }, ct);
    private static EffectOperationEntry Entry(ShopFixture f)
    {
        var ledger = f.Services.GetRequiredService<EffectOperationLedger>(); var table = typeof(EffectOperationLedger).GetField("ledgers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ledger)!; object?[] args = [f.Connection, null]; Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!);
        var entries = (IDictionary)args[1]!.GetType().GetProperty("Entries")!.GetValue(args[1])!; return Assert.Single(entries.Values.Cast<EffectOperationEntry>());
    }
    private static void Configure(IServiceCollection services, Trace trace)
    {
        var originalClock = (TimeProvider)services.Last(x => x.ServiceType == typeof(TimeProvider)).ImplementationInstance!;
        services.AddSingleton<TimeProvider>(p => new FaultClock(originalClock, p.GetRequiredService<IDomainTransactionAccessor>(), trace));
        services.AddScoped<GameWarehouseCommandHandler>(); services.AddScoped<ICommandHandler<GameWarehouseCommand, InventoryExpansionResult>, ObserveWhole>();
        services.AddSingleton<ISelectedSessionDomainService>(p => PresenceProxy.Create(p.GetRequiredService<SelectedSessionDomainService>(), trace));
        services.AddSingleton(trace); services.AddSingleton<ILoggerProvider>(trace); services.AddSingleton<InventoryExpansionDomainService>();
        services.AddSingleton<IInventoryExpansionDomainService>(p => new Observe(p.GetRequiredService<InventoryExpansionDomainService>(), p, trace));
    }
    private sealed class Attempt(DomainTransaction transaction)
    {
        internal DomainTransaction Transaction { get; } = transaction; internal int ForeignKeys, Gold, CargoGold, MainParameter, Tickets, Children; internal string Items = "", Slots = ""; internal InventoryExpansionReceipt? Receipt; internal Exception? ActionFailure;
    }
    private sealed class Observe(IInventoryExpansionDomainService actual, IServiceProvider services, Trace trace) : IInventoryExpansionDomainService
    {
        private async ValueTask<InventoryExpansionResult> Run(SelectedSessionIdentity session, Func<ValueTask<InventoryExpansionResult>> action, CancellationToken ct)
        {
            var attempt = new Attempt((DomainTransaction)services.GetRequiredService<IDomainTransactionAccessor>().Current!); trace.Attempts.Add(attempt); if (trace.RollbackFault) attempt.Transaction.Enlist(trace, () => new RollbackError(trace, attempt)); attempt.ForeignKeys = await Scalar(services, "PRAGMA foreign_keys", ct);
            try { if (trace.Injected is { } injected) throw injected; var result = await action(); attempt.Receipt = result.Receipt; return result; }
            catch (Exception cause) { attempt.ActionFailure = cause; throw; }
            finally
            {
                attempt.Gold = await Scalar(services, "SELECT carried_gold FROM character_stats WHERE character_id=1", ct); attempt.CargoGold = await Scalar(services, "SELECT value32 FROM account_cargo_state WHERE account_id=1", ct);
                attempt.MainParameter = await Scalar(services, "SELECT COALESCE((SELECT list_param16 FROM character_container_state WHERE character_id=1 AND list_type=0),0)", ct);
                attempt.Tickets = await Scalar(services, "SELECT COALESCE(SUM(count),0) FROM item_slots WHERE list_type=0 AND slot_index=65", ct);
                attempt.Items = await Rows(services, "SELECT item_uid,hex(item_core) AS item_core,cargo_character_id,created_at,updated_at,avatar_expire_date,avatar_clear_id,avatar_jewel_socket,avatar_color1,avatar_color2,avatar_delete_date,creature_field04,creature_mode_flag,creature_progress_value,creature_mode1_field0a,creature_mode1_field0b,creature_field_after_value,creature_text,creature_tail_flag,is_mirror,mirror_source_uid,appearance_item_id FROM items ORDER BY item_uid", ct); attempt.Slots = await Rows(services, "SELECT * FROM item_slots ORDER BY owner_scope,owner_id,list_type,slot_index", ct);
                if (await Scalar(services, "SELECT count(*) FROM sqlite_master WHERE name='warehouse_child'", ct) != 0) attempt.Children = await Scalar(services, "SELECT count(*) FROM warehouse_child", ct);
            }
        }
        public ValueTask<InventoryExpansionResult> UseItemAsync(SelectedSessionIdentity s, BusinessSlot slot, InventoryExpansionKind kind, DateTimeOffset at, CancellationToken ct) => Run(s, () => actual.UseItemAsync(s, slot, kind, at, ct), ct);
        public ValueTask<InventoryExpansionResult> UpgradeAccountAsync(SelectedSessionIdentity s, bool create, DateTimeOffset at, CancellationToken ct) => Run(s, () => actual.UpgradeAccountAsync(s, create, at, ct), ct);
        public ValueTask<InventoryExpansionResult> TransferGoldAsync(SelectedSessionIdentity s, bool deposit, int amount, DateTimeOffset at, CancellationToken ct) => Run(s, () => actual.TransferGoldAsync(s, deposit, amount, at, ct), ct);
        public ValueTask<IReadOnlyList<InventoryExpansionChange>?> PlanPurchaseAsync(SelectedSessionIdentity s, IReadOnlyList<InventoryExpansionGrant> grants, CancellationToken ct) => actual.PlanPurchaseAsync(s, grants, ct);
        public ValueTask ApplyPurchaseAsync(SelectedSessionIdentity s, IReadOnlyList<InventoryExpansionChange> changes, CancellationToken ct) => actual.ApplyPurchaseAsync(s, changes, ct);
        public ValueTask<InventoryExpansionReceipt> SnapshotAsync(SelectedSessionIdentity s, DateTimeOffset at, CancellationToken ct) => actual.SnapshotAsync(s, at, ct);
    }
    // Only the world=false case uses a controlled query; the original registry remains selected/in-world.
    // All authority/current methods forward to that same production singleton. This is not a natural registry transition.
    public class PresenceProxy : DispatchProxy
    {
        private ISelectedSessionDomainService actual = null!; private Trace trace = null!;
        private static ISelectedSessionDomainService Build(ISelectedSessionDomainService actual, Trace trace)
        { var result = DispatchProxy.Create<ISelectedSessionDomainService, PresenceProxy>(); var proxy = (PresenceProxy)(object)result; proxy.actual = actual; proxy.trace = trace; return result; }
        internal static ISelectedSessionDomainService Create(ISelectedSessionDomainService actual, object trace) => Build(actual, (Trace)trace);
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (trace.NotInWorld && method!.Name == nameof(ISelectedSessionDomainService.IsInWorld)) return false;
            try { return method!.Invoke(actual, args); }
            catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error.InnerException!); throw; }
        }
    }
    private sealed class FaultClock(TimeProvider actual, IDomainTransactionAccessor transactions, Trace trace) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() { if (transactions.Current is not null && trace.ClockFailure is { } failure) throw failure; return actual.GetUtcNow(); }
    }
    private sealed class ObserveWhole(GameWarehouseCommandHandler actual, Trace trace) : ICommandHandler<GameWarehouseCommand, InventoryExpansionResult>
    {
        public async ValueTask<InventoryExpansionResult> HandleAsync(CommandExecutionContext context, GameWarehouseCommand command, CancellationToken ct)
        { trace.Contexts.Add(context); trace.Before?.Invoke(); var result = await actual.HandleAsync(context, command, ct); trace.Results.Add(result); return result; }
    }
    private sealed class RollbackError(Trace trace, Attempt attempt) : ITransactionParticipant
    {
        public bool CommitCanFail => false; public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask; public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => throw (trace.SameObject ? attempt.ActionFailure! : trace.CleanupFailure);
    }
    private sealed class Trace : ILoggerProvider
    {
        internal Action? Before; internal Exception? Injected, ClockFailure; internal bool RollbackFault, SameObject, NotInWorld; internal IOException CleanupFailure { get; } = new("independent warehouse rollback");
        internal List<CommandExecutionContext> Contexts { get; } = []; internal List<InventoryExpansionResult> Results { get; } = []; internal ConcurrentQueue<string> Info { get; } = new();
        internal List<Attempt> Attempts { get; } = []; internal ConcurrentQueue<(string Category, LogLevel Level, Exception Cause)> Errors { get; } = new();
        public ILogger CreateLogger(string name) => new Logger(this, name); public void Dispose() { }
        private sealed class Logger(Trace trace, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null; public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? cause, Func<TState, Exception?, string> formatter) { if (cause is not null) trace.Errors.Enqueue((category, level, cause)); if (level == LogLevel.Information) trace.Info.Enqueue(formatter(state, cause)); }
        }
    }
}
