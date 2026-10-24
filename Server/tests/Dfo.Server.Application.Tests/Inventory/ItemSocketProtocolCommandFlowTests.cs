using Dfo.Server.Abstractions.Sessions;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Collections.Immutable;
using System.Text.Json;
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
using Dfo.Server.Protocol.Game.ItemSockets;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class ItemSocketProtocolCommandFlowTests(ITestOutputHelper output)
{
    private const string ActionDdl = "CREATE TRIGGER socket_fault BEFORE UPDATE ON items BEGIN SELECT RAISE(ABORT,'socket action SQL failure'); END;";
    private const string NativeDdl = "CREATE TABLE socket_parent(id INTEGER PRIMARY KEY); CREATE TABLE socket_child(id INTEGER REFERENCES socket_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER socket_fault AFTER UPDATE ON items WHEN substr(hex(NEW.item_core),3,8)='B90B0000' BEGIN INSERT INTO socket_child VALUES(404); END;";

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Old_and_new_socket_wire_keep_action_SQL_or_native_Commit_failure_ack_and_original_entry_retry(bool nativeCommit)
    {
        var trace = new Trace(); using var f = Fixture(trace); var protocol = nativeCommit ? 1 : 2; await Seed(f, protocol);
        var before = await Assets(f, protocol); f.Sql(nativeCommit ? NativeDdl : ActionDdl);
        var input = Input(protocol); var body = EffectRequestTransport.Body(input); await EffectRequestTransport.Send(f, input);
        var attempt = Assert.Single(trace.Attempts); Assert.Equal(1, attempt.ForeignKeys);
        var errors = trace.Errors.Where(x => x.Cause is SqliteException).ToArray(); Assert.NotEmpty(errors); var cause = Assert.IsType<SqliteException>(errors[0].Cause); Assert.All(errors, x => Assert.Same(cause, x.Cause));
        Assert.Contains(errors, x => x.Level == LogLevel.Error && x.Category.EndsWith("GameItemSocketCommandHandler", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, x => x.Category.Contains("CommandLoggingBehavior", StringComparison.Ordinal));
        Assert.Equal(19, cause.SqliteErrorCode); Assert.Equal(nativeCommit ? 787 : 1811, cause.SqliteExtendedErrorCode);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, attempt.Transaction.Completion);
        if (nativeCommit) { Assert.NotNull(attempt.Receipt); Assert.Null(attempt.ActionFailure); Assert.True(attempt.Children > 0); Assert.Equal(1, attempt.Material); Assert.NotEqual(before.Socket, attempt.Socket); Assert.Contains("SqliteTransaction.Commit", cause.StackTrace!, StringComparison.Ordinal); }
        else { Assert.Null(attempt.Receipt); Assert.Same(cause, attempt.ActionFailure); Assert.DoesNotContain("SqliteTransaction.Commit", cause.StackTrace!, StringComparison.Ordinal); }
        var entry = Entry(f); var ordinal = entry.Ordinal; Assert.Equal(EffectOperationState.ConfirmedRollback, entry.State); Assert.Equal(nativeCommit, entry.Projection is not null);
        Assert.Equal(input.Metadata.Type, entry.Type); Assert.Equal(input.Metadata.Sequence, entry.Sequence); Assert.Equal(body.Length, entry.Length); Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)), entry.Hash, ignoreCase: true);
        Assert.Equal(before, await Assets(f, protocol)); Assert.Empty(f.Sender.Batches); Assert.Single(f.Sender.Messages); Assert.Equal(new byte[] { 0, 4 }, Assert.Single(f.Sender.Outbound.Frames)[15..]);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Empty(f.Sender.Outbound.Closes);
        var failure = new { socketOracle = true, nativeCommit, ddl = nativeCommit ? NativeDdl : ActionDdl, body = Convert.ToHexString(body), entry.Hash, entry.Length, entry.Selector, entry.Type, entry.Sequence, ordinal,
            ack = Convert.ToHexString(f.Sender.Outbound.Frames[0]), cause.SqliteErrorCode, cause.SqliteExtendedErrorCode, cause.StackTrace, attempt.ForeignKeys,
            actionReturnedReceipt = attempt.Receipt is not null, attempt.Material, attempt.Children, attempt.Socket, completion = attempt.Transaction.Completion.ToString(), state = entry.State.ToString(), projection = entry.Projection is not null, before,
            logs = errors.Select(x => new { x.Category, level = x.Level.ToString() }), sameCause = errors.All(x => ReferenceEquals(cause, x.Cause)), sourceCloses = f.Sender.Outbound.Closes.Count };
        f.Sql("DROP TRIGGER socket_fault"); await EffectRequestTransport.Send(f, input);
        Assert.Same(entry, Entry(f)); Assert.Equal(ordinal, entry.Ordinal); Assert.Equal(EffectOperationState.Committed, entry.State); Assert.Equal(1, trace.Attempts[1].Receipt!.Materials.Single().Remaining);
        var committed = await Assets(f, protocol); Assert.Equal(before.TargetUid, committed.TargetUid); Assert.Equal(before.MaterialUid, committed.MaterialUid); Assert.Equal(nativeCommit ? 1 : 2, committed.Spent); Assert.True(committed.Events > before.Events);
        var acks = f.Sender.Messages.OfType<ItemSocketResponse>().Count(); await EffectRequestTransport.Send(f, input);
        Assert.Same(entry, Entry(f)); Assert.Equal(ordinal, entry.Ordinal); Assert.Equal(2, trace.Attempts.Count); Assert.Equal(committed, await Assets(f, protocol)); Assert.Equal(acks, f.Sender.Messages.OfType<ItemSocketResponse>().Count()); Assert.True(EffectRequestTransport.IsStateReplay(f.Sender.Batches.Last()));
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Empty(f.Sender.Outbound.Closes);
        output.WriteLine(JsonSerializer.Serialize(new { failure, retry = new { entry.Ordinal, committed, state = entry.State.ToString() }, replay = new { actualCalls = trace.Attempts.Count, acks } }));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Four_real_socket_shapes_commit_original_groups_UIDs_footprint_and_ACK_first_frames(int protocol)
    {
        var trace = new Trace(); using var f = Fixture(trace); await Seed(f, protocol); trace.Attempts.Clear(); var before = await Assets(f, protocol); var usedBefore = ItemUsed(f);
        await EffectRequestTransport.Send(f, Input(protocol)); var state = await Assets(f, protocol);
        var target = await EquipmentSocketTests.Read(f, protocol is 1 or 3 ? InventoryContainerType.Avatar : InventoryContainerType.Main, protocol is 1 or 3 ? 0 : 9);
        output.WriteLine(JsonSerializer.Serialize(new { socketMutation = true, protocol, before, actual = state, target.EmblemId1, target.EmblemId2 }));
        var selected = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(Assert.Single(trace.Contexts).Request); var result = Assert.Single(trace.Results);
        Assert.Equal(EffectOperationState.Committed, selected.State.Entry.State); Assert.True(selected.HasCommittedUnit); Assert.Equal(ReplayProjectionNeeds.Inventory, selected.State.Entry.Projection!.Needs);
        Assert.Equal(protocol == 3 ? 3 : 2, selected.State.Entry.Projection.TouchedSlots.Length); Assert.Equal(before.TargetUid, state.TargetUid); Assert.Equal(before.MaterialUid, state.MaterialUid);
        Assert.NotNull(result.Receipt); var frames = f.Sender.Outbound.Frames; Assert.Equal(protocol == 3 ? 4 : 3, frames.Count); Assert.Equal((byte)1, frames[0][0]); Assert.Equal(Input(protocol).Metadata.Type, BitConverter.ToUInt16(frames[0], 1));
        Assert.Equal(protocol == 3 ? 2 : 1, result.Receipt.Materials.Length); Assert.All(result.Receipt.Materials, x => Assert.Equal(1, x.Remaining));
        if (protocol < 2) Assert.Equal(new byte[] { 1, protocol == 0 ? (byte)9 : (byte)0, 0, 65, 0 }, frames[0][15..]);
        if (protocol == 2) { Assert.Equal(new byte[] { 1, 1, 0x21, 1, 1, 0, 0, 0 }, frames[0][15..]); Assert.Equal(1, state.MaterialCount); Assert.Equal(5000, target.EmblemId1); Assert.Equal(5000, target.EmblemId2); }
        var usedAfter = ItemUsed(f); Assert.Equal(protocol == 3 ? 2 : 1, usedAfter.Rows - usedBefore.Rows); Assert.Equal(protocol < 2 ? 1 : protocol == 3 ? 3 : 2, usedAfter.Count - usedBefore.Count);
        var calls = trace.Attempts.Count; var acks = f.Sender.Messages.OfType<ItemSocketResponse>().Count(); await EffectRequestTransport.Send(f, Input(protocol));
        Assert.Equal(state, await Assets(f, protocol)); Assert.Equal(calls, trace.Attempts.Count); Assert.Equal(acks, f.Sender.Messages.OfType<ItemSocketResponse>().Count()); Assert.True(EffectRequestTransport.IsStateReplay(f.Sender.Batches.Last()));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Natural_domain_locked_refusal_commits_Main_without_mutation(int protocol)
    {
        var trace = new Trace(); using var f = Fixture(trace); await Seed(f, protocol); trace.Attempts.Clear();
        if (protocol is 0 or 2) await f.SeedCore(9, EquipmentSocketTests.Equipment(protocol == 2 ? (byte)2 : (byte)0) with { EquipmentLockId = 1 });
        else await f.SeedCore(0, StackableItemCore.Create(8, 3001, 98765) with { EquipmentLockId = 1 }, InventoryContainerType.Avatar);
        var before = await Assets(f, protocol); await EffectRequestTransport.Send(f, Input(protocol));
        Assert.Equal(before, await Assets(f, protocol)); Assert.Equal(ItemSocketRejection.Locked, Assert.Single(trace.Results).Rejection); Assert.Equal(EffectOperationState.Committed, Entry(f).State);
        Assert.NotNull(Entry(f).Projection); Assert.Empty(Entry(f).Projection!.TouchedSlots); Assert.Equal(new byte[] { 0, 0x16 }, Assert.Single(f.Sender.Outbound.Frames)[15..]);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Invalid_wire_body_rejects_without_Main_and_without_calling_domain(int protocol)
    {
        var trace = new Trace(); using var f = Fixture(trace); await EffectRequestTransport.SendRaw(f, Input(protocol).Metadata, []);
        var selected = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(Assert.Single(trace.Contexts).Request);
        Assert.False(selected.HasCommittedUnit); Assert.Equal(EffectOperationState.Rejected, Entry(f).State); Assert.Null(Entry(f).Projection); Assert.Empty(trace.Attempts);
        Assert.Equal(new byte[] { 0, 4 }, Assert.Single(f.Sender.Outbound.Frames)[15..]);
    }

    [Fact]
    public async Task Avatar_embed_invalid_list_mapping_is_early_and_does_not_claim_Main()
    {
        var trace = new Trace(); using var f = Fixture(trace); var input = Input(3); var body = EffectRequestTransport.Body(input); body[0] = (byte)InventoryContainerType.Main;
        await EffectRequestTransport.SendRaw(f, input.Metadata, body); Assert.Equal(EffectOperationState.Rejected, Entry(f).State); Assert.Empty(trace.Attempts); Assert.False(Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(trace.Contexts[0].Request).HasCommittedUnit);
        Assert.Equal(new byte[] { 0, 4 }, Assert.Single(f.Sender.Outbound.Frames)[15..]);
    }

    [Theory]
    [InlineData(4001)] [InlineData(1001)]
    public async Task Avatar_defaults_validate_real_pile_but_do_not_consume_it_or_publish_material_frames(int materialId)
    {
        var trace = new Trace(); using var f = Fixture(trace, new() { AvatarDefaults = [16, 4, 4] }); await Seed(f, 1); await f.Seed(65, materialId, 2); var before = await Assets(f, 1);
        await EffectRequestTransport.Send(f, Input(1)); var receipt = Assert.Single(trace.Results).Receipt!; Assert.NotNull(receipt); Assert.Empty(receipt.Materials);
        var after = await Assets(f, 1); Assert.Equal(before.MaterialCount, after.MaterialCount); Assert.Equal(before.MaterialUid, after.MaterialUid); Assert.Equal(before.TargetUid, after.TargetUid); Assert.NotEqual(before.Socket, after.Socket);
        Assert.Equal(before.Events, after.Events); Assert.Equal(EffectOperationState.Committed, Entry(f).State); Assert.Equal(2, Entry(f).Projection!.TouchedSlots.Length);
        Assert.Equal(2, f.Sender.Outbound.Frames.Count); Assert.Equal(new byte[] { 1, 0, 0, 255, 255 }, f.Sender.Outbound.Frames[0][15..]);
    }

    [Theory]
    [InlineData("missing")] [InlineData("locked")] [InlineData("expired")]
    public async Task Avatar_defaults_do_not_skip_existing_material_validation(string defect)
    {
        var trace = new Trace(); using var f = Fixture(trace, new() { AvatarDefaults = [16, 4, 4] }); await Seed(f, 1);
        await f.SeedCore(65, StackableItemCore.Create(2, 4001, defect == "missing" ? 0 : 2) with { EquipmentLockId = defect == "locked" ? (byte)1 : (byte)0, ExpireTime = defect == "expired" ? (int)f.Clock.GetUtcNow().AddSeconds(-1).ToUnixTimeSeconds() : 0 });
        var before = await Assets(f, 1); await EffectRequestTransport.Send(f, Input(1)); Assert.Equal(before, await Assets(f, 1)); Assert.Equal(ItemSocketRejection.MissingMaterial, Assert.Single(trace.Results).Rejection);
        Assert.Equal(EffectOperationState.Committed, Entry(f).State); Assert.Equal(new byte[] { 0, 0x11 }, Assert.Single(f.Sender.Outbound.Frames)[15..]);
    }

    [Fact]
    public async Task Replay_reads_changed_current_material_and_does_not_repeat_ItemUsed_or_ACK()
    {
        var trace = new Trace(); using var f = Fixture(trace); await Seed(f, 2); await EffectRequestTransport.Send(f, Input(2)); var first = await Assets(f, 2); var usedBefore = ItemUsed(f);
        await f.Seed(289, 5000, 9, 9); await EffectRequestTransport.Send(f, Input(2));
        Assert.Equal(9, EffectRequestTransport.MainValue(f, 289)); Assert.Single(trace.Attempts); Assert.Single(f.Sender.Messages.OfType<ItemSocketResponse>()); Assert.Equal(usedBefore, ItemUsed(f)); Assert.Equal(first.TargetUid, (await Assets(f, 2)).TargetUid);
    }

    [Fact]
    public async Task Initial_Rejected_cannot_upgrade_after_selection_and_next_fresh_works()
    {
        var trace = new Trace(); using var f = Fixture(trace); await Seed(f, 0);
        TestGameplay.Run(f.Services, () => { f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(f.Session); f.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(f.Session.CharacterId); });
        trace.Before = () => f.Select(1); await EffectRequestTransport.Send(f, Input(0)); Assert.IsType<CommandRequestContext.ProtocolEffectRejected>(trace.Contexts[0].Request); Assert.Empty(trace.Attempts); Assert.Equal(2, f.Count(4000));
        trace.Before = null; await EffectRequestTransport.Send(f, Input(0, 8)); Assert.Equal(1, f.Count(4000)); Assert.Single(trace.Attempts);
    }

    [Fact]
    public async Task Initial_notification_encoder_failure_is_normal_Failed_and_current_replay_does_not_double_spend()
    {
        var trace = new Trace(); using var f = Fixture(trace); await Seed(f, 2); f.Sender.FailBatch = true; await EffectRequestTransport.Send(f, Input(2));
        Assert.Equal(EffectOperationState.Committed, Entry(f).State); Assert.Equal(1, f.Count(5000)); Assert.Empty(f.Sender.Messages); Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Empty(f.Sender.Outbound.Closes);
        f.Sender.FailBatch = false; await EffectRequestTransport.Send(f, Input(2)); Assert.Equal(1, f.Count(5000)); Assert.Single(trace.Attempts); Assert.True(EffectRequestTransport.IsStateReplay(f.Sender.Batches.Last()));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Direct_ACK_encoder_failure_retains_early_committed_and_rollback_default_protection(int stage)
    {
        var trace = new Trace(); using var f = Fixture(trace); await Seed(f, 0); if (stage == 1) await f.Seed(65, 4000, 0); if (stage == 2) f.Sql(ActionDdl);
        var before = await Assets(f, 0); var cause = new IOException("direct socket ACK encode"); f.Sender.FailSends(() => cause);
        async Task Send() { if (stage == 0) await EffectRequestTransport.SendRaw(f, Input(0).Metadata, []); else await EffectRequestTransport.Send(f, Input(0)); }
        if (stage == 1) { var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(Send); Assert.Same(cause, failure.InnerException); Assert.True(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); }
        else { Assert.Same(cause, await Assert.ThrowsAsync<IOException>(Send)); Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); }
        Assert.Equal(stage == 0 ? EffectOperationState.Rejected : stage == 1 ? EffectOperationState.Committed : EffectOperationState.ConfirmedRollback, Entry(f).State);
        Assert.Equal(before.Core, Convert.ToHexString(f.StoredCore(9))); Assert.Empty(f.Sender.Messages); Assert.Equal(stage == 1 ? 2 : 1, f.Sender.Outbound.Closes.Count);
        Assert.All(f.Sender.Outbound.Entries.Where(x => x.IsClose), x => Assert.Same(f.Connection, x.Connection));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Real_SQL_and_independent_rollback_error_keep_both_occurrences_and_no_failure_ACK(bool sameObject)
    {
        var trace = new Trace { RollbackFault = true, SameObject = sameObject }; using var f = Fixture(trace); await Seed(f, 2); var before = await Assets(f, 2); f.Sql(ActionDdl);
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => EffectRequestTransport.Send(f, Input(2)).AsTask()); var original = Assert.Single(trace.Attempts).ActionFailure;
        Assert.Equal(1811, Assert.IsType<SqliteException>(original).SqliteExtendedErrorCode); var causes = Assert.IsType<AggregateException>(failure.InnerException).InnerExceptions;
        Assert.Equal(2, causes.Count); Assert.Same(original, causes[0]); Assert.Same(sameObject ? original : trace.CleanupFailure, causes[1]); Assert.Equal(before.Core, Convert.ToHexString(f.StoredCore(9))); Assert.Equal(before.MaterialCount, f.Count(5000));
        Assert.Empty(f.Sender.Messages); Assert.Empty(f.Sender.Batches); Assert.Equal(EffectOperationState.Blocked, Entry(f).State); Assert.True(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Equal(2, f.Sender.Outbound.Closes.Count);
    }

    [Fact]
    public async Task Actual_domain_DemandActive_rejection_cannot_be_recovered_as_an_ordinary_failure_ACK()
    {
        var trace = new Trace { BadOwner = true }; using var f = Fixture(trace); await Seed(f, 2); var before = await Assets(f, 2);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => EffectRequestTransport.Send(f, Input(2)).AsTask()); Assert.Same(Assert.Single(trace.Attempts).ActionFailure, failure);
        Assert.Equal(before, await Assets(f, 2)); Assert.Empty(f.Sender.Messages); Assert.Empty(f.Sender.Batches); Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Single(f.Sender.Outbound.Closes);
    }

    private static ShopFixture Fixture(Trace trace, EquipmentSocketTests.Content? content = null) => EquipmentSocketTests.Fixture(content, s => Configure(s, trace));
    private static async Task Seed(ShopFixture f, int protocol)
    {
        if (protocol is 0 or 2) await EquipmentSocketTests.Seed(f, protocol == 2 ? (byte)2 : (byte)0);
        else
        {
            await f.SeedCore(0, StackableItemCore.Create(8, 3001, 98765), InventoryContainerType.Avatar); await f.Seed(65, 4001, 2);
            await f.Seed(289, 5003, 2, 9); await f.Seed(290, 5000, 3, 9);
            if (protocol == 3) await ItemSocketDomainSetup(f);
        }
    }
    private static async Task ItemSocketDomainSetup(ShopFixture f)
    {
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var tx = ((DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
            using var footprint = new Dfo.Server.Application.Tests.Support.TestEffectFootprints.Lease(f.Session);
            Assert.NotNull((await f.Services.GetRequiredService<IItemSocketDomainService>().ApplyAsync(f.Session, new(true, true, new(BusinessInventorySpace.Avatar, 0), 3001, new(BusinessInventorySpace.Consumable, 0), []), footprint, default)).Receipt);
            await tx.CommitAsync(default);
        });
    }
    private static ItemSocketRequest Input(int protocol, ushort sequence = 7) => protocol switch
    {
        0 => new(new(1, 0x37A, 0, sequence), InventoryContainerType.Main, 9, 3000, 65, []),
        1 => new(new(1, 0xCE, 0, sequence), InventoryContainerType.Avatar, 0, 3001, 65, []),
        2 => new(new(1, 0x379, 0, sequence), InventoryContainerType.Main, 9, 3000, 0, [new(289, 5000, 0), new(289, 5000, 1)]),
        3 => new(new(1, 0xC9, 0, sequence), InventoryContainerType.Avatar, 0, 3001, 0, [new(289, 5003, 0), new(290, 5000, 1), new(290, 5000, 2)]),
        _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
    };
    private static (int Rows, int Count) ItemUsed(ShopFixture f) => (f.Scalar("SELECT count(*) FROM domain_events WHERE event_type='character.item-used'"),
        f.Scalar("SELECT COALESCE(SUM(json_extract(payload_json,'$.Count')),0) FROM domain_events WHERE event_type='character.item-used'"));
    private sealed record AssetState(string Core, string Socket, int TargetUid, int MaterialUid, int MaterialCount, int Spent, int Gold, int Events, int Deliveries);
    private static async Task<AssetState> Assets(ShopFixture f, int protocol)
    {
        var avatar = protocol is 1 or 3; var list = avatar ? InventoryContainerType.Avatar : InventoryContainerType.Main; var slot = avatar ? 0 : 9; var materialSlot = protocol == 2 ? 289 : 65;
        var target = await EquipmentSocketTests.Read(f, list, slot); var count = f.Scalar($"SELECT COALESCE(SUM(count),0) FROM item_slots WHERE list_type=0 AND slot_index={materialSlot}");
        return new(Convert.ToHexString(f.StoredCore(slot, list)), target.AvatarDetail is { } detail ? Convert.ToHexString(detail.JewelSocket) : $"{target.EmblemSocketCount}:{target.EmblemId1}:{target.EmblemId2}",
            f.Scalar($"SELECT core_uid FROM item_slots WHERE list_type={(byte)list} AND slot_index={slot}"), f.Scalar($"SELECT core_uid FROM item_slots WHERE list_type=0 AND slot_index={materialSlot}"), count, (protocol == 2 ? 3 : 2) - count, f.Gold,
            f.Scalar("SELECT count(*) FROM domain_events"), f.Scalar("SELECT count(*) FROM domain_event_deliveries"));
    }
    private static EffectOperationEntry Entry(ShopFixture f)
    {
        var ledger = f.Services.GetRequiredService<EffectOperationLedger>(); var table = typeof(EffectOperationLedger).GetField("ledgers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ledger)!; object?[] args = [f.Connection, null]; Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!);
        var entries = (IDictionary)args[1]!.GetType().GetProperty("Entries")!.GetValue(args[1])!; return Assert.Single(entries.Values.Cast<EffectOperationEntry>());
    }
    private static void Configure(IServiceCollection services, Trace trace)
    {
        services.AddSingleton(trace); services.AddSingleton<ILoggerProvider>(trace); services.AddSingleton<ItemSocketDomainService>();
        services.AddScoped<GameItemSocketCommandHandler>(); services.AddScoped<ICommandHandler<GameItemSocketCommand, ItemSocketResult>, ObserveWhole>(); services.AddSingleton<IItemSocketDomainService>(p => new Observe(p.GetRequiredService<ItemSocketDomainService>(), p, trace));
    }
    private static ValueTask<int> Scalar(IServiceProvider services, string sql, CancellationToken ct) => services.GetRequiredService<SqliteDatabase>().ReadAsync(async (cmd, token) => { cmd.CommandText = sql; return Convert.ToInt32(await cmd.ExecuteScalarAsync(token)); }, ct);
    private sealed class Attempt(DomainTransaction transaction)
    {
        internal DomainTransaction Transaction { get; } = transaction; internal int ForeignKeys, Material, Children; internal string? Socket; internal ItemSocketReceipt? Receipt; internal Exception? ActionFailure;
    }
    private sealed class Observe(IItemSocketDomainService actual, IServiceProvider services, Trace trace) : IItemSocketDomainService
    {
        public async ValueTask<ItemSocketResult> ApplyAsync(SelectedSessionIdentity session, ItemSocketInput input, IEffectFootprintRecorder footprint, CancellationToken ct)
        {
            var attempt = new Attempt((DomainTransaction)services.GetRequiredService<IDomainTransactionAccessor>().Current!); trace.Attempts.Add(attempt); if (trace.RollbackFault) attempt.Transaction.Enlist(trace, () => new RollbackError(trace, attempt)); attempt.ForeignKeys = await Scalar(services, "PRAGMA foreign_keys", ct);
            try
            {
                var owner = trace.BadOwner ? SelectedSessionIdentity.Create(session.AccountId, new(2), session.ConnectionId, session.ConnectionGeneration, session.WireUserId) : session;
                var result = await actual.ApplyAsync(owner, input, footprint, ct); attempt.Receipt = result.Receipt;
                var mat = input.Open ? 65 : 289; attempt.Material = await Scalar(services, $"SELECT COALESCE(SUM(count),0) FROM item_slots WHERE list_type=0 AND slot_index={mat}", ct);
                attempt.Socket = await services.GetRequiredService<SqliteDatabase>().ReadAsync(async (cmd, token) => { cmd.CommandText = $"SELECT hex(avatar_jewel_socket) FROM items i JOIN item_slots s ON i.item_uid=s.core_uid WHERE s.list_type={(input.Avatar ? 1 : 0)} AND s.slot_index={(input.Avatar ? 0 : 9)}"; return Convert.ToString(await cmd.ExecuteScalarAsync(token)); }, ct);
                if (await Scalar(services, "SELECT count(*) FROM sqlite_master WHERE name='socket_child'", ct) != 0) attempt.Children = await Scalar(services, "SELECT count(*) FROM socket_child", ct); return result;
            }
            catch (Exception cause) { attempt.ActionFailure = cause; throw; }
        }
    }
    private sealed class ObserveWhole(GameItemSocketCommandHandler actual, Trace trace) : ICommandHandler<GameItemSocketCommand, ItemSocketResult>
    {
        public async ValueTask<ItemSocketResult> HandleAsync(CommandExecutionContext context, GameItemSocketCommand command, CancellationToken ct)
        { trace.Contexts.Add(context); trace.Before?.Invoke(); var result = await actual.HandleAsync(context, command, ct); trace.Results.Add(result); return result; }
    }
    private sealed class RollbackError(Trace trace, Attempt attempt) : ITransactionParticipant
    {
        public bool CommitCanFail => false; public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask; public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => throw (trace.SameObject ? attempt.ActionFailure! : trace.CleanupFailure);
    }
    private sealed class Trace : ILoggerProvider
    {
        internal List<Attempt> Attempts { get; } = []; internal List<CommandExecutionContext> Contexts { get; } = []; internal List<ItemSocketResult> Results { get; } = [];
        internal Action? Before; internal bool RollbackFault, SameObject, BadOwner; internal IOException CleanupFailure { get; } = new("independent socket rollback error"); internal ConcurrentQueue<(string Category, LogLevel Level, Exception Cause)> Errors { get; } = new();
        public ILogger CreateLogger(string name) => new Logger(this, name); public void Dispose() { }
        private sealed class Logger(Trace trace, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null; public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? cause, Func<TState, Exception?, string> formatter) { if (cause is not null) trace.Errors.Enqueue((category, level, cause)); }
        }
    }
}
