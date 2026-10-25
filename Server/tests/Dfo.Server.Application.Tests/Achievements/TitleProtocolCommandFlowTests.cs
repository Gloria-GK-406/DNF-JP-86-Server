using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Achievements;

// Registered decoder/dispatcher, actual title/achievement/attendance domains and SQLite,
// production registration/encoders. Content inputs and physical transport are controlled; QueryRows
// substitutes the broken readonly title projection only for protocol gates, and EventView keeps the
// actual attendance read while omitting other events. The Host suite exercises the full event projection.
public sealed class TitleProtocolCommandFlowTests(ITestOutputHelper output)
{
    private static readonly string[] Fine = ["StoreTitleCommand", "WithdrawTitleCommand", "EquipCarriedTitleReplacingMirrorCommand", "QueryTitleBookCommand", "SetTitleAppearanceCommand", "ReconcileAchievementCommand", "ClaimWeeklyAttendanceCommand"];
    private static readonly string[] Whole = ["GameTitleBookMutationCommand", "GameTitleBookMutationCommand", "GameTitleBookMutationCommand", "GameQueryTitleBookCommand", "GameSetTitleAppearanceCommand", "GameAchievementTriggerCommand", "GameWeeklyAttendanceCommand"];
    private static readonly string[] Methods = ["StoreAsync", "WithdrawAsync", "EquipCarriedTitleReplacingMirrorAsync", "GetAsync", "SetAppearanceAsync", "ReconcileAsync", "ClaimAsync"];
    private static bool Modern => typeof(ProtocolModule).Assembly.GetTypes().Any(x => x.Name == Whole[0]);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public async Task Registered_wire_preserves_real_state_native_body_and_exact_transaction_participant(int protocol)
    {
        var trace = new Trace(); using var f = await Create(protocol, trace);
        await Send(f, protocol);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Transactions).Completion);
        AssertChange(f, trace, protocol);
        var frames = f.Sender.Outbound.Frames;
        Assert.Equal(ExpectedFrames(protocol), frames.Select(Type));
        if (protocol < 3) Assert.Equal(MutationBody(true, protocol == 0 ? 0 : 3, protocol == 0 ? 9 : 13), frames[0][15..]);
        if (protocol == 4) Assert.Equal(new byte[] { 1, 4, 104, 0, 0 }, frames[0][15..]);
        if (protocol == 5) Assert.Equal(new byte[] { 1, 41, 35, 0, 0, 30, 0, 0, 0, 0, 0 }, frames[0][15..]);
        if (protocol == 6) Assert.Equal(new byte[4], frames[0][15..]);
        if (protocol == 3) {
            Assert.All(frames, x => { Assert.Equal((byte)1, x[15]); Assert.Equal((ushort)3, BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(16))); });
            Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(frames[4].AsSpan(22)));
        }
        Declaration(f, trace, protocol);
        output.WriteLine($"protocol={Fine[protocol]} modern={Modern} frames={string.Join(',', frames.Select(Type))}");
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)] [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)] [InlineData(4, false)] [InlineData(4, true)]
    [InlineData(5, false)] [InlineData(5, true)] [InlineData(6, false)] [InlineData(6, true)]
    public async Task Actual_SQL_action_and_native_commit_rollback_keep_original_refusal_policy(int protocol, bool native)
    {
        var trace = new Trace(); using var f = await Create(protocol, trace); var before = Assets(f);
        f.Sql(native
            ? "CREATE TABLE task53_parent(id INTEGER PRIMARY KEY); CREATE TABLE task53_fault(id INTEGER REFERENCES task53_parent(id) DEFERRABLE INITIALLY DEFERRED)"
            : "CREATE TABLE task53_fault(id INTEGER); CREATE TRIGGER task53_abort BEFORE INSERT ON task53_fault BEGIN SELECT RAISE(ABORT,'TASK53 action'); END");
        trace.SqlFault = true;
        var failure = await Record.ExceptionAsync(() => Send(f, protocol).AsTask());
        var tx = Assert.Single(trace.Transactions);
        var cause = Assert.IsType<SqliteException>(native ? tx.CommitFailure!.Cause : trace.Failure);
        Assert.Equal(native ? 787 : 1811, cause.SqliteExtendedErrorCode);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, tx.Completion); Assert.Equal(before, Assets(f));
        if (protocol is < 3 or 5)
        {
            Assert.Null(failure);
            var body = Assert.Single(f.Sender.Outbound.Frames)[15..];
            Assert.Equal(protocol == 5 ? new byte[] { 0, 1, 41, 35, 0, 0 } : MutationBody(false, protocol == 0 ? 0 : 3, protocol == 0 ? 9 : 13), body);
        }
        else { Assert.Same(cause, failure); Assert.Empty(f.Sender.Outbound.Frames); }
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Declaration(f, trace, protocol);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)] [InlineData(5, false)] [InlineData(5, true)]
    public async Task Cancellation_and_recursive_typed_failure_are_never_refusal_ACKs(int protocol, bool typed)
    {
        Exception cause = typed ? new AggregateException(new IOException("outer"), new TransactionOutcomeUncertainException(TransactionUncertaintyReason.RollbackFailure, "TASK53 typed", new IOException("inner"))) : new OperationCanceledException("TASK53 cancel");
        var trace = new Trace { Inject = cause }; using var f = await Create(protocol, trace); var before = Assets(f);
        Assert.Same(cause, await Record.ExceptionAsync(() => Send(f, protocol).AsTask()));
        Assert.Equal(before, Assets(f)); Assert.Empty(f.Sender.Outbound.Frames);
        Assert.Equal(typed ? DomainTransactionCompletion.Uncertain : DomainTransactionCompletion.ConfirmedRollback, Assert.Single(trace.Transactions).Completion);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public async Task Actual_DI_scope_failure_keeps_commit_and_protects_only_the_original_requester(int protocol)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var trace = new Trace { ScopeFailure = new IOException("TASK53 scope Dispose") }; using var f = await Create(protocol, trace, delivery);
        var physical = delivery.Add(f.Connection);
        var failure = await Record.ExceptionAsync(() => Send(f, protocol).AsTask());
        // Legacy title/achievement handlers caught their fine-command scope error; others propagated it.
        if (!Modern && protocol is < 3 or 5) Assert.Null(failure);
        else if (Modern && protocol != 3) Assert.Same(trace.ScopeFailure, Assert.IsType<TransactionOutcomeUncertainException>(failure).InnerException);
        else Assert.Same(trace.ScopeFailure, failure);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Transactions).Completion); AssertChange(f, trace, protocol);
        if (Modern || protocol is 3 or 4 or 6)
        { await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled(); Assert.Equal(1, physical.CloseCalls); }
        else { await delivery.Settled(); Assert.Equal(0, physical.CloseCalls); }
        Assert.Equal(Modern && protocol != 3, f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.Equal(Modern && protocol != 3, f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
        Assert.Equal(Modern && protocol != 3 ? new[] { f.Session } : [], trace.Discarded);
        Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(trace.Target!)!.Fenced);
        var frames = Split(physical.Sends).ToArray();
        if (Modern) Assert.Equal(ExpectedFrames(protocol), frames.Select(Type));
        else if (protocol is < 3 or 5) Assert.Equal((byte)0, Assert.Single(frames)[15]);
        else Assert.Empty(frames);
        Declaration(f, trace, protocol);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public async Task First_actual_encoder_failure_and_same_ID_physical_replacement_keep_original_isolation(int protocol)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var trace = new Trace(); using var f = await Create(protocol, trace, delivery); var physical = delivery.Add(f.Connection);
        var replacement = f.Connection with { }; var next = delivery.Add(replacement); var cause = new IOException("TASK53 actual encoder"); var encoded = new List<string>();
        f.Sender.Outbound.Encoder.Fail((target, message) => { if (encoded.Count == 0) { Assert.Same(f.Connection, target); f.Sender.Outbound.Connections.Remove(target.ConnectionId); f.Sender.Outbound.Connections.Add(replacement); } encoded.Add(message.GetType().Name); return cause; });
        var failure = await Record.ExceptionAsync(() => Send(f, protocol).AsTask());
        // Notification projection reports Failed. Weekly deliberately ignores both results.
        if (protocol == 6) { Assert.Null(failure); Assert.Equal(new[] { "WeeklyAttendanceResponse", "EventInformationMessage" }, encoded); }
        else if (Modern && protocol != 3) Assert.Same(cause, Assert.IsType<TransactionOutcomeUncertainException>(failure).InnerException);
        else Assert.Same(cause, failure);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Transactions).Completion); AssertChange(f, trace, protocol);
        if (protocol != 6) { await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled(); Assert.Equal(1, physical.CloseCalls); }
        Assert.Empty(physical.Sends); Assert.Equal(0, next.CloseCalls); Assert.Empty(next.Events); Assert.Same(next, delivery.Resolve(replacement));
        Assert.Equal(Modern && protocol is not (3 or 6), f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(replacement));
        Assert.Equal(Modern && protocol is not (3 or 6) ? new[] { f.Session } : [], trace.Discarded);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Slot_mapping_before_transaction_is_refused_but_result_mapping_is_never_swallowed(bool resultMapping)
    {
        var cause = new IOException("TASK53 slot address mapping"); var trace = new Trace { MappingFailure = cause, ResultMapping = resultMapping }; using var f = await Create(0, trace);
        var before = Assets(f); var failure = await Record.ExceptionAsync(() => Send(f, 0).AsTask());
        if (!resultMapping) { Assert.Null(failure); Assert.Empty(trace.Transactions); Assert.Equal(before, Assets(f)); Assert.Equal(MutationBody(false, 0, 9), Assert.Single(f.Sender.Outbound.Frames)[15..]); }
        else {
            if (Modern) Assert.Same(cause, Assert.IsType<TransactionOutcomeUncertainException>(failure).InnerException); else Assert.Same(cause, failure);
            Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Transactions).Completion); AssertChange(f, trace, 0); Assert.Empty(f.Sender.Outbound.Frames);
            Assert.Equal(Modern, f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Equal(Modern ? new[] { f.Session } : [], trace.Discarded);
        }
    }

    [Fact]
    public async Task Query_listener_mismatch_is_silent_before_opening_the_readonly_unit()
    {
        var trace = new Trace(); using var f = await Create(3, trace);
        TestGameplay.Run(f.Services, () => { var sessions = f.Services.GetRequiredService<ISelectedSessionDomainService>(); var target = trace.Target! with { ListenerPort = 10012 }; sessions.Select(target); return true; });
        await Send(f, 3); Assert.Empty(trace.Transactions); Assert.Empty(f.Sender.Outbound.Frames);
    }

    [Fact]
    public async Task Attendance_marks_and_mails_atomically_and_repeated_claim_never_creates_another_mail()
    {
        var trace = new Trace(); using var f = await Create(6, trace);
        await Send(f, 6); await Send(f, 6);
        Assert.Equal(new uint[] { 0, 1 }, f.Sender.Outbound.Frames.Where(x => Type(x) == 0x441).Select(x => BinaryPrimitives.ReadUInt32LittleEndian(x.AsSpan(15))));
        var mail = Assert.Single(await f.Services.GetRequiredService<IMailboxDomainService>().InboxAsync(new(1)));
        var reward = Assert.IsType<MailAsset.Stackable>(Assert.Single(mail.Attachments).Asset); Assert.Equal(2000, reward.ItemId); Assert.Equal(3, reward.Count);
        Assert.Equal(1, f.Scalar("SELECT attendance_no FROM event_attendance_week")); Assert.Equal(0, f.Count(2000));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Query_requires_target_listener_and_rechecks_both_world_sessions_before_delivery(bool requester)
    {
        var trace = new Trace(); using var f = await Create(3, trace);
        Action release = () => f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(requester ? f.Session : trace.Target!);
        if (requester) trace.AfterUnit = release; else trace.AfterDomain = release;
        await Send(f, 3); Assert.Empty(f.Sender.Outbound.Frames); Assert.Single(trace.Transactions); Declaration(f, trace, 3);
    }

    [Fact]
    public async Task Production_title_query_keeps_the_existing_readonly_lifecycle_rejection_without_frames()
    {
        var trace = new Trace { ActualQuery = true }; using var f = await Create(3, trace);
        var failure = Assert.IsType<InvalidOperationException>(await Record.ExceptionAsync(() => Send(f, 3).AsTask()));
        Assert.Equal("A read-only query cannot enlist a writable state participant.", failure.Message);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, Assert.Single(trace.Transactions).Completion);
        Assert.Empty(f.Sender.Outbound.Frames); Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Declaration(f, trace, 3);
    }

    [Fact]
    public async Task Incomplete_achievement_still_ACKs_ID_and_title_mapping_errors_keep_native_coordinates()
    {
        var trace = new Trace(); using var f = await Create(5, trace);
        await EffectRequestTransport.SendRaw(f, new(1, 0x1A1, 0, 7), BitConverter.GetBytes(9001));
        Assert.Equal(new byte[] { 0, 1, 41, 35, 0, 0 }, Assert.Single(f.Sender.Outbound.Frames)[15..]); Assert.Empty(trace.Transactions);
        await EffectRequestTransport.SendRaw(f, new(1, 0x19D, 0, 8), Ints(0x314D4254, int.MaxValue, 26596, 4, 10));
        Assert.Equal(MutationBody(false, 3, 13), f.Sender.Outbound.Frames.Last()[15..]); Assert.Empty(trace.Transactions);
    }

    private static async Task<ShopFixture> Create(int protocol, Trace trace, RemainingOpeningProtocolCommandFlowTests.SenderProbe? delivery = null)
    {
        var content = new Content();
        var f = new ShopFixture(s => {
            s.AddSingleton<IInventorySlotMappingRuleService>(new MappingProbe(trace));
            s.AddSingleton<ITitleBookDefinitionSource>(content); s.AddSingleton<IAchievementDefinitionSource>(content);
            s.AddSingleton<IItemPlacementDefinitionSource>(content); s.AddSingleton<IEquipmentSlotDefinitionSource>(content); s.AddSingleton<IEquipmentRequirementDefinitionSource>(content);
            s.AddSingleton<IAchievementCharacterFactsDomainService>(content); s.AddSingleton<IWeeklyAttendanceRuleService>(content); s.AddSingleton<IEventPlayerProjectionDomainService, EventView>();
            if (delivery is not null) s.AddSingleton<IOutboundSender>(delivery.Recorder);
            s.AddSingleton(p => new GameplayOutboxes(p.GetRequiredService<IOutboundSender>(), p.GetRequiredService<GameplayCloseCauses>(), p.GetRequiredService<ILogger<GameplayOutbox>>()));
            if (protocol < 5) DomainProbe.Add<ITitleBookDomainService>(s, trace, Methods[protocol]);
            else if (protocol == 5) DomainProbe.Add<IAchievementDomainService>(s, trace, Methods[protocol]);
            else DomainProbe.Add<IWeeklyAttendanceDomainService>(s, trace, Methods[protocol]);
            var name = Modern ? Whole[protocol] : Fine[protocol]; var d = s.Last(x => x.ServiceType.IsGenericType && x.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) && x.ServiceType.GenericTypeArguments[0].Name == name);
            s.AddScoped(d.ServiceType, p => Activator.CreateInstance(typeof(ScopeProbe<,>).MakeGenericType(d.ServiceType.GenericTypeArguments), Actual(d, p), trace)!);
            var units = s.Last(x => x.ServiceType == typeof(ICommandWorkUnits)); s.AddSingleton<ICommandWorkUnits>(p => new UnitProbe((ICommandWorkUnits)Actual(units, p), trace));
            var creature = s.Last(x => x.ServiceType == typeof(ICreatureRuntimeDomainService));
            s.AddSingleton<ICreatureRuntimeDomainService>(p => { var proxy = DispatchProxy.Create<ICreatureRuntimeDomainService, CreatureProbe>(); var spy = (CreatureProbe)(object)proxy; spy.Actual = Actual(creature, p); spy.Trace = trace; return proxy; });
        });
        f.SetLevel(20);
        trace.Target = TestGameplay.Run(f.Services, () => { var registry = f.Services.GetRequiredService<Dfo.Server.Application.Users.OnlineStateRegistry>();
            var connection = new NetworkConnectionContext(new("task53-target"), NetworkEndpointKind.Game, 10011); var user = registry.RegisterConnection(connection);
            registry.Authenticate(user.OnlineUserId, new(2)); var target = SelectedSessionIdentity.Create(new(2), new(3), connection.ConnectionId, registry.GetRequiredUser(user.OnlineUserId).Generation, 10011); registry.Select(target); registry.EnterWorld(target); return target; });
        f.Sql("INSERT INTO accounts(account_id,m_id) VALUES(2,'title-target'); INSERT INTO characters(character_id,account_id,name,job,level,slot_index) VALUES(3,2,'target',12,20,0)");
        await TestGameplay.RunAsync(f.Services, async () => {
            await using var tx = f.Services.GetRequiredService<DomainTransactionScope>().Begin(); var store = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(tx.Transaction);
            var original = new ItemSnapshot { ItemId = 26596, LegacyKind = 1, Type = ItemTraits.Equipment, Count = 1, Attribute = 12, Durability = 47, SealFlag = 1, EnchantCardId = 123, AmplifyType = 3, AmplifyValue = 91 };
            var title = await store.CreateAsync(original, default); trace.Original = title.Snapshot;
            var book = protocol != 0;
            await store.PutAsync(ContainerId.ForCharacter(new(1), book ? InventoryContainerType.TitleBookEvent : InventoryContainerType.Main), book ? 10 : 9, title, default);
            if (protocol == 3) await store.PutAsync(ContainerId.ForCharacter(new(3), InventoryContainerType.TitleBookEvent), 10, await store.CreateAsync(original with { ItemUid = null }, default), default);
            if (protocol is 2 or 4) {
                var mirror = await store.CreateAsync(title.Snapshot with { ItemUid = null, SealFlag = 0, IsMirror = true, MirrorSourceUid = title.ItemUid }, default);
                await store.PutAsync(ContainerId.ForCharacter(new(1), InventoryContainerType.Equipment), 13, mirror, default);
                await store.PutAsync(ContainerId.ForCharacter(new(1), InventoryContainerType.Main), 9, await store.CreateAsync(original with { ItemUid = null, ItemId = 26628 }, default), default);
            }
            if (protocol == 6) {
                var persistence = f.Services.GetRequiredService<IEventPersistence>(); var now = f.Clock.GetUtcNow();
                Assert.True(await persistence.SaveAsync(2208, 0, new(1, "task53", true, "签到", "签到", "", "", "", now.AddDays(-1), now.AddDays(30)), default));
                Assert.True(await f.Services.GetRequiredService<IWeeklyAttendanceDomainService>().RecordClearAsync(new(1), Guid.NewGuid(), 1, 20, now, default));
            }
            await tx.CommitAsync(default);
        });
        trace.Transactions.Clear(); trace.Armed = true; return f;
    }

    private static object Actual(ServiceDescriptor d, IServiceProvider p) => d.ImplementationInstance ?? d.ImplementationFactory?.Invoke(p) ?? ActivatorUtilities.CreateInstance(p, d.ImplementationType!);
    private static ushort Wire(int p) => p switch { 0 => 0x19C, 1 or 2 => 0x19D, 3 => 0x1A8, 4 => 0x239, 5 => 0x1A1, _ => 0x441 };
    private static ValueTask Send(ShopFixture f, int p) => EffectRequestTransport.SendRaw(f, new(1, Wire(p), 0, 7), p switch {
        0 => Ints(0, 9, 26596, 4, 10), 1 => Ints(3, 13, 26596, 4, 10), 2 => Ints(0x314D4254, 9, 26628, 4, 10),
        3 => BitConverter.GetBytes((ushort)3), 4 => BitConverter.GetBytes(26628), 5 => [.. BitConverter.GetBytes(9001), 255, 255, 255, 255, 255, 255], _ => new byte[14] });
    private static byte[] Ints(params int[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();
    private static byte[] MutationBody(bool success, int space, int slot) => success ? [(byte)1, .. Ints(space, slot, 4, 10)] : [(byte)0, (byte)10, .. Ints(space, 4)];
    private static ushort[] ExpectedFrames(int p) => p == 6 ? [0x441, 0x6C, 0x42D] : p == 3 ? [0x166, 0x166, 0x166, 0x166, 0x166] : [Wire(p)];
    private static ItemSnapshot? ReadItem(ShopFixture f, InventoryContainerType type, int slot) => f.InTransaction(() => f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Services.GetRequiredService<IDomainTransactionAccessor>().Current!).GetAsync(ContainerId.ForCharacter(new(1), type), slot, default).AsTask().GetAwaiter().GetResult()?.Snapshot);
    private static ushort Type(byte[] frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1));
    private static string Assets(ShopFixture f) => System.Text.Json.JsonSerializer.Serialize(new[] { ReadItem(f, InventoryContainerType.Main, 9), ReadItem(f, InventoryContainerType.TitleBookEvent, 10), ReadItem(f, InventoryContainerType.Equipment, 13) }) + string.Join('|', f.Scalar("SELECT count(*) FROM items"), f.Scalar("SELECT COALESCE(sum(list_type*100+slot_index),0) FROM item_slots"), f.Scalar("SELECT count(*) FROM domain_events"), f.Scalar("SELECT count(*) FROM character_achievement_complete"), f.Scalar("SELECT COALESCE(sum(attendance_no),0) FROM event_attendance_week"), f.Scalar("SELECT count(*) FROM mailbox_messages"));
    private static void AssertChange(ShopFixture f, Trace trace, int p) {
        if (p < 3) {
            var original = trace.Original!;
            var book = ReadItem(f, InventoryContainerType.TitleBookEvent, 10)!;
            Assert.Equal(p == 1 ? original with { SealFlag = 0 } : original, book);
            if (p == 1) {
                var mirror = ReadItem(f, InventoryContainerType.Equipment, 13)!;
                Assert.NotEqual(original.ItemUid, mirror.ItemUid);
                Assert.Equal(original with { ItemUid = mirror.ItemUid, SealFlag = 0, IsMirror = true, MirrorSourceUid = original.ItemUid }, mirror);
            }
            if (p == 2) {
                var worn = ReadItem(f, InventoryContainerType.Equipment, 13)!;
                Assert.False(worn.IsMirror); Assert.Equal(26628, worn.ItemId); Assert.Equal((byte)0, worn.SealFlag);
                Assert.Null(ReadItem(f, InventoryContainerType.Main, 9));
                Assert.Equal(12, worn.Attribute); Assert.Equal(47, worn.Durability); Assert.Equal(123, worn.EnchantCardId); Assert.Equal(91, worn.AmplifyValue);
            }
        }
        if (p == 0) Assert.Equal(1, f.Scalar("SELECT count(*) FROM item_slots WHERE owner_id=1 AND list_type=23 AND slot_index=10"));
        if (p is 1 or 2) Assert.Equal(1, f.Scalar("SELECT count(*) FROM item_slots WHERE owner_id=1 AND list_type=3 AND slot_index=13"));
        if (p == 4) Assert.Equal(26628, ReadItem(f, InventoryContainerType.Equipment, 13)!.AppearanceItemId);
        if (p == 5) Assert.Equal(30, f.Scalar("SELECT p1 FROM character_achievement_complete WHERE achievement_id=9001"));
        if (p == 6) { Assert.Equal(1, f.Scalar("SELECT attendance_no FROM event_attendance_week")); Assert.Equal(1, f.Scalar("SELECT count(*) FROM mailbox_messages")); }
    }
    private static void Declaration(ShopFixture f, Trace trace, int p) {
        if (!Modern) return; var (readOnly, d) = Assert.Single(trace.Units); Assert.Equal(p == 3, readOnly); Assert.Equal(CommandWorkUnitKind.Ordinary, d.Kind); Assert.Equal(CommandFootprintMode.None, d.Footprint);
        var actor = Assert.Single(d.Participants); Assert.Equal(f.Session, actor.Session); Assert.Same(f.Connection, actor.Connection); Assert.Equal(Wire(p), actor.Metadata!.Type);
    }
    private static IEnumerable<byte[]> Split(IEnumerable<byte[]> sends) { foreach (var bytes in sends) for (var at = 0; at < bytes.Length;) { var n = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 3)); yield return bytes[at..(at + n)]; at += n; } }

    public sealed class Trace { internal bool Armed, SqlFault, ActualQuery, ResultMapping, DomainReturned; internal Exception? Inject, Failure, ScopeFailure, MappingFailure; internal Action? AfterDomain, AfterUnit; internal ItemSnapshot? Original; internal SelectedSessionIdentity? Target; internal List<DomainTransaction> Transactions = []; internal List<SelectedSessionIdentity> Discarded = []; internal List<(bool, CommandWorkUnitDeclaration)> Units = []; }
    public class DomainProbe : DispatchProxy {
        internal object Actual = null!; internal IServiceProvider Services = null!; internal Trace Trace = null!; internal string Method = "";
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name != Method || !Trace.Armed ? Call(method, args) : typeof(DomainProbe).GetMethod(nameof(Observe), BindingFlags.NonPublic | BindingFlags.Instance)!.MakeGenericMethod(method.ReturnType.GenericTypeArguments[0]).Invoke(this, [method, args]);
        private object? Call(MethodInfo method, object?[]? args) { try { return method.Invoke(Actual, args); } catch (TargetInvocationException ex) when (ex.InnerException is { } cause) { ExceptionDispatchInfo.Capture(cause).Throw(); throw; } }
        private async ValueTask<T> Observe<T>(MethodInfo method, object?[] args) {
            Trace.Transactions.Add((DomainTransaction)Services.GetRequiredService<IDomainTransactionAccessor>().Current!);
            try {
                if (Trace.Inject is { } cause) throw cause;
                var result = Method == "GetAsync" && !Trace.ActualQuery
                    ? (T)(object)await QueryRows((CharacterId)args[0]!) : await (ValueTask<T>)Call(method, args)!;
                if (Trace.SqlFault) await Services.GetRequiredService<SqliteDatabase>().ExecuteAsync("INSERT INTO task53_fault VALUES(404)", _ => { }, default);
                Trace.DomainReturned = true; Trace.AfterDomain?.Invoke(); return result;
            } catch (Exception failure) { Trace.Failure = failure; throw; }
        }
        private async ValueTask<IReadOnlyList<CharacterTitleBookCategoryState>> QueryRows(CharacterId character) {
            // Read-only projection seam: production GetAsync currently enlists a writable lifecycle participant.
            // This alternate reader tests protocol listener/current/participant gates, not that broken domain path.
            var rows = await Services.GetRequiredService<ITitleBookPersistence>().ReadTitlesAsync(character, default);
            return Enumerable.Range(0, 5).Select(c => new CharacterTitleBookCategoryState((byte)c, 0, 0,
                rows.Where(x => x.Category == c).Select(x => new CharacterTitleBookEntryState((ushort)x.Index, x.Item.ItemId, x.Item.InstanceValue, x.Item.Attribute, x.Item.Durability, x.Item.SealFlag, x.Item.EnchantCardId, x.Item.EnchantUpgradeCount, x.Item.AmplifyType, x.Item.AmplifyValue)).ToArray())).ToArray();
        }
        internal static void Add<T>(IServiceCollection s, Trace trace, string method) where T : class { var d = s.Last(x => x.ServiceType == typeof(T)); s.AddSingleton<T>(p => { var proxy = Create<T, DomainProbe>(); var spy = (DomainProbe)(object)proxy; spy.Actual = TitleProtocolCommandFlowTests.Actual(d, p); spy.Services = p; spy.Trace = trace; spy.Method = method; return proxy; }); }
    }
    public sealed class ScopeProbe<T, R>(object actual, Trace trace) : ICommandHandler<T, R>, IDisposable where T : class, ICommand<R> where R : notnull { public ValueTask<R> HandleAsync(CommandExecutionContext c, T q, CancellationToken ct) => ((ICommandHandler<T, R>)actual).HandleAsync(c, q, ct); public void Dispose() { if (!Modern) trace.AfterUnit?.Invoke(); if (trace.ScopeFailure is { } cause) throw cause; } }
    public class CreatureProbe : DispatchProxy { internal object Actual = null!; internal Trace Trace = null!; protected override object? Invoke(MethodInfo? method, object?[]? args) { if (method!.Name == "DiscardExact") Trace.Discarded.Add((SelectedSessionIdentity)args![0]!); try { return method.Invoke(Actual, args); } catch (TargetInvocationException ex) when (ex.InnerException is { } cause) { ExceptionDispatchInfo.Capture(cause).Throw(); throw; } } }
    private sealed class UnitProbe(ICommandWorkUnits actual, Trace trace) : ICommandWorkUnits {
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CommandWorkUnit, CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull { trace.Units.Add((false, d)); return actual.WriteAsync(c, d, a, ct); }
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CommandWorkUnit, CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull { trace.Units.Add((false, d)); return actual.WriteResultAsync(c, d, a, ct); }
        public async ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull { trace.Units.Add((true, d)); var result = await actual.ReadOnlyAsync(c, d, a, ct); trace.AfterUnit?.Invoke(); return result; }
    }
    private sealed class MappingProbe(Trace trace) : IInventorySlotMappingRuleService {
        private readonly InventorySlotMappingRuleService actual = new();
        public BusinessSlot ToBusinessSlot(GameSlot slot) { if (trace.Armed && !trace.ResultMapping && trace.MappingFailure is { } cause) throw cause; return actual.ToBusinessSlot(slot); }
        public GameSlot ToGameSlot(BusinessSlot slot) { if (trace.Armed && trace.DomainReturned && trace.ResultMapping && trace.MappingFailure is { } cause) throw cause; return actual.ToGameSlot(slot); }
    }
    private sealed class EventView(IWeeklyAttendanceDomainService attendance, TimeProvider clock) : IEventPlayerProjectionDomainService { public async ValueTask<EventPlayerSnapshot> ReadNowAsync(SelectedSessionIdentity s, CancellationToken ct) => new([], await attendance.StateAsync(s.AccountId, clock.GetUtcNow(), ct)); }
    private sealed class Content : ITitleBookDefinitionSource, IAchievementDefinitionSource, IItemPlacementDefinitionSource, IEquipmentSlotDefinitionSource, IEquipmentRequirementDefinitionSource, IAchievementCharacterFactsDomainService, IWeeklyAttendanceRuleService {
        IReadOnlyList<TitleBookCategoryDefinition> ITitleBookDefinitionSource.Load() => Enumerable.Range(0, 5).Select(c => new TitleBookCategoryDefinition(c, 20, c == 4 ? [new(4, 10, 0, -1, [26596, 26628])] : [])).ToArray();
        IReadOnlyList<AchievementDefinition> IAchievementDefinitionSource.Load() => [new(9001, 1, 99, new("huntmonster", -1, [-1, -1, -1, 30], 1), [])];
        public ValueTask<ItemPlacement?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<ItemPlacement?>(id is 26596 or 26628 ? new(1, 1, new(BusinessInventorySpace.CarriedEquipment, 0, 47)) { WorldClass = ItemWorldClass.Equipment } : new(2, 1000, new(BusinessInventorySpace.Consumable, 0, 55)));
        ValueTask<BusinessSlot?> IEquipmentSlotDefinitionSource.GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<BusinessSlot?>(id is 26596 or 26628 ? new(BusinessInventorySpace.WornEquipment, 1) : null);
        public ValueTask<int?> MinimumLevelAsync(int id, CancellationToken ct) => ValueTask.FromResult<int?>(1);
        public ValueTask<AchievementCharacterFacts> ReadAsync(CharacterId id, CancellationToken ct) => ValueTask.FromResult(new AchievementCharacterFacts(20, new HashSet<int>()));
        public ValueTask<AchievementCharacterFacts> ReadForUnlockAsync(CharacterId id, IReadOnlyList<AchievementDefinition> pending, CancellationToken ct) => ReadAsync(id, ct);
        public void ValidateCalendarText(string text) { }
        public ValueTask<(WeeklyAttendanceDefinition? Definition, string? Error)> DefinitionAsync(CancellationToken ct) => ValueTask.FromResult<(WeeklyAttendanceDefinition?, string?)>((new("controlled", 1, 1, 12, [new(1, 2000, 3)], []), null));
        public ValueTask<string?> ItemNameAsync(int id, CancellationToken ct) => ValueTask.FromResult<string?>("reward");
        public ValueTask<bool> RecommendedAsync(int dungeon, int level, CancellationToken ct) => ValueTask.FromResult(true);
        public DateTimeOffset WeekStart(DateTimeOffset now) => new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
    }
}
