using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Sessions;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
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
using Dfo.Server.Protocol.Game.AvatarCompound;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class AvatarCompoundProtocolCommandFlowTests(ITestOutputHelper output)
{
    private const string ActionDdl = "CREATE TRIGGER compound_fault BEFORE DELETE ON item_slots BEGIN SELECT RAISE(ABORT,'compound action SQL failure'); END;";
    private const string NativeDdl = "CREATE TABLE compound_parent(id INTEGER PRIMARY KEY); CREATE TABLE compound_child(id INTEGER REFERENCES compound_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER compound_fault AFTER INSERT ON items WHEN NEW.avatar_expire_date IS NOT NULL BEGIN INSERT INTO compound_child VALUES(404); END;";

    [Fact]
    public async Task Replacing_the_admitted_actor_before_the_whole_preserves_both_avatars_and_material()
    {
        var trace = new Trace(); using var f = Fixture(trace); await AvatarCompoundTests.Seed(f);
        var before = await Assets(f); trace.Before = () => TestSessions.ReselectOnSameConnection(f.Services.GetRequiredService<ISelectedSessionDomainService>(), f.Services.GetRequiredService<Dfo.Server.Application.Users.OnlineStateRegistry>(), f.Session);
        await Assert.ThrowsAsync<InvalidOperationException>(() => EffectRequestTransport.Send(f, Input()).AsTask());
        Assert.Empty(trace.Attempts); Assert.Empty(f.Sender.Messages);
        Assert.Equal(before, await Assets(f));
        Assert.Same(f.Connection, Assert.Single(f.Sender.Outbound.Entries, e => e.IsClose).Connection);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Old_and_new_compound_wire_keep_action_SQL_or_native_Commit_failure_ack_and_original_entry_retry(bool nativeCommit)
    {
        var trace = new Trace(); using var f = Fixture(trace); await AvatarCompoundTests.Seed(f);
        var before = await Assets(f); f.Sql(nativeCommit ? NativeDdl : ActionDdl);
        var input = Input(); var body = EffectRequestTransport.Body(input); await EffectRequestTransport.Send(f, input);
        var attempt = Assert.Single(trace.Attempts); Assert.Equal(1, attempt.ForeignKeys);
        var errors = trace.Errors.Where(x => x.Cause is SqliteException).ToArray(); Assert.NotEmpty(errors); var cause = Assert.IsType<SqliteException>(errors[0].Cause); Assert.All(errors, x => Assert.Same(cause, x.Cause));
        Assert.Contains(errors, x => x.Level == LogLevel.Error && x.Category.EndsWith("GameAvatarCompoundCommandHandler", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, x => x.Category.Contains("CommandLoggingBehavior", StringComparison.Ordinal));
        Assert.Equal(19, cause.SqliteErrorCode); Assert.Equal(nativeCommit ? 787 : 1811, cause.SqliteExtendedErrorCode);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, attempt.Transaction.Completion);
        if (nativeCommit)
        {
            Assert.NotNull(attempt.Receipt); Assert.Equal(2, attempt.Receipt.Outputs.Length); Assert.Null(attempt.ActionFailure);
            Assert.Equal(2, attempt.Children); Assert.Equal(2, attempt.AvatarRows); Assert.Equal(1, attempt.Material); Assert.Equal(2, attempt.AvatarSlots);
            SqliteFailureAssertions.FromCommit(attempt.Transaction, cause);
        }
        else
        {
            Assert.Null(attempt.Receipt); Assert.Same(cause, attempt.ActionFailure); Assert.Equal(4, attempt.AvatarRows); Assert.Equal(2, attempt.Material);
            Assert.Equal(2, attempt.AvatarSlots); SqliteFailureAssertions.FromAction(attempt.Transaction, cause);
        }
        var entry = Entry(f); var ordinal = entry.Ordinal; Assert.Equal(EffectOperationState.ConfirmedRollback, entry.State); Assert.Equal(nativeCommit, entry.Projection is not null);
        Assert.Equal(input.Metadata.Type, entry.Type); Assert.Equal(input.Metadata.Sequence, entry.Sequence); Assert.Equal(body.Length, entry.Length); Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)), entry.Hash, ignoreCase: true);
        Assert.Equal(before, await Assets(f)); Assert.Empty(f.Sender.Batches); Assert.Single(f.Sender.Messages); Assert.Equal(new byte[] { 0, 0x17 }, Assert.Single(f.Sender.Outbound.Frames)[15..]);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Empty(f.Sender.Outbound.Closes);
        var failure = new { compoundOracle = true, nativeCommit, ddl = nativeCommit ? NativeDdl : ActionDdl, body = Convert.ToHexString(body), entry.Hash, entry.Length, entry.Selector, entry.Type, entry.Sequence, ordinal,
            ack = Convert.ToHexString(f.Sender.Outbound.Frames[0]), cause.SqliteErrorCode, cause.SqliteExtendedErrorCode, cause.StackTrace, attempt.ForeignKeys,
            actionReturnedReceipt = attempt.Receipt is not null, attempt.Material, attempt.Children, attempt.AvatarRows, attempt.AvatarSlots, attempt.OutputUids, completion = attempt.Transaction.Completion.ToString(), state = entry.State.ToString(), projection = entry.Projection is not null, before,
            logs = errors.Select(x => new { x.Category, level = x.Level.ToString() }), sameCause = errors.All(x => ReferenceEquals(cause, x.Cause)), sourceCloses = f.Sender.Outbound.Closes.Count };
        f.Sql("DROP TRIGGER compound_fault"); await EffectRequestTransport.Send(f, input);
        Assert.Same(entry, Entry(f)); Assert.Equal(ordinal, entry.Ordinal); Assert.Equal(EffectOperationState.Committed, entry.State);
        var receipt = trace.Attempts[1].Receipt!; Assert.Equal(2, receipt.Outputs.Length); Assert.All(receipt.Outputs, x => Assert.DoesNotContain(x.Item.ItemUid!.Value.Value, new[] { before.FirstUid, before.SecondUid }));
        Assert.Equal(1, f.Count(21)); Assert.Equal(0, f.Count(39086)); Assert.Equal(0, f.Count(39087)); Assert.Equal(1, f.Count(39088)); Assert.Equal(1, f.Count(42601));
        Assert.Equal(131, f.Sender.Outbound.Frames.Last().Length - 15); var committed = await Assets(f); Assert.True(committed.Events > before.Events);
        var acks = f.Sender.Messages.OfType<AvatarCompoundResponse>().Count(); await EffectRequestTransport.Send(f, input);
        Assert.Same(entry, Entry(f)); Assert.Equal(ordinal, entry.Ordinal); Assert.Equal(2, trace.Attempts.Count); Assert.Equal(committed, await Assets(f));
        var recovery = Assert.IsType<AvatarCompoundResponse>(f.Sender.Batches.Last().Messages[0]); Assert.True(recovery.Recovery); Assert.Null(recovery.Receipt);
        Assert.Equal(acks + 1, f.Sender.Messages.OfType<AvatarCompoundResponse>().Count()); Assert.Contains(f.Sender.Batches.Last().Messages.Skip(1), x => x is not AvatarCompoundResponse);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Empty(f.Sender.Outbound.Closes);
        output.WriteLine(JsonSerializer.Serialize(new { failure, retry = new { entry.Ordinal, committed, state = entry.State.ToString() }, replay = new { actualCalls = trace.Attempts.Count, acks, recovery = true } }));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Complete_wire_commits_only_original_ACK_with_independent_UIDs_and_exact_Main_projection(bool rare)
    {
        var trace = new Trace(); using var f = Fixture(trace, rare ? 10000 : 0); await AvatarCompoundTests.Seed(f); var before = await Assets(f);
        await EffectRequestTransport.Send(f, Input()); var selected = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(Assert.Single(trace.Contexts).Request);
        Assert.True(selected.HasCommittedUnit); Assert.Equal(EffectOperationState.Committed, Entry(f).State); Assert.Equal(ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.AvatarCompound, Entry(f).Projection!.Needs);
        Assert.Equal(new[] { new BusinessSlot(BusinessInventorySpace.Avatar, 0), new BusinessSlot(BusinessInventorySpace.Avatar, 1), new BusinessSlot(BusinessInventorySpace.Consumable, 0) }.OrderBy(x => x.ToString()), Entry(f).Projection!.TouchedSlots.OrderBy(x => x.ToString()));
        var receipt = Assert.Single(trace.Results).Receipt!; Assert.NotNull(receipt); Assert.Equal(rare ? 2 : 1, receipt.Outputs.Length);
        Assert.Equal(0, f.Count(39086)); Assert.Equal(0, f.Count(39087)); Assert.Equal(1, f.Count(21)); Assert.Equal(1, f.Count(39088)); Assert.Equal(rare ? 1 : 0, f.Count(42601));
        Assert.All(receipt.Outputs, x => { Assert.True(x.Item.ItemUid!.Value.Value > Math.Max(before.FirstUid, before.SecondUid)); Assert.NotEqual(12345, x.Item.InstanceValue); Assert.NotEqual(54321, x.Item.InstanceValue); Assert.Equal(0, x.Item.Attribute); Assert.Equal(new byte[30], x.Item.AvatarDetail!.JewelSocket); Assert.Equal(0, x.Item.AvatarDetail.ExpireDate); });
        Assert.Equal(receipt.Outputs.Length, receipt.Outputs.Select(x => x.Item.ItemUid).Distinct().Count());
        var first = Assert.Single(f.Sender.Outbound.Frames); Assert.Equal(rare ? 131 : 89, first.Length - 15); Assert.Equal(1, first[0]); Assert.Equal(3, first[16]); Assert.Single(f.Sender.Messages); Assert.IsType<AvatarCompoundResponse>(f.Sender.Messages[0]);
        Assert.Equal(1, f.Scalar("SELECT count(*) FROM domain_events WHERE event_type='character.item-used'"));
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Empty(f.Sender.Outbound.Closes);
    }

    [Theory]
    [InlineData("material", 0x16)] [InlineData("ability", 0x17)] [InlineData("locked", 0xD2)] [InlineData("lifetime", 0x13)]
    public async Task Natural_domain_refusal_still_commits_Main_and_minimum_UI_inventory_without_spending(string reason, byte code)
    {
        var trace = new Trace(); using var f = Fixture(trace); await AvatarCompoundTests.Seed(f);
        if (reason == "material") await f.Seed(65, 21, 0);
        if (reason is "locked" or "lifetime") await f.SeedCore(0, StackableItemCore.Create(8, 39086, 12345) with { EquipmentLockId = reason == "locked" ? (byte)1 : (byte)0, ExpireTime = reason == "lifetime" ? 123 : 0 }, InventoryContainerType.Avatar);
        var before = await Assets(f); await EffectRequestTransport.Send(f, Input(ability: reason == "ability" ? (ushort)99 : (ushort)0));
        Assert.Equal(before, await Assets(f)); Assert.Null(Assert.Single(trace.Results).Receipt); Assert.Single(trace.Attempts); Assert.Equal(EffectOperationState.Committed, Entry(f).State);
        Assert.True(Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(trace.Contexts[0].Request).HasCommittedUnit); Assert.Empty(Entry(f).Projection!.TouchedSlots);
        Assert.Equal(ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.AvatarCompound, Entry(f).Projection!.Needs); Assert.Equal(new byte[] { 0, code }, Assert.Single(f.Sender.Outbound.Frames)[15..]);
    }

    [Fact]
    public async Task Full_ushort_Ability256_reaches_original_domain_rejection_without_consumption_or_new_output_UID()
    {
        var trace = new Trace(); using var f = Fixture(trace, 0); await AvatarCompoundTests.Seed(f); var before = await Assets(f);
        await EffectRequestTransport.Send(f, Input(ability: 256)); var state = await Assets(f);
        output.WriteLine(JsonSerializer.Serialize(new { compoundAbilityMutation = true, before, actual = state, frames = f.Sender.Outbound.Frames.Select(x => Convert.ToHexString(x)) }));
        Assert.Equal(before, state); Assert.Equal(AvatarCompoundRejection.InvalidRequest, Assert.Single(trace.Results).Rejection); Assert.Null(trace.Results[0].Receipt);
        Assert.Equal(EffectOperationState.Committed, Entry(f).State); Assert.Equal(new byte[] { 0, 0x17 }, Assert.Single(f.Sender.Outbound.Frames)[15..]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Invalid_wire_or_unmappable_slot_rejects_without_Main_or_domain(bool mapping)
    {
        var trace = new Trace(); using var f = Fixture(trace); await AvatarCompoundTests.Seed(f); var before = await Assets(f);
        if (mapping) await EffectRequestTransport.Send(f, Input() with { MaterialSlot = ushort.MaxValue });
        else await EffectRequestTransport.SendRaw(f, Input().Metadata, []);
        Assert.Equal(before, await Assets(f)); Assert.Equal(EffectOperationState.Rejected, Entry(f).State); Assert.Empty(trace.Attempts); Assert.Null(Entry(f).Projection);
        Assert.False(Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(Assert.Single(trace.Contexts).Request).HasCommittedUnit); Assert.Equal(new byte[] { 0, 0x17 }, Assert.Single(f.Sender.Outbound.Frames)[15..]);
    }

    [Fact]
    public async Task Initial_Rejected_cannot_upgrade_after_selection_and_next_fresh_can_compound()
    {
        var trace = new Trace(); using var f = Fixture(trace); await AvatarCompoundTests.Seed(f);
        TestGameplay.Run(f.Services, () => { f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(f.Session); f.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(f.Session.CharacterId); });
        trace.Before = () =>
        {
            f.Select(1);
            f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(f.Session.CharacterId,
                new PersistentItemContainer(ContainerId.ForCharacter(f.Session.CharacterId, InventoryContainerType.Avatar), new(InventoryContainerType.Avatar, 0),
                    f.Services.GetRequiredService<IItemDomainStore>(), f.Services.GetRequiredService<IDomainTransactionAccessor>(),
                    f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>()));
        }; await EffectRequestTransport.Send(f, Input()); Assert.IsType<CommandRequestContext.ProtocolEffectRejected>(trace.Contexts[0].Request); Assert.Empty(trace.Attempts); Assert.Equal(2, f.Count(21));
        trace.Before = null; await EffectRequestTransport.Send(f, Input(8)); Assert.Single(trace.Attempts); Assert.Equal(1, f.Count(21)); Assert.Equal(0, f.Count(39086));
    }

    [Fact]
    public async Task Initial_notification_encoder_Failed_is_isolated_and_replay_recovers_UI_before_changed_current_inventory()
    {
        var trace = new Trace(); using var f = Fixture(trace); await AvatarCompoundTests.Seed(f); f.Sender.FailBatch = true; await EffectRequestTransport.Send(f, Input());
        Assert.Equal(EffectOperationState.Committed, Entry(f).State); Assert.Equal(1, f.Count(21)); Assert.Empty(f.Sender.Messages); Assert.Empty(f.Sender.Outbound.Closes); Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        var used = f.Scalar("SELECT count(*) FROM domain_events WHERE event_type='character.item-used'"); await f.Seed(65, 21, 9); f.Sender.FailBatch = false; await EffectRequestTransport.Send(f, Input());
        Assert.Single(trace.Attempts); Assert.Equal(9, f.Count(21)); Assert.Equal(used, f.Scalar("SELECT count(*) FROM domain_events WHERE event_type='character.item-used'"));
        var batch = f.Sender.Batches.Last(); Assert.True(Assert.IsType<AvatarCompoundResponse>(batch.Messages[0]).Recovery); Assert.Equal(26, batch.Frames[0].Length - 15); Assert.Contains(batch.Messages.Skip(1), x => x is not AvatarCompoundResponse);
        Assert.DoesNotContain(f.Sender.Messages.OfType<AvatarCompoundResponse>(), x => x.Receipt is not null); Assert.Equal(9, EffectRequestTransport.MainValue(f, 65));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Direct_rejection_ACK_encoder_failure_keeps_early_committed_and_rollback_default_protection(int stage)
    {
        var trace = new Trace(); using var f = Fixture(trace); await AvatarCompoundTests.Seed(f); if (stage == 1) await f.Seed(65, 21, 0); if (stage == 2) f.Sql(ActionDdl);
        var before = await Assets(f); var cause = new IOException("direct compound ACK encode"); f.Sender.FailSends(() => cause);
        async Task Send() { if (stage == 0) await EffectRequestTransport.SendRaw(f, Input().Metadata, []); else await EffectRequestTransport.Send(f, Input()); }
        if (stage == 1) { var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(Send); Assert.Same(cause, failure.InnerException); Assert.True(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); }
        else { Assert.Same(cause, await Assert.ThrowsAsync<IOException>(Send)); Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); }
        Assert.Equal(stage == 0 ? EffectOperationState.Rejected : stage == 1 ? EffectOperationState.Committed : EffectOperationState.ConfirmedRollback, Entry(f).State);
        Assert.Equal(before, await Assets(f)); Assert.Empty(f.Sender.Messages); Assert.Equal(stage == 1 ? 2 : 1, f.Sender.Outbound.Closes.Count); Assert.All(f.Sender.Outbound.Entries.Where(x => x.IsClose), x => Assert.Same(f.Connection, x.Connection));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Action_SQL_and_independent_rollback_fault_keep_both_occurrences_and_no_normal_ACK(bool sameObject)
    {
        var trace = new Trace { RollbackFault = true, SameObject = sameObject }; using var f = Fixture(trace); await AvatarCompoundTests.Seed(f); var before = await Assets(f); f.Sql(ActionDdl);
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => EffectRequestTransport.Send(f, Input()).AsTask()); var original = Assert.Single(trace.Attempts).ActionFailure;
        Assert.Equal(1811, Assert.IsType<SqliteException>(original).SqliteExtendedErrorCode); var causes = Assert.IsType<AggregateException>(failure.InnerException).InnerExceptions;
        Assert.Equal(2, causes.Count); Assert.Same(original, causes[0]); Assert.Same(sameObject ? original : trace.CleanupFailure, causes[1]); Assert.Equal(before, await Assets(f));
        Assert.Empty(f.Sender.Messages); Assert.Empty(f.Sender.Batches); Assert.Equal(EffectOperationState.Blocked, Entry(f).State); Assert.True(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Equal(2, f.Sender.Outbound.Closes.Count);
    }

    [Fact]
    public async Task Actual_domain_first_DemandActive_wrong_owner_is_sticky_and_cannot_become_failure_ACK()
    {
        var trace = new Trace { BadOwner = true }; using var f = Fixture(trace); await AvatarCompoundTests.Seed(f); var before = await Assets(f);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => EffectRequestTransport.Send(f, Input()).AsTask()); Assert.Same(Assert.Single(trace.Attempts).ActionFailure, failure);
        Assert.Equal(before, await Assets(f)); Assert.Empty(f.Sender.Messages); Assert.Empty(f.Sender.Batches); Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Single(f.Sender.Outbound.Closes);
    }

    private static ShopFixture Fixture(Trace trace, int rate = 10000) => AvatarCompoundTests.Fixture(new() { Rate = rate }, s => Configure(s, trace), archive: false);
    private static AvatarCompoundRequest Input(ushort sequence = 7, ushort ability = 0) => new(new(1, 0x63, 0, sequence), 65, 0, 39086, 1, 39087, 39088, ability, 0, 0);
    private sealed record AssetState(string Items, string Slots, int FirstUid, int SecondUid, int BoxUid, int Gold, int Events, int Deliveries, string Cooldowns, string Effects);
    private static Task<AssetState> Assets(ShopFixture f) => TestGameplay.RunAsync(f.Services, async () => new AssetState(
        await Rows(f.Services, "SELECT item_uid,hex(item_core),avatar_expire_date,avatar_clear_id,hex(avatar_jewel_socket),avatar_color1,avatar_color2,avatar_delete_date,is_mirror,mirror_source_uid FROM items ORDER BY item_uid", default),
        await Rows(f.Services, "SELECT * FROM item_slots ORDER BY owner_scope,owner_id,list_type,slot_index", default),
        f.Scalar("SELECT COALESCE(MAX(core_uid),0) FROM item_slots WHERE list_type=1 AND slot_index=0"), f.Scalar("SELECT COALESCE(MAX(core_uid),0) FROM item_slots WHERE list_type=1 AND slot_index=1"), f.Scalar("SELECT COALESCE(MAX(core_uid),0) FROM item_slots WHERE list_type=0 AND slot_index=65"), f.Gold,
        f.Scalar("SELECT count(*) FROM domain_events"), f.Scalar("SELECT count(*) FROM domain_event_deliveries"), await Rows(f.Services, "SELECT * FROM character_item_cooldowns ORDER BY sort_order", default), await Rows(f.Services, "SELECT * FROM character_item_effects ORDER BY sort_order", default)));
    private static ValueTask<string> Rows(IServiceProvider services, string sql, CancellationToken ct) => services.GetRequiredService<SqliteDatabase>().ReadAsync(async (cmd, token) =>
    {
        cmd.CommandText = sql; await using var reader = await cmd.ExecuteReaderAsync(token); var rows = new List<string>();
        while (await reader.ReadAsync(token)) rows.Add(JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i))).ToArray()));
        return string.Join("\n", rows);
    }, ct);
    private static ValueTask<int> Scalar(IServiceProvider services, string sql, CancellationToken ct) => services.GetRequiredService<SqliteDatabase>().ReadAsync(async (cmd, token) => { cmd.CommandText = sql; return Convert.ToInt32(await cmd.ExecuteScalarAsync(token)); }, ct);
    private static EffectOperationEntry Entry(ShopFixture f)
    {
        var ledger = f.Services.GetRequiredService<EffectOperationLedger>(); var table = typeof(EffectOperationLedger).GetField("ledgers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ledger)!; object?[] args = [f.Connection, null]; Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!);
        var entries = (IDictionary)args[1]!.GetType().GetProperty("Entries")!.GetValue(args[1])!; return Assert.Single(entries.Values.Cast<EffectOperationEntry>());
    }
    private static void Configure(IServiceCollection services, Trace trace)
    {
        services.AddSingleton(trace); services.AddSingleton<ILoggerProvider>(trace); services.AddSingleton<AvatarCompoundDomainService>();
        services.AddScoped<GameAvatarCompoundCommandHandler>(); services.AddScoped<ICommandHandler<GameAvatarCompoundCommand, AvatarCompoundResult>, ObserveWhole>(); services.AddSingleton<IAvatarCompoundDomainService>(p => new Observe(p.GetRequiredService<AvatarCompoundDomainService>(), p, trace));
    }
    private sealed class Attempt(DomainTransaction transaction)
    {
        internal DomainTransaction Transaction { get; } = transaction; internal int ForeignKeys, Material, Children, AvatarRows, AvatarSlots; internal string OutputUids = ""; internal AvatarCompoundReceipt? Receipt; internal Exception? ActionFailure;
    }
    private sealed class Observe(IAvatarCompoundDomainService actual, IServiceProvider services, Trace trace) : IAvatarCompoundDomainService
    {
        public async ValueTask<AvatarCompoundResult> CompoundAsync(SelectedSessionIdentity session, AvatarCompoundInput input, IEffectFootprintRecorder footprint, CancellationToken ct)
        {
            var attempt = new Attempt((DomainTransaction)services.GetRequiredService<IDomainTransactionAccessor>().Current!); trace.Attempts.Add(attempt); if (trace.RollbackFault) attempt.Transaction.Enlist(trace, () => new RollbackError(trace, attempt)); attempt.ForeignKeys = await Scalar(services, "PRAGMA foreign_keys", ct);
            try { var owner = trace.BadOwner ? SelectedSessionIdentity.Create(session.AccountId, new(2), session.ConnectionId, session.ConnectionGeneration, session.WireUserId) : session; var result = await actual.CompoundAsync(owner, input, footprint, ct); attempt.Receipt = result.Receipt; return result; }
            catch (Exception cause) { attempt.ActionFailure = cause; throw; }
            finally
            {
                attempt.Material = await Scalar(services, "SELECT COALESCE(SUM(count),0) FROM item_slots WHERE list_type=0 AND slot_index=65", ct);
                attempt.AvatarRows = await Scalar(services, "SELECT count(*) FROM items WHERE avatar_expire_date IS NOT NULL", ct);
                attempt.AvatarSlots = await Scalar(services, "SELECT count(*) FROM item_slots WHERE list_type=1", ct);
                attempt.OutputUids = await Rows(services, "SELECT item_uid,hex(item_core) FROM items WHERE avatar_expire_date IS NOT NULL ORDER BY item_uid", ct);
                if (await Scalar(services, "SELECT count(*) FROM sqlite_master WHERE name='compound_child'", ct) != 0) attempt.Children = await Scalar(services, "SELECT count(*) FROM compound_child", ct);
            }
        }
    }
    private sealed class ObserveWhole(GameAvatarCompoundCommandHandler actual, Trace trace) : ICommandHandler<GameAvatarCompoundCommand, AvatarCompoundResult>
    {
        public async ValueTask<AvatarCompoundResult> HandleAsync(CommandExecutionContext context, GameAvatarCompoundCommand command, CancellationToken ct)
        { trace.Contexts.Add(context); trace.Before?.Invoke(); var result = await actual.HandleAsync(context, command, ct); trace.Results.Add(result); return result; }
    }
    private sealed class RollbackError(Trace trace, Attempt attempt) : ITransactionParticipant
    {
        public bool CommitCanFail => false; public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask; public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => throw (trace.SameObject ? attempt.ActionFailure! : trace.CleanupFailure);
    }
    private sealed class Trace : ILoggerProvider
    {
        internal List<Attempt> Attempts { get; } = []; internal List<CommandExecutionContext> Contexts { get; } = []; internal List<AvatarCompoundResult> Results { get; } = [];
        internal Action? Before; internal bool RollbackFault, SameObject, BadOwner; internal IOException CleanupFailure { get; } = new("independent compound rollback error");
        internal ConcurrentQueue<(string Category, LogLevel Level, Exception Cause)> Errors { get; } = new();
        public ILogger CreateLogger(string name) => new Logger(this, name); public void Dispose() { }
        private sealed class Logger(Trace trace, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null; public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? cause, Func<TState, Exception?, string> formatter) { if (cause is not null) trace.Errors.Enqueue((category, level, cause)); }
        }
    }
}
