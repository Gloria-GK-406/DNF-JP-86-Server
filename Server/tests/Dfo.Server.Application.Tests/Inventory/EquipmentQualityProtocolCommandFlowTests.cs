using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Sessions;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
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
using Dfo.Server.Protocol.Game.ResetItemQuality;
using Dfo.Server.Protocol.Game.ResetRandomOption;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentQualityProtocolCommandFlowTests(ITestOutputHelper output)
{
    private const string NativeDdl = "CREATE TABLE quality_parent(id INTEGER PRIMARY KEY); CREATE TABLE quality_child(id INTEGER REFERENCES quality_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER quality_fault AFTER UPDATE ON items WHEN substr(hex(NEW.item_core),3,8)='B80B0000' BEGIN INSERT INTO quality_child VALUES(404); END;";
    private const string ActionDdl = "CREATE TRIGGER quality_fault BEFORE UPDATE ON items BEGIN SELECT RAISE(ABORT,'quality action SQL failure'); END;";

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Replacing_the_admitted_actor_before_the_whole_never_rerolls_or_consumes(bool grade)
    {
        var trace = new Trace(); using var f = Fixture(grade, trace); await Seed(f, grade);
        var before = Assets(f); trace.Before = () => TestSessions.ReselectOnSameConnection(f.Services.GetRequiredService<ISelectedSessionDomainService>(), f.Services.GetRequiredService<Dfo.Server.Application.Users.OnlineStateRegistry>(), f.Session);
        await Assert.ThrowsAsync<InvalidOperationException>(() => EffectRequestTransport.Send(f, Input(grade)).AsTask());
        Assert.Empty(trace.Attempts); Assert.Empty(f.Sender.Messages);
        Assert.Equal(2, f.Scalar("SELECT SUM(count) FROM item_slots WHERE owner_id=1 AND list_type=0 AND slot_index=65"));
        Assert.Equal(before, Assets(f));
        Assert.Same(f.Connection, Assert.Single(f.Sender.Outbound.Entries, e => e.IsClose).Connection);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task Actual_action_SQL_and_native_Commit_keep_failure_ack_cause_assets_and_original_entry_retry(bool grade, bool nativeCommit)
    {
        var trace = new Trace(); using var f = Fixture(grade, trace); await Seed(f, grade);
        var before = Assets(f); f.Sql(nativeCommit ? NativeDdl : ActionDdl);
        var request = Input(grade); var body = EffectRequestTransport.Body(request);
        await EffectRequestTransport.Send(f, request);
        var attempt = Assert.Single(trace.Attempts); Assert.Equal(1, attempt.ForeignKeys);
        var errors = trace.Errors.Where(x => x.Cause is SqliteException).ToArray(); Assert.NotEmpty(errors);
        var cause = Assert.IsType<SqliteException>(errors[0].Cause); Assert.All(errors, x => Assert.Same(cause, x.Cause));
        Assert.Contains(errors, x => x.Level == LogLevel.Error && x.Category.EndsWith(grade ? "GameResetRandomOptionCommandHandler" : "GameResetItemQualityCommandHandler", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, x => x.Category.Contains("CommandLoggingBehavior", StringComparison.Ordinal));
        Assert.Equal(19, cause.SqliteErrorCode); Assert.Equal(nativeCommit ? 787 : 1811, cause.SqliteExtendedErrorCode);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, attempt.Transaction.Completion);
        if (nativeCommit)
        {
            Assert.NotNull(attempt.Receipt); Assert.Null(attempt.ActionFailure); Assert.Equal(1, attempt.Material);
            Assert.True(attempt.Children > 0); Assert.NotEqual(before.Core, attempt.Core);
            SqliteFailureAssertions.FromCommit(attempt.Transaction, cause);
        }
        else { Assert.Null(attempt.Receipt); Assert.Same(cause, attempt.ActionFailure); SqliteFailureAssertions.FromAction(attempt.Transaction, cause); }
        var entry = Entry(f); var ordinal = entry.Ordinal;
        Assert.Equal(EffectOperationState.ConfirmedRollback, entry.State); Assert.Equal(nativeCommit, entry.Projection is not null);
        Assert.Equal(request.Metadata.Type, entry.Type); Assert.Equal(request.Metadata.Selector, entry.Selector); Assert.Equal(request.Metadata.Sequence, entry.Sequence);
        Assert.Equal(body.Length, entry.Length); Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)), entry.Hash, ignoreCase: true);
        Assert.Equal(before, Assets(f)); Assert.Empty(f.Sender.Batches); Assert.Single(f.Sender.Messages);
        Assert.Equal(new byte[] { 0, 0x13 }, Assert.Single(f.Sender.Outbound.Frames)[15..]);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Empty(f.Sender.Outbound.Closes);
        var failure = new { grade, nativeCommit, ddl = nativeCommit ? NativeDdl : ActionDdl, body = Convert.ToHexString(body), entry.Hash, entry.Length, entry.Type, entry.Selector, entry.Sequence, ordinal,
            ack = Convert.ToHexString(f.Sender.Outbound.Frames[0]), cause.SqliteErrorCode, cause.SqliteExtendedErrorCode, cause.StackTrace, attempt.ForeignKeys,
            actionReturnedReceipt = attempt.Receipt is not null, attempt.Material, attempt.Children, attempt.Core, completion = attempt.Transaction.Completion.ToString(), state = entry.State.ToString(), projection = entry.Projection is not null, before,
            logs = errors.Select(x => new { x.Category, level = x.Level.ToString() }), sameCause = errors.All(x => ReferenceEquals(cause, x.Cause)), sourceClose = f.Sender.Outbound.Closes.Count };
        f.Sql("DROP TRIGGER quality_fault"); await EffectRequestTransport.Send(f, request);
        Assert.Same(entry, Entry(f)); Assert.Equal(ordinal, entry.Ordinal); Assert.Equal(EffectOperationState.Committed, entry.State);
        Assert.Equal(1, f.Count(4000)); Assert.NotEqual(before.Core, Assets(f).Core); Assert.Equal(before.Uid, Assets(f).Uid); Assert.Equal(before.Gold, f.Gold);
        Assert.Equal(2, trace.Attempts.Count); Assert.NotNull(trace.Attempts[1].Receipt); Assert.Equal(DomainTransactionCompletion.Committed, trace.Attempts[1].Transaction.Completion);
        var committed = Assets(f); var acks = f.Sender.Messages.Count(x => x is ResetItemQualityResponse or ResetRandomOptionResponse);
        await EffectRequestTransport.Send(f, request);
        Assert.Same(entry, Entry(f)); Assert.Equal(ordinal, entry.Ordinal); Assert.Equal(2, trace.Attempts.Count); Assert.Equal(committed, Assets(f));
        Assert.Equal(acks, f.Sender.Messages.Count(x => x is ResetItemQualityResponse or ResetRandomOptionResponse)); Assert.True(EffectRequestTransport.IsStateReplay(f.Sender.Batches.Last()));
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Empty(f.Sender.Outbound.Closes);
        output.WriteLine(JsonSerializer.Serialize(new { failure, retry = new { entry.Ordinal, state = entry.State.ToString(), committed }, replay = new { actualCalls = trace.Attempts.Count, acks } }));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Real_success_commits_minimum_inventory_without_recorders_and_replay_never_rerolls(bool grade)
    {
        var trace = new Trace(); using var f = Fixture(grade, trace); await Seed(f, grade); var before = Assets(f);
        var input = Input(grade); await EffectRequestTransport.Send(f, input);
        var selected = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(Assert.Single(trace.Contexts).Request);
        Assert.True(selected.HasCommittedUnit); Assert.Equal(EffectOperationState.Committed, selected.State.Entry.State);
        Assert.Equal(ReplayProjectionNeeds.Inventory, selected.State.Entry.Projection!.Needs); Assert.Empty(selected.State.Entry.Projection.TouchedSlots);
        Assert.Equal(1, f.Count(4000)); Assert.Equal(before.Uid, Assets(f).Uid); Assert.NotEqual(before.Core, Assets(f).Core);
        var frames = f.Sender.Outbound.Frames; Assert.Equal(grade ? 3 : 2, frames.Count);
        Assert.Equal(grade ? (byte)0 : (byte)1, frames[0][0]);
        Assert.Equal(grade ? (ushort)0xE : (ushort)0x51, BitConverter.ToUInt16(frames[0], 1));
        if (grade) { Assert.Equal((ushort)65, BitConverter.ToUInt16(frames[1], 18)); Assert.Equal(new byte[] { 1 }, frames[2][15..]); }
        else { Assert.Equal(new byte[] { 1, 65, 0, 1, 0, 0, 0, 2, 0 }, frames[0][15..]); Assert.Equal((ushort)9, BitConverter.ToUInt16(frames[1], 18)); }
        var committed = Assets(f); var acks = f.Sender.Messages.Count(x => x is ResetItemQualityResponse or ResetRandomOptionResponse);
        await EffectRequestTransport.Send(f, input); Assert.Equal(committed, Assets(f)); Assert.Single(trace.Attempts);
        Assert.Equal(acks, f.Sender.Messages.Count(x => x is ResetItemQualityResponse or ResetRandomOptionResponse)); Assert.True(EffectRequestTransport.IsStateReplay(f.Sender.Batches.Last()));
    }

    [Fact]
    public async Task A_wire_ushort256_reaches_the_original_domain_and_commits_refusal_without_consuming_option_zero()
    {
        var trace = new Trace(); using var f = Fixture(true, trace); await Seed(f, true); var before = Assets(f);
        await EffectRequestTransport.Send(f, Input(true, option: 256));
        output.WriteLine(JsonSerializer.Serialize(new { option = 256, before, actual = Assets(f), state = Entry(f).State.ToString() }));
        Assert.Equal(before, Assets(f)); Assert.Equal(2, f.Count(4000));
        Assert.Equal(EquipmentQualityRejection.InvalidOption, Assert.Single(trace.Results).Rejection);
        Assert.Equal(EffectOperationState.Committed, Entry(f).State); Assert.True(Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(trace.Contexts[0].Request).HasCommittedUnit);
        Assert.Equal(new byte[] { 0, 0x13 }, Assert.Single(f.Sender.Outbound.Frames)[15..]); Assert.Empty(Entry(f).Projection!.TouchedSlots);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Invalid_body_or_unmappable_slot_is_rejected_without_Main_or_domain_calls(bool grade, bool unmappable)
    {
        var trace = new Trace(); using var f = Fixture(grade, trace); await Seed(f, grade); var before = Assets(f);
        var request = Input(grade); var body = unmappable ? EffectRequestTransport.Body(request) : [];
        if (unmappable) System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(body, ushort.MaxValue);
        await EffectRequestTransport.SendRaw(f, request.Metadata, body);
        var selected = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(Assert.Single(trace.Contexts).Request);
        Assert.False(selected.HasCommittedUnit); Assert.Equal(EffectOperationState.Rejected, selected.State.Entry.State); Assert.Null(selected.State.Entry.Projection);
        Assert.Empty(trace.Attempts); Assert.Equal(before, Assets(f)); Assert.Equal(new byte[] { 0, 0x13 }, Assert.Single(f.Sender.Outbound.Frames)[15..]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Initially_rejected_request_cannot_upgrade_after_selection_then_next_fresh_request_works(bool grade)
    {
        var trace = new Trace(); using var f = Fixture(grade, trace); await Seed(f, grade);
        TestGameplay.Run(f.Services, () => { f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(f.Session); f.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(f.Session.CharacterId); });
        trace.Before = () => f.Select(1);
        await EffectRequestTransport.Send(f, Input(grade));
        Assert.IsType<CommandRequestContext.ProtocolEffectRejected>(trace.Contexts[0].Request); Assert.Empty(trace.Attempts); Assert.Equal(2, f.Count(4000));
        trace.Before = null; await EffectRequestTransport.Send(f, Input(grade, 8)); Assert.Equal(1, f.Count(4000)); Assert.Single(trace.Attempts);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Original_missing_material_refusal_still_commits_Main_and_current_inventory(bool grade)
    {
        var trace = new Trace(); using var f = Fixture(grade, trace); await Seed(f, grade); await f.Seed(65, 4000, 0); var before = Assets(f);
        await EffectRequestTransport.Send(f, Input(grade)); Assert.Equal(before, Assets(f));
        Assert.Equal(EffectOperationState.Committed, Entry(f).State); Assert.NotNull(Entry(f).Projection); Assert.Empty(Entry(f).Projection!.TouchedSlots);
        Assert.Equal(EquipmentQualityRejection.MaterialUnavailable, Assert.Single(trace.Results).Rejection);
        Assert.Equal(new byte[] { 0, grade ? (byte)0x15 : (byte)0x11 }, Assert.Single(f.Sender.Outbound.Frames)[15..]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task A_controlled_InWorld_query_keeps_original_domain_NoSession_inside_normal_Main(bool grade)
    {
        // Only IsInWorld is controlled; real registry/current/physical owner and all state stay unchanged.
        var trace = new Trace { NoWorld = true }; using var f = Fixture(grade, trace); await Seed(f, grade); var before = Assets(f);
        await EffectRequestTransport.Send(f, Input(grade));
        Assert.Equal(EquipmentQualityRejection.NoSession, Assert.Single(trace.Results).Rejection);
        Assert.Equal(EffectOperationState.Committed, Entry(f).State); Assert.Equal(before, Assets(f));
        Assert.True(Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(trace.Contexts[0].Request).HasCommittedUnit);
        Assert.Equal(new byte[] { 0, 0x13 }, Assert.Single(f.Sender.Outbound.Frames)[15..]);
    }

    [PvfTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Reseal_nonnull_success_or_refusal_never_falls_back_to_quality(bool refuse)
    {
        var trace = new Trace(); using var f = EquipmentQualityTests.Fixture(new EquipmentQualityTests.Content { Rarity = 3 }, s => Configure(s, trace));
        await f.SeedCore(9, EquipmentQualityTests.Equipment with { SealFlag = refuse ? (byte)1 : (byte)0, TradeRestriction = 0 }); await f.Seed(65, 14, 30);
        var before = f.StoredCore(9); await EffectRequestTransport.Send(f, Input(false)); Assert.Empty(trace.Attempts); Assert.Equal(EffectOperationState.Committed, Entry(f).State);
        if (refuse) { Assert.Equal(before, f.StoredCore(9)); Assert.Equal(30, f.Count(14)); Assert.Equal(new byte[] { 0, 0x12 }, Assert.Single(f.Sender.Outbound.Frames)[15..]); }
        else { Assert.Equal(14, f.Count(14)); Assert.Equal(new byte[] { 1, 65, 0, 14, 0, 0, 0, 1, 0 }, f.Sender.Outbound.Frames[0][15..]); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Original_notification_encoder_failure_is_Failed_normally_then_only_current_replay(bool grade)
    {
        var trace = new Trace(); using var f = Fixture(grade, trace); await Seed(f, grade); f.Sender.FailBatch = true;
        var input = Input(grade); await EffectRequestTransport.Send(f, input);
        Assert.Equal(EffectOperationState.Committed, Entry(f).State); Assert.Equal(1, f.Count(4000)); Assert.Empty(f.Sender.Messages);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Empty(f.Sender.Outbound.Closes);
        f.Sender.FailBatch = false; await EffectRequestTransport.Send(f, input); Assert.Equal(1, f.Count(4000)); Assert.Single(trace.Attempts);
        Assert.True(EffectRequestTransport.IsStateReplay(f.Sender.Batches.Last())); Assert.Empty(f.Sender.Messages.OfType<ResetItemQualityResponse>()); Assert.Empty(f.Sender.Messages.OfType<ResetRandomOptionResponse>());
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)]
    [InlineData(true, 0)] [InlineData(true, 1)] [InlineData(true, 2)]
    public async Task Direct_refusal_ACK_encoder_failure_keeps_default_phase_protection(bool grade, int stage)
    {
        var trace = new Trace(); using var f = Fixture(grade, trace); await Seed(f, grade);
        if (stage == 1) await f.Seed(65, 4000, 0); if (stage == 2) f.Sql(ActionDdl);
        var before = Assets(f); var cause = new IOException("same ordinary direct ACK encoder failure"); f.Sender.FailSends(() => cause);
        async Task Send() { if (stage == 0) await EffectRequestTransport.SendRaw(f, Input(grade).Metadata, []); else await EffectRequestTransport.Send(f, Input(grade)); }
        if (stage == 1) { var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(Send); Assert.Same(cause, failure.InnerException); Assert.True(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Equal(EffectOperationState.Committed, Entry(f).State); }
        else { var failure = await Assert.ThrowsAsync<IOException>(Send); Assert.Same(cause, failure); Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Equal(stage == 0 ? EffectOperationState.Rejected : EffectOperationState.ConfirmedRollback, Entry(f).State); }
        Assert.Equal(before, Assets(f)); Assert.Empty(f.Sender.Messages); Assert.Equal(stage == 1 ? 2 : 1, f.Sender.Outbound.Closes.Count);
        Assert.All(f.Sender.Outbound.Entries.Where(x => x.IsClose), x => Assert.Same(f.Connection, x.Connection));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_business_SQL_and_independent_rollback_fault_preserve_both_causes_without_failure_ACK(bool sameObject)
    {
        var trace = new Trace { RollbackFault = true, SameObject = sameObject }; using var f = Fixture(true, trace); await Seed(f, true); var before = Assets(f); f.Sql(ActionDdl);
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => EffectRequestTransport.Send(f, Input(true)).AsTask());
        var original = Assert.Single(trace.Attempts).ActionFailure; Assert.Equal(1811, Assert.IsType<SqliteException>(original).SqliteExtendedErrorCode);
        var causes = Assert.IsType<AggregateException>(failure.InnerException).InnerExceptions; Assert.Equal(2, causes.Count); Assert.Same(original, causes[0]); Assert.Same(sameObject ? original : trace.CleanupFailure, causes[1]);
        Assert.Equal(before, Assets(f)); Assert.Empty(f.Sender.Messages); Assert.Empty(f.Sender.Batches); Assert.Equal(EffectOperationState.Blocked, Entry(f).State);
        Assert.True(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Equal(2, f.Sender.Outbound.Closes.Count);
    }

    private static ShopFixture Fixture(bool grade, Trace trace) => grade ? MagicSealGradeTests.Fixture(s => Configure(s, trace)) : EquipmentQualityTests.Fixture(configure: s => Configure(s, trace));
    private static Task Seed(ShopFixture f, bool grade) => grade ? MagicSealGradeTests.Seed(f) : EquipmentQualityTests.Seed(f);
    private static IEffectSettlementRequest Input(bool grade, ushort sequence = 7, ushort option = 1) => grade ? new ResetRandomOptionRequest(new(1, 0x1C8, 0, sequence), 9, 65, option) : new ResetItemQualityRequest(new(1, 0x51, 0, sequence), 9, 3000, 65);
    private sealed record AssetState(string Core, int Uid, int Material, int Gold, int Events, int Deliveries);
    private static AssetState Assets(ShopFixture f) => new(Convert.ToHexString(f.StoredCore(9)), f.Scalar("SELECT core_uid FROM item_slots WHERE list_type=0 AND slot_index=9"), f.Count(4000), f.Gold, f.Scalar("SELECT count(*) FROM domain_events"), f.Scalar("SELECT count(*) FROM domain_event_deliveries"));
    private static EffectOperationEntry Entry(ShopFixture f)
    {
        var ledger = f.Services.GetRequiredService<EffectOperationLedger>();
        var table = typeof(EffectOperationLedger).GetField("ledgers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ledger)!;
        object?[] args = [f.Connection, null]; Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!);
        var entries = (IDictionary)args[1]!.GetType().GetProperty("Entries")!.GetValue(args[1])!; return Assert.Single(entries.Values.Cast<EffectOperationEntry>());
    }
    private static void Configure(IServiceCollection services, Trace trace)
    {
        services.AddSingleton(trace); services.AddSingleton<ILoggerProvider>(trace); services.AddSingleton<EquipmentQualityDomainService>();
        if (trace.NoWorld) services.AddSingleton<ISelectedSessionDomainService>(p => new NotInWorldQuery(p.GetRequiredService<SelectedSessionDomainService>()));
        services.AddScoped<GameResetItemQualityCommandHandler>(); services.AddScoped<GameResetRandomOptionCommandHandler>();
        services.AddScoped<ICommandHandler<GameResetItemQualityCommand, EquipmentQualityResult>, ObserveWhole<GameResetItemQualityCommand, GameResetItemQualityCommandHandler>>();
        services.AddScoped<ICommandHandler<GameResetRandomOptionCommand, EquipmentQualityResult>, ObserveWhole<GameResetRandomOptionCommand, GameResetRandomOptionCommandHandler>>();
        services.AddSingleton<IEquipmentQualityDomainService>(p => new ObserveQuality(p.GetRequiredService<EquipmentQualityDomainService>(), p, trace));
    }
    private sealed class Attempt(DomainTransaction transaction)
    {
        internal DomainTransaction Transaction { get; } = transaction;
        internal int ForeignKeys, Material, Children;
        internal string? Core;
        internal EquipmentQualityReceipt? Receipt;
        internal Exception? ActionFailure;
    }
    private static ValueTask<int> Scalar(IServiceProvider services, string sql, CancellationToken ct) => services.GetRequiredService<SqliteDatabase>().ReadAsync(async (cmd, token) =>
    { cmd.CommandText = sql; return Convert.ToInt32(await cmd.ExecuteScalarAsync(token)); }, ct);
    private sealed class ObserveQuality(IEquipmentQualityDomainService actual, IServiceProvider services, Trace trace) : IEquipmentQualityDomainService
    {
        public ValueTask<EquipmentQualityResult> RerollAsync(SelectedSessionIdentity session, BusinessSlot target, int id, BusinessSlot material, CancellationToken ct) => Observe(() => actual.RerollAsync(session, target, id, material, ct), ct);
        public ValueTask<EquipmentQualityResult> RerollMagicSealGradeAsync(SelectedSessionIdentity session, BusinessSlot target, BusinessSlot material, ushort option, CancellationToken ct) => Observe(() => actual.RerollMagicSealGradeAsync(session, target, material, option, ct), ct);
        private async ValueTask<EquipmentQualityResult> Observe(Func<ValueTask<EquipmentQualityResult>> action, CancellationToken ct)
        {
            var attempt = new Attempt((DomainTransaction)services.GetRequiredService<IDomainTransactionAccessor>().Current!); trace.Attempts.Add(attempt);
            if (trace.RollbackFault) attempt.Transaction.Enlist(trace, () => new RollbackFailure(trace, attempt));
            attempt.ForeignKeys = await Scalar(services, "PRAGMA foreign_keys", ct);
            try
            {
                var result = await action(); attempt.Receipt = result.Receipt;
                attempt.Material = await Scalar(services, "SELECT count FROM item_slots WHERE list_type=0 AND slot_index=65", ct);
                attempt.Core = await services.GetRequiredService<SqliteDatabase>().ReadAsync(async (cmd, token) => { cmd.CommandText = "SELECT hex(i.item_core) FROM items i JOIN item_slots s ON i.item_uid=s.core_uid WHERE s.list_type=0 AND s.slot_index=9"; return (string)(await cmd.ExecuteScalarAsync(token))!; }, ct);
                if (await Scalar(services, "SELECT count(*) FROM sqlite_master WHERE name='quality_child'", ct) != 0) attempt.Children = await Scalar(services, "SELECT count(*) FROM quality_child", ct);
                return result;
            }
            catch (Exception failure) { attempt.ActionFailure = failure; throw; }
        }
    }
    private sealed class ObserveWhole<TCommand, THandler>(THandler actual, Trace trace) : ICommandHandler<TCommand, EquipmentQualityResult>
        where TCommand : class, ICommand<EquipmentQualityResult> where THandler : ICommandHandler<TCommand, EquipmentQualityResult>
    {
        public async ValueTask<EquipmentQualityResult> HandleAsync(CommandExecutionContext context, TCommand command, CancellationToken ct)
        { trace.Contexts.Add(context); trace.Before?.Invoke(); var result = await actual.HandleAsync(context, command, ct); trace.Results.Add(result); return result; }
    }
    private sealed class RollbackFailure(Trace trace, Attempt attempt) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => throw (trace.SameObject ? attempt.ActionFailure! : trace.CleanupFailure);
    }
    private sealed class NotInWorldQuery(ISelectedSessionDomainService actual) : ISelectedSessionDomainService
    {
        public SelectedSessionIdentity? Select(SelectedSessionIdentity s) => actual.Select(s);
        public void EnterWorld(SelectedSessionIdentity s) => actual.EnterWorld(s);
        public bool Fence(SelectedSessionIdentity s) => actual.Fence(s);
        public bool ReleaseExact(SelectedSessionIdentity s) => actual.ReleaseExact(s);
        public SelectedSessionIdentity? Find(AccountId id) => actual.Find(id);
        public SelectedSessionIdentity? FindByCharacter(CharacterId id) => actual.FindByCharacter(id);
        public SelectedSessionIdentity? FindByWireUserId(ushort id) => actual.FindByWireUserId(id);
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId id) => actual.FindByConnection(id);
        public SelectedSessionStanding? StandingOf(SelectedSessionIdentity s) => actual.StandingOf(s);
        public bool IsSelected(SelectedSessionIdentity s) => actual.IsSelected(s);
        public bool IsInWorld(SelectedSessionIdentity s) => false;
        public bool TryGetInWorld(CharacterId id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SelectedSessionIdentity? s) => actual.TryGetInWorld(id, out s);
        public bool TryGetInWorld(ushort id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SelectedSessionIdentity? s) => actual.TryGetInWorld(id, out s);
        public bool TryGetInWorld(NetworkConnectionId id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SelectedSessionIdentity? s) => actual.TryGetInWorld(id, out s);
    }
    private sealed class Trace : ILoggerProvider
    {
        internal List<Attempt> Attempts { get; } = [];
        internal List<CommandExecutionContext> Contexts { get; } = [];
        internal List<EquipmentQualityResult> Results { get; } = [];
        internal Action? Before;
        internal bool RollbackFault, SameObject, NoWorld;
        internal IOException CleanupFailure { get; } = new("independent actual rollback failure");
        internal ConcurrentQueue<(string Category, LogLevel Level, Exception Cause)> Errors { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
        public void Dispose() { }
        private sealed class Logger(Trace owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            { if (exception is not null) owner.Errors.Enqueue((category, level, exception)); }
        }
    }
}
