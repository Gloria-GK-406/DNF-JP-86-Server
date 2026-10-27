using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Execution.Outbox;
using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol.Game.CeraShopPurchase;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class CeraShopPurchaseProtocolCommandFlowTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("success")] [InlineData("refusal")] [InlineData("malformed")]
    [InlineData("no-session")] [InlineData("no-online")]
    public async Task Ordinary_wire_result_preserves_assets_response_and_healthy_actor(string mode)
    {
        var t = new Trace(mode); using var f = Fixture(t); var before = await Assets(f);
        if (mode == "refusal") { f.Host.Sql("UPDATE accounts SET cera=1"); before = await Assets(f); }
        var input = mode == "malformed" ? new CeraShopPurchaseInput(0, 0, 0, [], "Truncated entry") : Input();
        var decoded = await CeraPurchaseWire.SendAsync(f.Host, input);
        Assert.Equal(ClientMessageType.GameCeraShopPurchase, decoded.MessageType);
        var ack = Assert.Single(f.Host.Sender.Messages.OfType<CeraShopPurchaseResponse>());
        if (mode == "success") { Assert.Equal(110000, ack.ProductId); Assert.Equal(96400, f.Cera); Assert.Equal(3, f.Host.Sender.Messages.Count); }
        else { Assert.Null(ack.ProductId); Assert.Equal(mode == "refusal" ? 11 : 1, ack.ErrorCode); Assert.Equal(before, await Assets(f)); }
        AssertHealthy(f); AssertNoLedger(f);
        if (mode is "no-session" or "no-online") Assert.Empty(t.Transactions);
        else { Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(t.Transactions).Completion); Assert.Equal(1, t.UnitCalls); }
        Write(mode, new { before, after = await Assets(f), t.UnitCalls, result = t.ResultKind, messages = Messages(f) });
    }

    [Theory] [InlineData("action")] [InlineData("commit")]
    public async Task Real_storage_failure_rolls_back_complete_assets_then_returns_original_rejection(string mode)
    {
        var t = new Trace(mode); using var f = Fixture(t); ConfigureFailure(f, mode); var before = await Assets(f);
        await CeraPurchaseWire.SendAsync(f.Host, Input(mode));
        var tx = Assert.Single(t.Transactions); Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, tx.Completion);
        Assert.Equal(before, await Assets(f)); var sql = Assert.IsType<SqliteException>(t.Cause);
        Assert.Equal(19, sql.SqliteErrorCode); Assert.Equal(mode == "action" ? 1811 : 787, sql.SqliteExtendedErrorCode);
        if (mode == "commit") SqliteFailureAssertions.FromCommit(tx, sql); else SqliteFailureAssertions.FromAction(tx, sql);
        Assert.Equal(mode == "commit", t.Receipt); Assert.NotEqual(before, t.StagedAssets);
        Assert.Equal(mode == "commit" ? "CommitRolledBack" : "Returned", t.ResultKind);
        if (mode == "commit") { Assert.Equal(1, t.ForeignKeys); Assert.Equal(1, t.Children); Assert.Same(sql, tx.CommitFailure!.Cause); Assert.Equal(TransactionFailureKind.KnownStorage, tx.CommitFailure.Kind); }
        var ack = Assert.IsType<CeraShopPurchaseResponse>(Assert.Single(f.Host.Sender.Messages)); Assert.Null(ack.ProductId); Assert.Equal(1, ack.ErrorCode);
        AssertHealthy(f); AssertNoLedger(f);
        f.Host.Sql(mode == "action" ? "DROP TRIGGER cera_flow_fail_pet" : "DROP TRIGGER cera_flow_deferred"); t.Mode = "retry"; f.Host.Sender.Messages.Clear();
        await CeraPurchaseWire.SendAsync(f.Host, Input(mode));
        Assert.Equal(mode == "action" ? 76600 : 96400, f.Cera);
        Assert.Equal(mode == "action" ? 2 : 1, f.Host.Sender.Messages.OfType<CeraShopPurchaseResponse>().Count());
        Write(mode, new { before, staged = t.StagedAssets, after = await Assets(f), sql.SqliteExtendedErrorCode, result = t.ResultKind });
    }

    [Theory] [InlineData("nonfallible")] [InlineData("rollback")] [InlineData("scope")]
    public async Task Uncertain_or_scope_failure_is_not_a_storage_rejection_and_protects_the_exact_buyer(string mode)
    {
        var t = new Trace(mode); using var f = Fixture(t); ConfigureFailure(f, mode); var before = await Assets(f);
        var error = await Record.ExceptionAsync(() => CeraPurchaseWire.SendAsync(f.Host, Input(mode)).AsTask());
        Assert.NotNull(error); Assert.True(TransactionOutcomeUncertainException.Contains(error)); Assert.True(GameplayJob.EndedAJob(error));
        var tx = Assert.Single(t.Transactions); Assert.Equal(mode == "scope" ? DomainTransactionCompletion.Committed : DomainTransactionCompletion.Uncertain, tx.Completion);
        if (mode == "rollback") { Assert.Equal(before, await Assets(f)); Assert.True(Includes(error, Assert.IsType<SqliteException>(t.Cause))); Assert.True(Includes(error, t.RollbackFault)); }
        else { Assert.Equal(96400, f.Cera); Assert.NotEqual(before, await Assets(f)); Assert.True(Includes(error, mode == "scope" ? t.ScopeFault : t.CommitFault)); }
        Assert.True(f.Host.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Host.Session)!.Fenced);
        Assert.True(f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Host.Connection));
        Assert.DoesNotContain(f.Host.Session, f.Host.Services.GetRequiredService<ICreatureRuntimeDomainService>().DueSessions());
        Assert.NotEmpty(f.Host.Sender.Outbound.Closes); Assert.All(f.Host.Sender.Outbound.Entries.Where(e => e.IsClose), e => Assert.Same(f.Host.Connection, e.Connection));
        Assert.True(f.Host.Sender.Outbound.Entries.Last().IsClose);
        Assert.DoesNotContain(f.Host.Sender.Messages.OfType<CeraShopPurchaseResponse>(), x => x.ProductId is null);
        if (mode == "scope") { Assert.Equal(3, t.MessagesBeforeScope.Length); Assert.Contains(typeof(CeraShopPurchaseResponse).FullName, t.MessagesBeforeScope); Assert.Equal(110000, Assert.Single(f.Host.Sender.Messages.OfType<CeraShopPurchaseResponse>()).ProductId); }
        else Assert.Empty(f.Host.Sender.Messages);
        AssertNoLedger(f);
        Write(mode, new { completion = tx.Completion.ToString(), cause = error.ToString(), before, after = await Assets(f), t.MessagesBeforeScope, closes = f.Host.Sender.Outbound.Closes.Count });
    }

    [Theory] [InlineData("cancelled")] [InlineData("typed")] [InlineData("sticky")]
    public async Task Independent_action_failure_cannot_be_mapped_to_an_ordinary_refusal(string mode)
    {
        var t = new Trace(mode); using var f = Fixture(t); var before = await Assets(f);
        var error = await Record.ExceptionAsync(() => CeraPurchaseWire.SendAsync(f.Host, Input()).AsTask());
        Assert.NotNull(error); Assert.True(Includes(error, t.IndependentFault)); Assert.True(t.Receipt);
        Assert.Equal(mode == "typed" ? DomainTransactionCompletion.Uncertain : DomainTransactionCompletion.ConfirmedRollback, Assert.Single(t.Transactions).Completion);
        Assert.Equal(before, await Assets(f)); Assert.Null(t.ResultKind);
        Assert.Empty(f.Host.Sender.Messages.OfType<CeraShopPurchaseResponse>());
        Assert.NotEmpty(f.Host.Sender.Outbound.Closes); AssertNoLedger(f);
        Write(mode, new { cause = error.ToString(), before, after = await Assets(f) });
    }

    [Fact]
    public async Task Same_non_effect_sequence_can_buy_again_without_a_main_or_replay_entry()
    {
        var t = new Trace("success"); using var f = Fixture(t);
        await CeraPurchaseWire.SendAsync(f.Host, Input(), 7); var next = await CeraPurchaseWire.SendAsync(f.Host, Input(), 7);
        Assert.False(next.Metadata.SequenceIsFresh); Assert.Equal(92800, f.Cera); Assert.Equal(2, f.Host.Count(3));
        Assert.Equal(2, t.UnitCalls); Assert.Equal(2, f.Host.Sender.Messages.OfType<CeraShopPurchaseResponse>().Count()); AssertNoLedger(f); AssertHealthy(f);
    }

    [Fact]
    public async Task Scope_failure_preserves_replacement_runtime_and_another_physical_connection()
    {
        var t = new Trace("scope"); using var f = Fixture(t); var old = f.Host.Session;
        var other = new NetworkConnectionContext(new("cera-other-physical"), NetworkEndpointKind.Game, 10011);
        SelectedSessionIdentity? replacement = null;
        t.BeforeScopeFault = () =>
        {
            var registry = f.Host.Services.GetRequiredService<OnlineStateRegistry>();
            var user = registry.RegisterConnection(other); registry.Authenticate(user.OnlineUserId, old.AccountId);
            replacement = SelectedSessionIdentity.Create(old.AccountId, old.CharacterId, other.ConnectionId, registry.GetRequiredUser(user.OnlineUserId).Generation, 10011);
            registry.Select(replacement); registry.EnterWorld(replacement);
            f.Host.Services.GetRequiredService<ICreatureRuntimeDomainService>().Enter(replacement);
        };
        var error = await Record.ExceptionAsync(() => CeraPurchaseWire.SendAsync(f.Host, Input()).AsTask());
        Assert.NotNull(error); Assert.True(Includes(error, t.ScopeFault)); Assert.NotNull(replacement);
        Assert.Contains(replacement, f.Host.Services.GetRequiredService<ICreatureRuntimeDomainService>().DueSessions());
        Assert.False(f.Host.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(replacement)!.Fenced);
        Assert.False(f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(other));
        Assert.True(f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Host.Connection));
        Assert.All(f.Host.Sender.Outbound.Entries.Where(e => e.IsClose), e => Assert.Same(f.Host.Connection, e.Connection)); Assert.DoesNotContain(f.Host.Sender.Outbound.Entries, e => e.IsClose && ReferenceEquals(e.Connection, other));
        Assert.NotEmpty(f.Host.Sender.Outbound.Closes); Assert.Equal(96400, f.Cera);
        Write("replacement", new { old, replacement, sourceExact = f.Host.Sender.Outbound.Entries.Where(e => e.IsClose).All(e => ReferenceEquals(e.Connection, f.Host.Connection)) });
    }

    private void Write(string kind, object data) => output.WriteLine(JsonSerializer.Serialize(new { task29 = true, kind, data }));
    private static CeraShopPurchaseInput Input(string mode = "success") => CeraShopTestContent.Input(mode is "action" or "rollback" ? [CeraShopTestContent.Entry(), CeraShopTestContent.Entry(105016)] : [CeraShopTestContent.Entry()]);
    private static void ConfigureFailure(CeraShopFixture f, string mode)
    {
        if (mode is "action" or "rollback")
        { f.Content.Products[110000] = f.Content.Products[110000] with { Price = new(100, 0, 3600, 0, []) }; f.Host.Sql("CREATE TRIGGER cera_flow_fail_pet BEFORE INSERT ON item_slots WHEN NEW.list_type=7 BEGIN SELECT RAISE(ABORT,'cera flow late pet'); END;"); }
        if (mode == "commit") f.Host.Sql("CREATE TABLE cera_flow_parent(id INTEGER PRIMARY KEY); CREATE TABLE cera_flow_child(id INTEGER PRIMARY KEY,parent_id INTEGER REFERENCES cera_flow_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER cera_flow_deferred AFTER INSERT ON item_slots BEGIN INSERT INTO cera_flow_child(parent_id) VALUES(987654321); END;");
    }
    private static CeraShopFixture Fixture(Trace t)
    {
        var f = new CeraShopFixture(configure: services =>
        {
            var domain = services.Last(x => x.ServiceType == typeof(ICeraShopDomainService));
            services.AddSingleton<ICeraShopDomainService>(p => new ObserveShop(Resolve<ICeraShopDomainService>(domain, p), p, t));
            var units = services.Last(x => x.ServiceType == typeof(ICommandWorkUnits));
            services.AddScoped<ICommandWorkUnits>(p => new ObserveUnits(Resolve<ICommandWorkUnits>(units, p), t));
            var whole = services.Last(x => x.ServiceType == typeof(ICommandHandler<GameCeraShopPurchaseCommand, CeraShopPurchaseResult>));
            services.AddTransient<ICommandHandler<GameCeraShopPurchaseCommand, CeraShopPurchaseResult>>(p => new ObserveWhole(Resolve<ICommandHandler<GameCeraShopPurchaseCommand, CeraShopPurchaseResult>>(whole, p), p, t));
            services.AddScoped(_ => new ScopeFailure(t));
            var selected = services.Last(x => x.ServiceType == typeof(ISelectedSessionDomainService));
            services.AddSingleton<ISelectedSessionDomainService>(p => QueryProxy<ISelectedSessionDomainService>.Make(Resolve<ISelectedSessionDomainService>(selected, p), t));
            var online = services.Last(x => x.ServiceType == typeof(IOnlineUserQueryDomainService));
            services.AddSingleton<IOnlineUserQueryDomainService>(p => QueryProxy<IOnlineUserQueryDomainService>.Make(Resolve<IOnlineUserQueryDomainService>(online, p), t));
        });
        t.Fixture = f; TestGameplay.Run(f.Host.Services, () => f.Host.Services.GetRequiredService<ICreatureRuntimeDomainService>().Enter(f.Host.Session)); t.Armed = true; return f;
    }
    private static T Resolve<T>(ServiceDescriptor d, IServiceProvider p) where T : class => (T)(d.ImplementationInstance ?? d.ImplementationFactory?.Invoke(p) ?? ActivatorUtilities.CreateInstance(p, d.ImplementationType!));
    private static void AssertHealthy(CeraShopFixture f) { Assert.False(f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Host.Connection)); Assert.False(f.Host.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Host.Session)!.Fenced); Assert.Empty(f.Host.Sender.Outbound.Closes); }
    private static bool Includes(Exception outer, Exception expected) => ReferenceEquals(outer, expected) || outer is AggregateException a && a.InnerExceptions.Any(x => Includes(x, expected)) || outer.InnerException is { } inner && Includes(inner, expected);
    private static void AssertNoLedger(CeraShopFixture f) { var table = typeof(Dfo.Server.Application.Effects.EffectOperationLedger).GetField("ledgers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Host.Services.GetRequiredService<Dfo.Server.Application.Effects.EffectOperationLedger>())!; object?[] args = [f.Host.Connection, null]; Assert.False((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!); }
    private static Task<string> Assets(CeraShopFixture f) => TestGameplay.RunAsync(f.Host.Services, async () => await Rows(f.Host.Services, default));
    private static ValueTask<string> Rows(IServiceProvider p, CancellationToken ct) => p.GetRequiredService<SqliteDatabase>().ReadAsync(async (cmd, token) =>
    {
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name"; var names = new List<string>(); await using (var r = await cmd.ExecuteReaderAsync(token)) while (await r.ReadAsync(token)) names.Add(r.GetString(0));
        var tables = new SortedDictionary<string, object>();
        foreach (var name in names) { cmd.CommandText = "SELECT * FROM \"" + name.Replace("\"", "\"\"") + "\""; await using var r = await cmd.ExecuteReaderAsync(token); var columns = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToArray(); var rows = new List<string>(); while (await r.ReadAsync(token)) rows.Add(JsonSerializer.Serialize(Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? null : r.GetValue(i) is byte[] blob ? "BLOB:" + Convert.ToHexString(blob) : r.GetValue(i)).ToArray())); rows.Sort(StringComparer.Ordinal); tables[name] = new { columns, rows }; }
        return JsonSerializer.Serialize(tables);
    }, ct);
    private static string[] Messages(CeraShopFixture f) => f.Host.Sender.Messages.Select(x => x.GetType().Name).ToArray();
    internal sealed class Trace(string mode)
    {
        internal readonly Exception IndependentFault = mode switch { "cancelled" => new OperationCanceledException("cera cancelled after real purchase"), "typed" => new TransactionOutcomeUncertainException(TransactionUncertaintyReason.RollbackFailure, "cera independent typed cause", new IOException("original typed inner")), _ => new IOException("cera existing request failure") }; internal CommandExecutionContext Context = null!;
        internal string Mode = mode; internal bool Armed, Receipt; internal int UnitCalls, ForeignKeys, Children; internal string? ResultKind, StagedAssets; internal Exception? Cause; internal readonly IOException CommitFault = new("cera flow nonfallible"); internal readonly IOException RollbackFault = new("cera flow rollback"); internal readonly IOException ScopeFault = new("cera flow whole scope"); internal readonly List<DomainTransaction> Transactions = []; internal string[] MessagesBeforeScope = []; internal Action? BeforeScopeFault; internal CeraShopFixture Fixture = null!;
    }
    private sealed class ObserveShop(ICeraShopDomainService actual, IServiceProvider p, Trace t) : ICeraShopDomainService
    {
        public async ValueTask<CeraShopPurchaseResult> PurchaseAsync(SelectedSessionIdentity s, CeraShopPurchaseInput input, DateTimeOffset now, Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken ct)
        {
            var tx = Assert.IsType<DomainTransaction>(p.GetRequiredService<IDomainTransactionAccessor>().Current); t.Transactions.Add(tx);
            if (t.Mode is "nonfallible" or "rollback") tx.Enlist(t, () => new Fault(t));
            try { var r = await actual.PurchaseAsync(s, input, now, footprint, ct); t.Receipt = r.Receipt is not null; if (t.Mode is "cancelled" or "typed") throw t.IndependentFault; if (t.Mode == "sticky") t.Context.Request!.Failed(t.IndependentFault); return r; }
            catch (Exception e) { t.Cause = e; throw; }
            finally { t.StagedAssets = await Rows(p, CancellationToken.None); t.ForeignKeys = await p.GetRequiredService<SqliteDatabase>().ReadAsync(async (cmd, token) => { cmd.CommandText = "PRAGMA foreign_keys"; return Convert.ToInt32(await cmd.ExecuteScalarAsync(token)); }, CancellationToken.None); if (t.Mode == "commit") t.Children = await p.GetRequiredService<SqliteDatabase>().ReadAsync(async (cmd, token) => { cmd.CommandText = "SELECT COUNT(*) FROM cera_flow_child"; return Convert.ToInt32(await cmd.ExecuteScalarAsync(token)); }, CancellationToken.None); }
        }
    }
    private sealed class ObserveUnits(ICommandWorkUnits actual, Trace t) : ICommandWorkUnits
    {
        public async ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CommandWorkUnit, CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull
        { var req = Assert.IsType<CommandRequestContext.ProtocolNonEffect>(c.Request); var actor = Assert.Single(d.Participants); Assert.Same(req.Dispatch.Connection, actor.Connection); Assert.Same(Assert.IsType<ProtocolFrameMetadata.Game>(req.Metadata).Value, actor.Metadata); Assert.Equal(CommandFootprintMode.NonRequest, d.Footprint); Assert.Equal(CommandWorkUnitKind.Ordinary, d.Kind); t.UnitCalls++; var result = await actual.WriteResultAsync(c, d, a, ct); t.ResultKind = result is CommandWriteResult<T>.CommitRolledBack rollback ? Capture(rollback.Cause) : "Returned"; return result; }
        private string Capture(Exception e) { t.Cause = e; return "CommitRolledBack"; }
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CommandWorkUnit, CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull => throw new InvalidOperationException("Cera preserves its bounded result entry.");
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull => throw new InvalidOperationException("Cera has one original writable boundary.");
    }
    private sealed class ObserveWhole(ICommandHandler<GameCeraShopPurchaseCommand, CeraShopPurchaseResult> actual, IServiceProvider p, Trace t) : ICommandHandler<GameCeraShopPurchaseCommand, CeraShopPurchaseResult>
    { public ValueTask<CeraShopPurchaseResult> HandleAsync(CommandExecutionContext c, GameCeraShopPurchaseCommand command, CancellationToken ct) { t.Context = c; if (t.Mode == "scope") { var fault = p.GetRequiredService<ScopeFailure>(); fault.Box = p.GetRequiredService<GameplayOutboxes>().Current; } return actual.HandleAsync(c, command, ct); } }
    private sealed class ScopeFailure(Trace t) : IAsyncDisposable
    {
        internal GameplayOutbox Box = null!;
        public ValueTask DisposeAsync()
        { var box = Box; var slots = (IEnumerable)typeof(GameplayOutbox).GetField("slots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(box)!; t.MessagesBeforeScope = slots.Cast<object>().Select(x => x.GetType().GetProperty("Entry")!.GetValue(x)).OfType<OutboundEntry>().SelectMany(x => x.Messages).Select(x => x.GetType().FullName!).ToArray(); t.BeforeScopeFault?.Invoke(); throw t.ScopeFault; }
    }
    private sealed class Fault(Trace t) : ITransactionParticipant
    { public bool CommitCanFail => false; public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask; public ValueTask CommitAsync(CancellationToken ct) { if (t.Mode == "nonfallible") throw t.CommitFault; return ValueTask.CompletedTask; } public ValueTask RollbackAsync(CancellationToken ct) { if (t.Mode == "rollback") throw t.RollbackFault; return ValueTask.CompletedTask; } }
    public class QueryProxy<T> : DispatchProxy where T : class
    {
        private T actual = null!; private Trace trace = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) { if (trace.Armed && trace.Mode == "no-session" && method!.Name == "FindByConnection") return null; if (trace.Armed && trace.Mode == "no-online" && method!.Name == "TryGetByConnection") { args![1] = null; return false; } try { return method!.Invoke(actual, args); } catch (TargetInvocationException e) when (e.InnerException is { } cause) { ExceptionDispatchInfo.Throw(cause); throw; } }
        private static T MakeCore(T actual, Trace t) { var result = Create<T, QueryProxy<T>>(); ((QueryProxy<T>)(object)result).actual = actual; ((QueryProxy<T>)(object)result).trace = t; return result; }
        internal static T Make(T actual, Trace t) => MakeCore(actual, t);
    }
}
