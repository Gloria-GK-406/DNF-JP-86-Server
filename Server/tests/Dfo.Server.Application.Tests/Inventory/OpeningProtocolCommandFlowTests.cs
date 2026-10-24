using System.Collections;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Application.Users;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.AvatarPackageOpening;
using Dfo.Server.Protocol.Game.MagicBoxSingleOpening;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol.Game.PackageOpening;
using Dfo.Server.Protocol.Game.MagicBoxBatchOpening;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class OpeningProtocolCommandFlowTests(ITestOutputHelper output)
{
    // Fixture-only DDL. The trigger is attached to the actual reward INSERT, never a fake participant.
    private const string NativeDdl = "CREATE TABLE opening_commit_parent(id INTEGER PRIMARY KEY); "
        + "CREATE TABLE opening_commit_child(id INTEGER REFERENCES opening_commit_parent(id) DEFERRABLE INITIALLY DEFERRED); "
        + "CREATE TRIGGER fail_opening AFTER INSERT ON item_slots WHEN NEW.slot_index<>80 AND NEW.slot_index<>120 "
        + "BEGIN INSERT INTO opening_commit_child VALUES(404); END;";
    private const string ActionDdl = "CREATE TRIGGER fail_opening BEFORE INSERT ON item_slots "
        + "WHEN NEW.slot_index<>80 AND NEW.slot_index<>120 BEGIN SELECT RAISE(ABORT,'opening reward action SQL failure'); END;";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_action_SQL_and_native_Commit_failures_keep_the_protocol_rollback_ack_and_same_entry_retry(bool nativeCommit)
    {
        var trace = new Trace();
        using var opening = new OpeningFixture(services => Configure(services, trace));
        var f = opening.Host;
        opening.Content.UseRule = opening.Content.UseRule with { CooldownMilliseconds = 5000 };
        opening.Content.Definitions[1000] = opening.Content.Definitions[1000] with { UsesSeriaLuck = true };
        var source = nativeCommit ? 1000 : 1001;
        await f.Seed(80, source, 2); await f.Seed(120, 2000, 5);
        var before = Assets(f); f.Sql(nativeCommit ? NativeDdl : ActionDdl);
        var input = Request(nativeCommit); var body = EffectRequestTransport.Body(input);
        await EffectRequestTransport.Send(f, input);

        var attempt = Assert.Single(trace.Attempts);
        Assert.Equal(1, attempt.ForeignKeys);
        var errors = trace.Errors.Where(x => x.Cause is SqliteException).ToArray();
        Assert.NotEmpty(errors);
        var cause = Assert.IsType<SqliteException>(errors[0].Cause);
        Assert.All(errors, x => Assert.Same(cause, x.Cause));
        Assert.DoesNotContain(errors, x => x.Category.Contains("CommandLoggingBehavior", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.Level == LogLevel.Error && x.Category.EndsWith(nativeCommit ? "GameMagicBoxBatchOpeningCommandHandler" : "GamePackageOpeningCommandHandler", StringComparison.Ordinal));
        Assert.Equal(19, cause.SqliteErrorCode);
        Assert.Equal(nativeCommit ? 787 : 1811, cause.SqliteExtendedErrorCode);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, attempt.Transaction.Completion);
        if (nativeCommit)
        {
            Assert.NotNull(attempt.Receipt); Assert.Null(attempt.ActionFailure);
            Assert.Equal(2, attempt.RewardCount); Assert.True(attempt.DeferredChildren > 0);
            Assert.True(attempt.EventRows > before.Events); Assert.True(attempt.DeliveryRows > before.Deliveries);
            Assert.Contains("SqliteTransaction.Commit", cause.StackTrace!, StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(attempt.Receipt); Assert.Same(cause, attempt.ActionFailure);
            Assert.DoesNotContain("SqliteTransaction.Commit", cause.StackTrace!, StringComparison.Ordinal);
        }
        var entry = Entry(f); var ordinal = entry.Ordinal;
        Assert.Equal(EffectOperationState.ConfirmedRollback, entry.State);
        Assert.Equal(nativeCommit, entry.Projection is not null);
        Assert.Equal(input.Metadata.Selector, entry.Selector); Assert.Equal(input.Metadata.Type, entry.Type);
        Assert.Equal(input.Metadata.Sequence, entry.Sequence); Assert.Equal(body.Length, entry.Length);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)), entry.Hash, ignoreCase: true);
        Assert.Equal(before, Assets(f)); Assert.False(Cooling(f, source));
        Assert.Empty(f.Sender.Batches); Assert.Single(f.Sender.Messages);
        if (nativeCommit) Assert.Equal(OpeningRejection.StorageFailure, Assert.IsType<MagicBoxBatchOpeningResponse>(f.Sender.Messages[0]).Result.Rejection);
        else Assert.Equal(OpeningRejection.StorageFailure, Assert.IsType<PackageOpeningResponse>(f.Sender.Messages[0]).Result.Rejection);
        var failureFrame = Assert.Single(f.Sender.Outbound.Frames);
        Assert.Equal((byte)1, failureFrame[0]);
        if (!nativeCommit) Assert.Equal(Convert.FromHexString("0000FFFFFFFFFFFFFFFF000000000000000000000000"), failureFrame[15..]);
        else Assert.Equal(new byte[] { 0, 2 }, failureFrame[15..]);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.Empty(f.Sender.Outbound.Closes);
        var failureSummary = new { nativeCommit, ddl = nativeCommit ? NativeDdl : ActionDdl,
            selector = entry.Selector, type = entry.Type, sequence = entry.Sequence, ordinal, entry.Hash, entry.Length,
            body = Convert.ToHexString(body), ack = Convert.ToHexString(failureFrame),
            cause.SqliteErrorCode, cause.SqliteExtendedErrorCode, stack = cause.StackTrace,
            attempt.ForeignKeys, actionReturnedReceipt = attempt.Receipt is not null, attempt.RewardCount,
            attempt.DeferredChildren, attempt.EventRows, attempt.DeliveryRows, completion = attempt.Transaction.Completion.ToString(),
            ledgerState = entry.State.ToString(), projectionPresent = entry.Projection is not null, before,
            logs = errors.Select(x => new { x.Category, level = x.Level.ToString() }).ToArray(),
            logSameCause = errors.All(x => ReferenceEquals(cause, x.Cause)), sourceCloses = f.Sender.Outbound.Closes.Count };

        f.Sql("DROP TRIGGER fail_opening");
        await EffectRequestTransport.Send(f, input);
        Assert.Same(entry, Entry(f)); Assert.Equal(ordinal, entry.Ordinal);
        Assert.Equal(EffectOperationState.Committed, entry.State);
        Assert.Equal(1, f.Count(source)); Assert.Equal(nativeCommit ? 4 : 5, f.Count(2000));
        Assert.Equal(2, f.Count(3)); Assert.Equal(nativeCommit ? 1 : 0, f.Count(6000));
        Assert.Equal(100000, f.Gold); Assert.True(Cooling(f, source));
        Assert.Equal(2, trace.Attempts.Count); Assert.NotNull(trace.Attempts[1].Receipt);
        Assert.Equal(DomainTransactionCompletion.Committed, trace.Attempts[1].Transaction.Completion);
        Assert.Equal(nativeCommit ? 1 : before.Luck, Assets(f).Luck);
        Assert.True(Assets(f).Events > before.Events); Assert.True(Assets(f).Deliveries > before.Deliveries);
        var committed = Assets(f); var acks = f.Sender.Messages.Count(x => x is PackageOpeningResponse or MagicBoxBatchOpeningResponse);
        await EffectRequestTransport.Send(f, input);
        Assert.Same(entry, Entry(f)); Assert.Equal(ordinal, entry.Ordinal);
        Assert.Equal(2, trace.Attempts.Count); Assert.Equal(committed, Assets(f));
        Assert.Equal(acks, f.Sender.Messages.Count(x => x is PackageOpeningResponse or MagicBoxBatchOpeningResponse));
        Assert.True(EffectRequestTransport.IsStateReplay(f.Sender.Batches.Last()));
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Empty(f.Sender.Outbound.Closes);
        output.WriteLine(JsonSerializer.Serialize(new { failure = failureSummary, retry = new { entry.Ordinal, state = entry.State.ToString(), committed },
            replay = new { actualActionCalls = trace.Attempts.Count, acks, noFence = true, noSourceClose = true } }));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Four_real_wire_shapes_commit_ack_before_absolute_updates_and_replay_current_state_without_reopening(int protocol)
    {
        var trace = new Trace(); using var opening = new OpeningFixture(services => Configure(services, trace)); var f = opening.Host;
        var source = protocol == 1 ? 1002 : protocol >= 2 ? 1000 : 1001;
        var count = protocol == 3 ? 2 : 1;
        await f.Seed(80, source, count); if (protocol >= 2) await f.Seed(120, 2000, count);
        var request = Input(protocol);
        await EffectRequestTransport.Send(f, request);
        var selected = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(Assert.Single(trace.Contexts).Request);
        var entry = selected.State.Entry;
        Assert.Equal(EffectOperationState.Committed, entry.State); Assert.True(selected.HasCommittedUnit);
        Assert.NotNull(entry.Projection); Assert.NotEmpty(entry.Projection.TouchedSlots);
        Assert.Equal(0, f.Count(source)); Assert.Equal(0, f.Count(2000));
        Assert.Equal(protocol == 1 ? 1 : 0, f.Count(39032));
        Assert.Equal(protocol == 1 ? 0 : 2 * count, f.Count(3)); Assert.Equal(protocol >= 2 ? count : 0, f.Count(6000));
        var frames = f.Sender.Outbound.Frames;
        Assert.Equal((byte)1, frames[0][0]); Assert.Equal(request.Metadata.Type, BinaryPrimitives.ReadUInt16LittleEndian(frames[0].AsSpan(1)));
        var responses = protocol == 1 ? 2 : 1;
        if (protocol == 1)
        {
            Assert.IsType<AvatarPackageOpeningResponse>(f.Sender.Messages[0]); Assert.IsType<OpeningRewardPopup>(f.Sender.Messages[1]);
            Assert.Equal(new byte[] { 1, 80, 0 }, frames[0][15..]);
            Assert.Equal((ushort)0xA0, BinaryPrimitives.ReadUInt16LittleEndian(frames[1].AsSpan(1)));
        }
        Assert.All(frames.Skip(responses), frame => Assert.Equal((byte)0, frame[0]));
        Assert.Contains(frames.Skip(responses), frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == 0xE);
        var ordinal = entry.Ordinal; var before = Assets(f); var acks = f.Sender.Messages.Count(x => x is IGameCmdMessage);
        f.Sql("UPDATE character_stats SET carried_gold=98765 WHERE character_id=1");
        await EffectRequestTransport.Send(f, request);
        Assert.Single(trace.Contexts); Assert.Same(entry, Entry(f)); Assert.Equal(ordinal, entry.Ordinal);
        Assert.Equal(98765, EffectRequestTransport.MainValue(f, 0));
        Assert.Equal(before with { Gold = 98765 }, Assets(f)); Assert.Equal(acks, f.Sender.Messages.Count(x => x is IGameCmdMessage));
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Empty(f.Sender.Outbound.Closes);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Malformed_input_never_claims_Main_but_a_real_domain_refusal_confirms_rollback(int protocol)
    {
        var trace = new Trace(); using var opening = new OpeningFixture(services => Configure(services, trace)); var f = opening.Host;
        var input = Input(protocol); await EffectRequestTransport.SendRaw(f, input.Metadata, []);
        var malformed = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(trace.Contexts[0].Request);
        Assert.Equal(EffectOperationState.Rejected, malformed.State.Entry.State); Assert.Null(malformed.State.Entry.Projection);
        Assert.False(malformed.HasCommittedUnit); Assert.Equal(OpeningRejection.InvalidRequest, Rejection(f.Sender.Messages[0]));
        await EffectRequestTransport.Send(f, Input(protocol, 8));
        var refused = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(trace.Contexts[1].Request);
        Assert.Equal(EffectOperationState.ConfirmedRollback, refused.State.Entry.State); Assert.Null(refused.State.Entry.Projection);
        Assert.False(refused.HasCommittedUnit); Assert.Equal(OpeningRejection.WrongItem, Rejection(f.Sender.Messages[1]));
        Assert.Empty(f.Sender.Batches); Assert.Equal(100000, f.Gold); Assert.Equal(0, f.Count(3));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task An_initially_unselected_request_cannot_upgrade_when_selection_appears_before_the_whole(int protocol)
    {
        var trace = new Trace(); using var opening = new OpeningFixture(services => Configure(services, trace)); var f = opening.Host;
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(f.Session));
        trace.Before = () => f.Select(2);
        await EffectRequestTransport.Send(f, Input(protocol));
        var rejected = Assert.IsType<CommandRequestContext.ProtocolEffectRejected>(Assert.Single(trace.Contexts).Request);
        Assert.False(rejected.HasCommittedUnit); Assert.Empty(trace.Attempts);
        Assert.Equal(OpeningRejection.NoSession, Rejection(Assert.Single(f.Sender.Messages))); Assert.Empty(f.Sender.Batches);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Package_wire_selection_preserves_the_original_plain_or_avatar_branch(bool avatar)
    {
        using var opening = new OpeningFixture(); var f = opening.Host;
        opening.Content.Definitions[1001] = OpeningContent.Package(1001, avatar ? [new(39032, 1)] : [new(3, 2), new(6000, 1)])
            with { Kind = OpeningPackageKind.Selection, SelectionCount = 1 };
        await f.Seed(80, 1001, 1);
        await EffectRequestTransport.Send(f, new PackageOpeningRequest(new(1, 0xA0, 0, 7),
            new(80, [avatar ? 39032 : 6000]), avatar ? new(80, [new(39032, 2)]) : null));
        var ack = Assert.IsType<PackageOpeningResponse>(f.Sender.Messages[0]); Assert.NotNull(ack.Result.Receipt);
        Assert.Equal(0, f.Count(1001)); Assert.Equal(0, f.Count(3));
        if (avatar) { Assert.Equal(1, f.Count(39032)); Assert.Equal(2, Assert.Single(ack.Result.Receipt.Updates).Core.Attribute); }
        else Assert.Equal(1, f.Count(6000));
    }

    [Fact]
    public async Task Avatar_empty_choices_uses_the_package_domain_and_keeps_ack_then_popup_order()
    {
        using var opening = new OpeningFixture(); var f = opening.Host; await f.Seed(80, 1001, 1);
        await EffectRequestTransport.Send(f, new AvatarPackageOpeningRequest(new(1, 0x207, 0, 7), new(80, [])));
        Assert.IsType<AvatarPackageOpeningResponse>(f.Sender.Messages[0]); Assert.IsType<OpeningRewardPopup>(f.Sender.Messages[1]);
        Assert.Equal(2, f.Count(3)); Assert.Equal(0, f.Count(1001));
    }

    [Fact]
    public async Task Initial_notification_encoder_failure_is_normal_failed_delivery_then_current_replay_succeeds()
    {
        using var opening = new OpeningFixture(); var f = opening.Host; await f.Seed(80, 1001, 1);
        f.Sender.FailBatch = true; var input = Input(0);
        await EffectRequestTransport.Send(f, input);
        Assert.Equal(EffectOperationState.Committed, Entry(f).State); Assert.Equal(2, f.Count(3)); Assert.Equal(0, f.Count(1001));
        Assert.Empty(f.Sender.Messages); Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        f.Sender.FailBatch = false; await EffectRequestTransport.Send(f, input);
        Assert.True(EffectRequestTransport.IsStateReplay(f.Sender.Batches.Last())); Assert.Empty(f.Sender.Messages.OfType<PackageOpeningResponse>());
        Assert.Equal(2, f.Count(3)); Assert.Empty(f.Sender.Outbound.Closes);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Real_commit_or_action_cause_and_an_independent_post_current_fault_preserve_both_occurrences(bool nativeCommit, bool sameObject)
    {
        var trace = new Trace { FaultCurrent = true, AfterCommit = nativeCommit, SameObject = sameObject };
        using var opening = new OpeningFixture(services => Configure(services, trace)); var f = opening.Host;
        if (nativeCommit)
        {
            await f.Seed(80, 1000, 2); await f.Seed(120, 2000, 5); f.Sql(NativeDdl);
        }
        var before = Assets(f);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => EffectRequestTransport.Send(f, Request(nativeCommit)).AsTask());
        var attempt = Assert.Single(trace.Attempts);
        var original = nativeCommit ? attempt.Transaction.CommitFailure!.Cause : attempt.ActionFailure!;
        Assert.True(trace.CurrentFaultThrown); Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Same(original, failure.InnerExceptions[0]); Assert.Same(trace.CurrentFault, failure.InnerExceptions[1]);
        if (sameObject) Assert.Same(original, trace.CurrentFault);
        if (nativeCommit) { Assert.NotNull(attempt.Receipt); Assert.Equal(787, Assert.IsType<SqliteException>(original).SqliteExtendedErrorCode); }
        else Assert.IsType<OpeningRejectedException>(original);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, attempt.Transaction.Completion);
        Assert.Empty(f.Sender.Messages); Assert.Empty(f.Sender.Batches); Assert.Equal(before, Assets(f));
        Assert.Equal(EffectOperationState.ConfirmedRollback, Entry(f).State);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.Equal(new[] { f.Connection.ConnectionId }, f.Sender.Outbound.Closes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task A_real_opening_action_SQL_cause_and_independent_rollback_fault_keep_both_and_fence_the_exact_participant(bool sameObject)
    {
        var trace = new Trace { FailRollback = true, RollbackSameObject = sameObject };
        using var opening = new OpeningFixture(services => Configure(services, trace)); var f = opening.Host;
        await f.Seed(80, 1001, 2); await f.Seed(120, 2000, 5); var before = Assets(f); f.Sql(ActionDdl);
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => EffectRequestTransport.Send(f, Request(false)).AsTask());
        var business = Assert.Single(trace.Attempts).ActionFailure!;
        Assert.Equal(1811, Assert.IsType<SqliteException>(business).SqliteExtendedErrorCode);
        var causes = Assert.IsType<AggregateException>(failure.InnerException).InnerExceptions;
        Assert.Equal(2, causes.Count); Assert.Same(business, causes[0]); Assert.Same(sameObject ? business : trace.RollbackFault, causes[1]);
        Assert.Equal(before, Assets(f)); Assert.Empty(f.Sender.Messages); Assert.Empty(f.Sender.Batches);
        Assert.Equal(EffectOperationState.Blocked, Entry(f).State);
        Assert.True(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.Equal(new[] { f.Connection.ConnectionId, f.Connection.ConnectionId }, f.Sender.Outbound.Closes);
        Assert.All(f.Sender.Outbound.Entries.Where(entry => entry.IsClose), entry => Assert.Same(f.Connection, entry.Connection));
    }

    [Fact]
    public async Task A_scope_cleanup_failure_after_returned_success_keeps_default_typed_fencing_and_the_durable_reward()
    {
        var cause = new IOException("independent command scope disposal"); var trace = new Trace { ScopeFailure = cause };
        using var opening = new OpeningFixture(services => Configure(services, trace)); var f = opening.Host; await f.Seed(80, 1001, 1);
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => EffectRequestTransport.Send(f, Input(0)).AsTask());
        Assert.Same(cause, failure.InnerException); Assert.Equal(2, f.Count(3)); Assert.Equal(0, f.Count(1001));
        Assert.Equal(EffectOperationState.Committed, Entry(f).State);
        Assert.True(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Equal(new[] { f.Connection.ConnectionId, f.Connection.ConnectionId }, f.Sender.Outbound.Closes);
        Assert.All(f.Sender.Outbound.Entries.Where(entry => entry.IsClose), entry => Assert.Same(f.Connection, entry.Connection));
    }

    private static IEffectSettlementRequest Input(int protocol, ushort sequence = 7) => protocol switch
    {
        0 => new PackageOpeningRequest(new(1, 0xA0, 0, sequence), new(80, [])),
        1 => new AvatarPackageOpeningRequest(new(1, 0x207, 0, sequence), new(80, [new(39032, 2)])),
        2 => new MagicBoxSingleOpeningRequest(new(1, 0xD0, 0, sequence), new(0, 80, 0, 120, 0, 1)),
        3 => new MagicBoxBatchOpeningRequest(new(1, 0x450, 0, sequence), new(0, 80, 1000, 120, 2000, 2)),
        _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
    };
    private static OpeningRejection Rejection(IServerNetworkMessage message) => message switch
    { PackageOpeningResponse x => x.Result.Rejection, AvatarPackageOpeningResponse x => x.Result.Rejection,
        MagicBoxSingleOpeningResponse x => x.Result.Rejection, MagicBoxBatchOpeningResponse x => x.Result.Rejection,
        _ => throw new InvalidOperationException("Not an opening response."), };

    private static IEffectSettlementRequest Request(bool box) => box
        ? new MagicBoxBatchOpeningRequest(new(1, 0x450, 0, 7), new(0, 80, 1000, 120, 2000, 1))
        : new PackageOpeningRequest(new(1, 0xA0, 0, 7), new(80, []));
    private sealed record AssetState(int Sources, int Material, int Reward, int OtherReward, int Gold, int Luck, int Events, int Deliveries);
    private static AssetState Assets(ShopFixture f) => new(f.Count(1000) + f.Count(1001), f.Count(2000), f.Count(3), f.Count(6000), f.Gold,
        f.Scalar("SELECT seria_luck_value FROM accounts WHERE account_id=1"), f.Scalar("SELECT count(*) FROM domain_events"), f.Scalar("SELECT count(*) FROM domain_event_deliveries"));
    private static bool Cooling(ShopFixture f, int source) => TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>()
        .IsCoolingDown(f.Session.CharacterId, ItemCooldownKey.ForItem(source)));

    // Read the production ledger's private CWT only; do not Admit, mutate or substitute it.
    private static EffectOperationEntry Entry(ShopFixture f)
    {
        var ledger = f.Services.GetRequiredService<EffectOperationLedger>();
        var table = typeof(EffectOperationLedger).GetField("ledgers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ledger)!;
        object?[] args = [f.Connection, null];
        Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!);
        var entries = (IDictionary)args[1]!.GetType().GetProperty("Entries")!.GetValue(args[1])!;
        return Assert.Single(entries.Values.Cast<EffectOperationEntry>());
    }
    private static void Configure(IServiceCollection services, Trace trace)
    {
        services.AddSingleton<ILoggerProvider>(trace);
        if (trace.FaultCurrent) services.AddSingleton<IOnlineUserQueryDomainService>(p => new PostCurrentQuery(p.GetRequiredService<OnlineUserDomainService>(), trace));
        services.AddScoped<GamePackageOpeningCommandHandler>(); services.AddScoped<GameAvatarPackageOpeningCommandHandler>();
        services.AddScoped<GameMagicBoxSingleOpeningCommandHandler>(); services.AddScoped<GameMagicBoxBatchOpeningCommandHandler>();
        services.AddScoped<ICommandHandler<GamePackageOpeningCommand, PackageOpeningResult>, ObserveWhole<GamePackageOpeningCommand, PackageOpeningResult, GamePackageOpeningCommandHandler>>();
        services.AddScoped<ICommandHandler<GameAvatarPackageOpeningCommand, PackageOpeningResult>, ObserveWhole<GameAvatarPackageOpeningCommand, PackageOpeningResult, GameAvatarPackageOpeningCommandHandler>>();
        services.AddScoped<ICommandHandler<GameMagicBoxSingleOpeningCommand, MagicBoxOpeningResult>, ObserveWhole<GameMagicBoxSingleOpeningCommand, MagicBoxOpeningResult, GameMagicBoxSingleOpeningCommandHandler>>();
        services.AddScoped<ICommandHandler<GameMagicBoxBatchOpeningCommand, MagicBoxOpeningResult>, ObserveWhole<GameMagicBoxBatchOpeningCommand, MagicBoxOpeningResult, GameMagicBoxBatchOpeningCommandHandler>>();
        services.AddSingleton(trace);
        services.AddSingleton<PackageOpeningDomainService>(); services.AddSingleton<MagicBoxOpeningDomainService>();
        services.AddSingleton<IPackageOpeningDomainService>(p => new ObservePackage(p.GetRequiredService<PackageOpeningDomainService>(), p, trace));
        services.AddSingleton<IMagicBoxOpeningDomainService>(p => new ObserveBox(p.GetRequiredService<MagicBoxOpeningDomainService>(), p, trace));
    }
    private sealed class Attempt(DomainTransaction transaction)
    {
        internal DomainTransaction Transaction { get; } = transaction;
        internal int ForeignKeys, RewardCount, DeferredChildren, EventRows, DeliveryRows;
        internal OpeningInventoryReceipt? Receipt;
        internal Exception? ActionFailure;
    }
    private static async ValueTask<Attempt> Begin(IServiceProvider services, Trace trace, CancellationToken ct)
    {
        var attempt = new Attempt((DomainTransaction)services.GetRequiredService<IDomainTransactionAccessor>().Current!);
        trace.Attempts.Add(attempt);
        if (trace.FailRollback) attempt.Transaction.Enlist(trace, () => new RollbackFault(trace, attempt));
        attempt.ForeignKeys = await Scalar(services, "PRAGMA foreign_keys", ct);
        return attempt;
    }
    private static async ValueTask Returned(IServiceProvider services, Attempt attempt, OpeningInventoryReceipt? receipt, bool box, CancellationToken ct)
    {
        attempt.Receipt = receipt;
        // SqliteDatabase joins the very same actual participant and open transaction as the reward write.
        attempt.RewardCount = await Scalar(services, "SELECT COALESCE(SUM(s.count),0) FROM item_slots s JOIN items i ON i.item_uid=s.core_uid WHERE substr(hex(i.item_core),3,8)='03000000'", ct);
        if (box && await Scalar(services, "SELECT count(*) FROM sqlite_master WHERE name='opening_commit_child'", ct) != 0) attempt.DeferredChildren = await Scalar(services, "SELECT count(*) FROM opening_commit_child", ct);
        attempt.EventRows = await Scalar(services, "SELECT count(*) FROM domain_events", ct);
        attempt.DeliveryRows = await Scalar(services, "SELECT count(*) FROM domain_event_deliveries", ct);
    }
    private static ValueTask<int> Scalar(IServiceProvider services, string sql, CancellationToken ct) => services.GetRequiredService<SqliteDatabase>().ReadAsync(async (cmd, token) =>
    { cmd.CommandText = sql; return Convert.ToInt32(await cmd.ExecuteScalarAsync(token)); }, ct);
    private sealed class ObservePackage(IPackageOpeningDomainService actual, IServiceProvider services, Trace trace) : IPackageOpeningDomainService
    {
        public async ValueTask<PackageOpeningResult> OpenAsync(SelectedSessionIdentity session, BusinessPackageOpeningInput input, DateTimeOffset now, IEffectFootprintRecorder footprint, CancellationToken ct)
        {
            var attempt = await Begin(services, trace, ct);
            try { var result = await actual.OpenAsync(session, input, now, footprint, ct); await Returned(services, attempt, result.Receipt, false, ct); return result; }
            catch (Exception error) { attempt.ActionFailure = error; throw; }
        }
    }
    private sealed class ObserveBox(IMagicBoxOpeningDomainService actual, IServiceProvider services, Trace trace) : IMagicBoxOpeningDomainService
    {
        public async ValueTask<MagicBoxOpeningResult> OpenAsync(SelectedSessionIdentity session, BusinessMagicBoxOpeningInput input, DateTimeOffset now, IEffectFootprintRecorder footprint, CancellationToken ct)
        {
            var attempt = await Begin(services, trace, ct);
            try { var result = await actual.OpenAsync(session, input, now, footprint, ct); await Returned(services, attempt, result.Receipt, true, ct); return result; }
            catch (Exception error) { attempt.ActionFailure = error; throw; }
        }
    }
    private sealed class ObserveWhole<TCommand, TResult, THandler>(THandler actual, Trace trace) : ICommandHandler<TCommand, TResult>, IDisposable
        where TCommand : class, ICommand<TResult> where TResult : notnull where THandler : ICommandHandler<TCommand, TResult>
    {
        public ValueTask<TResult> HandleAsync(CommandExecutionContext context, TCommand command, CancellationToken ct)
        { trace.Contexts.Add(context); trace.Before?.Invoke(); return actual.HandleAsync(context, command, ct); }
        public void Dispose() { if (trace.ScopeFailure is { } failure) throw failure; }
    }
    private sealed class PostCurrentQuery(IOnlineUserQueryDomainService actual, Trace trace) : IOnlineUserQueryDomainService
    {
        public OnlineUserSnapshot GetRequired(OnlineUserId id) => actual.GetRequired(id);
        public bool TryGetByConnection(NetworkConnectionId id, out OnlineUserSnapshot? user)
        {
            var attempt = trace.Attempts.LastOrDefault();
            if (!trace.CurrentFaultThrown && attempt is not null && (trace.AfterCommit
                    ? attempt.Transaction.Completion == DomainTransactionCompletion.ConfirmedRollback
                    : attempt.ActionFailure is not null && attempt.Transaction.IsRollbackOnly))
            {
                trace.CurrentFaultThrown = true;
                trace.CurrentFault = trace.SameObject ? trace.AfterCommit ? attempt.Transaction.CommitFailure!.Cause : attempt.ActionFailure!
                    : new IOException("independent actual current-query failure");
                throw trace.CurrentFault;
            }
            return actual.TryGetByConnection(id, out user);
        }
    }
    private sealed class RollbackFault(Trace trace, Attempt attempt) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => throw (trace.RollbackSameObject ? attempt.ActionFailure! : trace.RollbackFault);
    }
    private sealed class Trace : ILoggerProvider
    {
        internal List<Attempt> Attempts { get; } = [];
        internal List<CommandExecutionContext> Contexts { get; } = [];
        internal Action? Before;
        internal bool FaultCurrent, AfterCommit, SameObject, CurrentFaultThrown;
        internal Exception? CurrentFault;
        internal bool FailRollback, RollbackSameObject;
        internal IOException RollbackFault { get; } = new("controlled opening rollback cleanup");
        internal Exception? ScopeFailure;
        internal ConcurrentQueue<(string Category, LogLevel Level, Exception Cause)> Errors { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
        public void Dispose() { }
        private sealed class Logger(Trace owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            { if (exception is not null) owner.Errors.Enqueue((category, logLevel, exception)); }
        }
    }
}
