using System.Reflection;
using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notices;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Accounts;

// Actual decoder/dispatcher, domains, SQLite and production outbox. Only fault boundaries and
// physical transport are controlled. The same tests characterize the original fine route and whole route.
public sealed class SettingsProtocolCommandFlowTests
{
    private static readonly string[] Fine = ["SaveClientSettingsCommand", "SaveClientSettingsCommand", "SaveClientSettingsCommand", "SaveCharacterOptionCommand", "MarkNoticeSeenCommand", "MarkNoticeSeenCommand"];
    private static readonly string[] Whole = ["GameSaveGameOption1Command", "GameSaveHotkeysCommand", "GameSaveQuickChatCommand", "GameSaveCharacterOptionCommand", "GameNoticeSeenCommand", "GameNoticeSeenCommand"];
    private static readonly string[] Method = ["SaveMainAsync", "SaveHotkeysAsync", "SaveQuickChatAsync", "SaveAsync", "MarkSeenAsync", "MarkSeenAsync"];
    private static bool Modern => typeof(ProtocolModule).Assembly.GetTypes().Any(x => x.Name == Whole[0]);

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)]
    [InlineData(3, true)] [InlineData(4, false)] [InlineData(4, true)] [InlineData(5, true)]
    public async Task Registered_upload_commits_full_bytes_without_ACK_and_keeps_original_participants(int protocol, bool selected)
    {
        var trace = new Trace(); using var f = Create(protocol, trace, selected);
        await Send(f, protocol);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Transactions).Completion);
        await Changed(f, protocol); Declaration(f, trace, protocol, selected);
        Assert.Empty(f.Sender.Outbound.Frames);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)] [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)] [InlineData(3, false)] [InlineData(3, true)]
    [InlineData(4, false)] [InlineData(4, true)] [InlineData(5, false)] [InlineData(5, true)]
    public async Task Action_SQL_and_native_commit_errors_propagate_without_ACK_and_restore_all_saved_fields(int protocol, bool native)
    {
        var trace = new Trace(); using var f = Create(protocol, trace); var before = await Assets(f);
        f.Sql(native ? "CREATE TABLE task54_parent(id INTEGER PRIMARY KEY); CREATE TABLE task54_fault(id INTEGER REFERENCES task54_parent(id) DEFERRABLE INITIALLY DEFERRED)"
            : "CREATE TABLE task54_fault(id INTEGER); CREATE TRIGGER task54_abort BEFORE INSERT ON task54_fault BEGIN SELECT RAISE(ABORT,'TASK54 action'); END");
        trace.SqlFault = true;
        var failure = await Record.ExceptionAsync(() => Send(f, protocol).AsTask()); var tx = Assert.Single(trace.Transactions);
        var cause = Assert.IsType<SqliteException>(native ? tx.CommitFailure!.Cause : trace.Failure);
        Assert.Equal(native ? 787 : 1811, cause.SqliteExtendedErrorCode); Assert.Same(cause, failure);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, tx.Completion); Assert.Equal(before, await Assets(f));
        Assert.Empty(f.Sender.Outbound.Frames); Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Declaration(f, trace, protocol, true);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)]
    [InlineData(3, true)] [InlineData(4, true)] [InlineData(5, true)]
    public async Task Actual_scope_Dispose_after_commit_keeps_exact_protection_and_same_ID_replacement_isolation(int protocol, bool selected)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var trace = new Trace { ScopeFailure = new IOException("TASK54 actual DI Dispose") }; using var f = Create(protocol, trace, selected, delivery);
        var physical = delivery.Add(f.Connection); var replacement = f.Connection with { }; var next = delivery.Add(replacement);
        trace.AfterDomain = () => { f.Sender.Outbound.Connections.Remove(f.Connection.ConnectionId); f.Sender.Outbound.Connections.Add(replacement); };
        var failure = await Record.ExceptionAsync(() => Send(f, protocol).AsTask());
        if (Modern) Assert.Same(trace.ScopeFailure, Assert.IsType<TransactionOutcomeUncertainException>(failure).InnerException);
        else Assert.Same(trace.ScopeFailure, failure);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Transactions).Completion); await Changed(f, protocol);
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled();
        Assert.Equal(1, physical.CloseCalls); Assert.Empty(physical.Sends); Assert.Empty(next.Events); Assert.Equal(0, next.CloseCalls);
        Assert.Same(next, delivery.Resolve(replacement)); Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(replacement));
        var protect = Modern && selected && protocol < 4;
        Assert.Equal(protect, f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.Equal(protect, f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)?.Fenced ?? false);
        Assert.Equal(protect ? new[] { f.Session } : [], trace.Discarded);
        Declaration(f, trace, protocol, selected);
    }

    [Fact]
    public async Task Two_profiles_and_two_banks_are_distinct_full_blobs_and_repeated_reports_remain_no_ACK()
    {
        var trace = new Trace(); using var f = Create(1, trace);
        await Send(f, 1); await Send(f, 2, 0); await Send(f, 2, 1);
        Activate(f, 1); await Send(f, 1, 1);
        await Send(f, 0); await Send(f, 4); await Send(f, 4); await Send(f, 5); await Send(f, 5);
        var db = f.Services.GetRequiredService<SqliteDatabase>();
        Assert.Equal(new[] { Convert.ToHexString(Payload(1)), Convert.ToHexString(Payload(1, 1)) }, await db.ReadAllAsync("SELECT hex(payload) FROM account_hotkey_options ORDER BY key_type", _ => { }, r => r.GetString(0), default));
        Assert.Equal(new[] { Convert.ToHexString(Payload(2)), Convert.ToHexString(Payload(2, 1)) }, await db.ReadAllAsync("SELECT hex(quickchat_bank0) FROM account_settings UNION ALL SELECT hex(quickchat_bank1) FROM account_settings", _ => { }, r => r.GetString(0), default));
        Assert.Equal(new[] { "0|1|0|127", "1|1|1|127" }, await db.ReadAllAsync(
            "SELECT owner_type,owner_id,notice_group,notice_id FROM notice_states ORDER BY owner_type,notice_group,notice_id",
            _ => { }, row => $"{row.GetInt32(0)}|{row.GetInt32(1)}|{row.GetInt32(2)}|{row.GetInt32(3)}", default));
        Assert.Empty(f.Sender.Outbound.Frames);
    }

    [Fact]
    public async Task Unknown_profile_and_missing_character_return_before_any_transaction_or_frame()
    {
        var trace = new Trace(); using var f = Create(1, trace, false);
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<IClientSettingsDomainService>().ForgetConnection(f.Connection.ConnectionId));
        await Send(f, 1); await Send(f, 3); await Send(f, 5);
        Assert.Empty(trace.Transactions); Assert.Empty(trace.Units); Assert.Empty(f.Sender.Outbound.Frames);
    }

    [Theory] [InlineData(5)]
    public async Task Notice_secondary_keeps_its_inner_domain_gate_when_world_membership_is_lost(int protocol)
    {
        var trace = new Trace(); using var f = Create(protocol, trace);
        trace.BeforeDomain = () => f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(f.Session);
        var before = await Assets(f); await Send(f, protocol);
        Assert.Equal(before, await Assets(f)); Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Transactions).Completion);
        Declaration(f, trace, protocol, true); Assert.Empty(f.Sender.Outbound.Frames);
    }

    [Fact]
    public async Task Captured_profile_is_outside_transaction_and_token_change_prevents_overwrite()
    {
        var trace = new Trace(); using var f = Create(1, trace); var before = await Assets(f);
        trace.AfterCapture = () => Activate(f, 1);
        await Send(f, 1); Assert.True(trace.CaptureObserved); Assert.Equal(before, await Assets(f));
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Transactions).Completion); Assert.Empty(f.Sender.Outbound.Frames);
    }

    private static ShopFixture Create(int protocol, Trace trace, bool selected = true, RemainingOpeningProtocolCommandFlowTests.SenderProbe? delivery = null)
    {
        var f = new ShopFixture(s => {
            if (delivery is not null) s.AddSingleton<IOutboundSender>(delivery.Recorder);
            s.AddSingleton(p => new GameplayOutboxes(p.GetRequiredService<IOutboundSender>(), p.GetRequiredService<GameplayCloseCauses>(), p.GetRequiredService<ILogger<GameplayOutbox>>()));
            if (protocol < 3) DomainProbe.Add<IClientSettingsDomainService>(s, trace, Method[protocol]);
            else if (protocol == 3) DomainProbe.Add<ICharacterOptionsDomainService>(s, trace, Method[protocol]);
            else DomainProbe.Add<INoticeStateDomainService>(s, trace, Method[protocol]);
            var name = Modern ? Whole[protocol] : Fine[protocol]; var d = s.Last(x => x.ServiceType.IsGenericType && x.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) && x.ServiceType.GenericTypeArguments[0].Name == name);
            s.AddScoped(d.ServiceType, p => Activator.CreateInstance(typeof(ScopeProbe<,>).MakeGenericType(d.ServiceType.GenericTypeArguments), Actual(d, p), trace)!);
            var unit = s.Last(x => x.ServiceType == typeof(ICommandWorkUnits)); s.AddSingleton<ICommandWorkUnits>(p => new UnitProbe((ICommandWorkUnits)Actual(unit, p), trace));
            var creature = s.Last(x => x.ServiceType == typeof(ICreatureRuntimeDomainService)); s.AddSingleton<ICreatureRuntimeDomainService>(p => { var proxy = DispatchProxy.Create<ICreatureRuntimeDomainService, CreatureProbe>(); var spy = (CreatureProbe)(object)proxy; spy.Actual = Actual(creature, p); spy.Trace = trace; return proxy; });
        });
        f.Sql("INSERT INTO account_settings(account_id,hotkey_key_type,hotkey_slots) VALUES(1,7,X'3412'); INSERT INTO character_settings(character_id,user_state_bits) VALUES(1,3),(2,9)");
        if (!selected) TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(f.Session));
        Activate(f, 0); trace.Armed = true; return f;
    }
    private static void Activate(ShopFixture f, byte key) => TestGameplay.Run(f.Services, () => {
        var user = f.Services.GetRequiredService<IOnlineUserQueryDomainService>(); Assert.True(user.TryGetByConnection(f.Connection.ConnectionId, out var current));
        var origin = new ClientSettingsOrigin(current!.SessionIdentity, current.AccountId!.Value, current.Generation, f.Services.GetRequiredService<ISelectedSessionDomainService>().FindByConnection(f.Connection.ConnectionId));
        Assert.True(f.Services.GetRequiredService<IClientSettingsDomainService>().ActivateHotkeyProfile(origin, key));
    });
    private static object Actual(ServiceDescriptor d, IServiceProvider p) => d.ImplementationInstance ?? d.ImplementationFactory?.Invoke(p) ?? ActivatorUtilities.CreateInstance(p, d.ImplementationType!);
    private static ushort Wire(int p) => p switch { 0 => 0xC5, 1 => 0xC6, 2 => 0x170, 3 => 0x1C0, 4 => 0x1DE, _ => 0x1F8 };
    private static byte[] Payload(int p, byte variant = 0) => Enumerable.Range(0, p == 0 ? 150 : p == 1 ? 257 : p == 2 ? 300 : 512).Select(i => (byte)(i + variant)).ToArray();
    private static byte[] Body(int p, byte variant = 0) => p switch { 0 or 1 => [.. BitConverter.GetBytes(Payload(p, variant).Length), .. Payload(p, variant)], 2 => [variant, .. BitConverter.GetBytes(300), .. Payload(p, variant)], 3 => [.. BitConverter.GetBytes(512), .. Payload(p, variant)], _ => BitConverter.GetBytes(127) };
    private static ValueTask Send(ShopFixture f, int p, byte variant = 0) => EffectRequestTransport.SendRaw(f, new(1, Wire(p), 0, 7), Body(p, variant));
    private static async Task Changed(ShopFixture f, int p) {
        var db = f.Services.GetRequiredService<SqliteDatabase>();
        var sql = p switch { 0 => "SELECT hex(main_game_option) FROM account_settings WHERE account_id=1", 1 => "SELECT hex(payload) FROM account_hotkey_options WHERE account_id=1 AND key_type=0", 2 => "SELECT hex(quickchat_bank0) FROM account_settings WHERE account_id=1", 3 => "SELECT hex(character_option_blob) FROM character_settings WHERE character_id=1", _ => $"SELECT hex(X'7F') FROM notice_states WHERE owner_type={p - 4} AND owner_id=1 AND notice_group={p - 4} AND notice_id=127" };
        var expected = p == 3 ? Body(p) : p < 3 ? Payload(p) : new byte[] { 127 };
        Assert.Equal(Convert.ToHexString(expected), Assert.Single(await db.ReadAllAsync(sql, _ => { }, r => r.GetString(0), default)));
        if (p >= 4) Assert.Equal(1, f.Scalar("SELECT count(*) FROM notice_states"));
        Assert.Equal(7, f.Scalar("SELECT hotkey_key_type FROM account_settings WHERE account_id=1")); Assert.Equal(3, f.Scalar("SELECT user_state_bits FROM character_settings WHERE character_id=1"));
        Assert.Equal(0, f.Scalar("SELECT count(*) FROM account_settings WHERE account_id<>1")); Assert.Equal(0, f.Scalar("SELECT count(*) FROM notice_states WHERE owner_id<>1"));
    }
    private static async Task<string> Assets(ShopFixture f) {
        var db = f.Services.GetRequiredService<SqliteDatabase>(); var rows = new List<string>();
        foreach (var table in new[] { "account_settings", "account_hotkey_options", "character_settings", "notice_states", "domain_events" })
            rows.AddRange(await db.ReadAllAsync("SELECT * FROM " + table + " ORDER BY rowid", _ => { }, r => string.Join('|', Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "NULL" : r.GetValue(i) is byte[] bytes ? Convert.ToHexString(bytes) : r.GetValue(i).ToString())), default));
        return string.Join('\n', rows);
    }
    private static void Declaration(ShopFixture f, Trace trace, int protocol, bool selected) {
        if (!Modern) return; var d = Assert.Single(trace.Units); Assert.Equal(CommandWorkUnitKind.Ordinary, d.Kind); Assert.Equal(CommandFootprintMode.None, d.Footprint);
        if (selected && protocol < 4) { var actor = Assert.Single(d.Participants); Assert.Equal(f.Session, actor.Session); Assert.Same(f.Connection, actor.Connection); Assert.Equal(Wire(protocol), actor.Metadata!.Type); }
        else Assert.Empty(d.Participants);
    }
    public sealed class Trace { internal bool Armed, SqlFault, CaptureObserved; internal Exception? Failure, ScopeFailure; internal Action? BeforeDomain, AfterDomain, AfterCapture; internal List<DomainTransaction> Transactions = []; internal List<CommandWorkUnitDeclaration> Units = []; internal List<SelectedSessionIdentity> Discarded = []; }
    public class DomainProbe : DispatchProxy {
        internal object Actual = null!; internal IServiceProvider Services = null!; internal Trace Trace = null!; internal string Method = "";
        protected override object? Invoke(MethodInfo? method, object?[]? args) {
            if (Trace.Armed && method!.Name == "CaptureHotkeyUpload") { Assert.Null(Services.GetRequiredService<IDomainTransactionAccessor>().Current); var result = Call(method, args); Trace.CaptureObserved = true; Trace.AfterCapture?.Invoke(); return result; }
            return method!.Name != Method || !Trace.Armed ? Call(method, args) : Observe(method, args!);
        }
        private object? Call(MethodInfo method, object?[]? args) { try { return method.Invoke(Actual, args); } catch (TargetInvocationException ex) when (ex.InnerException is { } cause) { ExceptionDispatchInfo.Capture(cause).Throw(); throw; } }
        private async ValueTask<bool> Observe(MethodInfo method, object?[] args) {
            Trace.Transactions.Add((DomainTransaction)Services.GetRequiredService<IDomainTransactionAccessor>().Current!);
            try { Trace.BeforeDomain?.Invoke(); var result = await (ValueTask<bool>)Call(method, args)!; if (Trace.SqlFault) await Services.GetRequiredService<SqliteDatabase>().ExecuteAsync("INSERT INTO task54_fault VALUES(404)", _ => { }, default); Trace.AfterDomain?.Invoke(); return result; }
            catch (Exception failure) { Trace.Failure = failure; throw; }
        }
        internal static void Add<T>(IServiceCollection s, Trace trace, string method) where T : class { var d = s.Last(x => x.ServiceType == typeof(T)); s.AddSingleton<T>(p => { var proxy = Create<T, DomainProbe>(); var spy = (DomainProbe)(object)proxy; spy.Actual = SettingsProtocolCommandFlowTests.Actual(d, p); spy.Services = p; spy.Trace = trace; spy.Method = method; return proxy; }); }
    }
    public sealed class ScopeProbe<T, R>(object actual, Trace trace) : ICommandHandler<T, R>, IDisposable where T : class, ICommand<R> where R : notnull { public ValueTask<R> HandleAsync(CommandExecutionContext c, T q, CancellationToken ct) => ((ICommandHandler<T, R>)actual).HandleAsync(c, q, ct); public void Dispose() { if (trace.ScopeFailure is { } cause) throw cause; } }
    public class CreatureProbe : DispatchProxy { internal object Actual = null!; internal Trace Trace = null!; protected override object? Invoke(MethodInfo? method, object?[]? args) { if (method!.Name == "DiscardExact") Trace.Discarded.Add((SelectedSessionIdentity)args![0]!); try { return method.Invoke(Actual, args); } catch (TargetInvocationException ex) when (ex.InnerException is { } cause) { ExceptionDispatchInfo.Capture(cause).Throw(); throw; } } }
    private sealed class UnitProbe(ICommandWorkUnits actual, Trace trace) : ICommandWorkUnits {
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CommandWorkUnit, CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull { trace.Units.Add(d); return actual.WriteAsync(c, d, a, ct); }
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CommandWorkUnit, CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
    }
}
