using Dfo.Server.Application.Tests.Support;
using System.Collections;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Characters;
using System.Reflection;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Users;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Abstractions.Transactions;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Creatures;
using Dfo.Server.Protocol.Game.UseStackable;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class CreatureAndEffectProtocolCommandFlowTests(ITestOutputHelper output)
{
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Real_decoder_dispatcher_sqlite_and_encoder_preserve_success_and_frame_order(int protocol)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        using var f = await Create(protocol, delivery); var physical = delivery.Add(f.Connection);
        var before = f.Gold;
        await Send(f, protocol); await delivery.Settled();
        var frames = Split(physical.Sends).ToArray();
        Assert.NotEmpty(physical.Sends); Assert.Empty(f.Sender.Outbound.Closes);
        Assert.Equal(before, f.Gold);
        if (protocol == 0)
        {
            Assert.Equal(new ushort[] { 0x65, 0x64, 0x0E }, frames.Select(Type));
            Assert.Equal(new byte[] { 1, 189, 0, 7 }, frames[1][15..]);
            Assert.Equal(1, f.Count(25));
        }
        else if (protocol == 1)
        {
            Assert.Equal(new ushort[] { 0x66, 0x0E, 0x69 }, frames.Select(Type));
            Assert.Equal(new byte[] { 1 }, frames[0][15..]);
            Assert.Equal(63023, BinaryPrimitives.ReadInt32LittleEndian(f.StoredCore(0, InventoryContainerType.Pet).AsSpan(1)));
        }
        else if (protocol == 2)
        {
            Assert.Equal(new ushort[] { 0xFB, 0x0E, 0xDB }, frames.Select(Type));
            Assert.Equal(new byte[] { 1, 69, 0, 0 }, frames[0][15..]); Assert.Equal(2, f.Count(1000));
        }
        else
        {
            Assert.Equal(new ushort[] { 0x41, 0x1F7 }, frames.Select(Type));
            Assert.Equal(new byte[] { 0, 1 }, frames[0][15..]);
        }
        Assert.All(delivery.Reports, r => Assert.Equal(OutboundOutcome.Delivered, r.Outcome));
        output.WriteLine($"protocol={protocol} frames={string.Join(',', frames.Select(Type))} physicalSends={physical.Sends.Count}");
    }

    [Theory] [InlineData(0)] [InlineData(2)]
    public async Task Committed_duplicate_actual_replay_encoder_failure_closes_original_transport_and_preserves_assets(int protocol)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        using var f = await Create(protocol, delivery); var physical = delivery.Add(f.Connection);
        await Send(f, protocol); await delivery.Settled(); var count = f.Count(protocol == 0 ? 25 : 1000);
        var entry = Entry(f); var ordinal = entry.Ordinal;
        Assert.Equal(EffectOperationState.Committed, entry.State);
        var sends = physical.Sends.Count; var cause = new IOException("actual TASK36 replay encoder"); var calls = 0;
        f.Sender.Outbound.Encoder.Fail((target, message) => { Assert.Same(f.Connection, target); calls++; Assert.Contains("InventorySnapshot", message.GetType().Name); return cause; });
        Assert.Same(cause, await Record.ExceptionAsync(() => Send(f, protocol).AsTask()));
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled();
        Assert.Equal(1, calls); Assert.Equal(1, physical.CloseCalls); Assert.Equal(sends, physical.Sends.Count);
        Assert.Equal(count, f.Count(protocol == 0 ? 25 : 1000));
        Assert.Same(entry, Entry(f)); Assert.Equal(ordinal, entry.Ordinal); Assert.Equal(EffectOperationState.Committed, entry.State);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.Equal("close", physical.Events.Last().Kind);
        Assert.All(delivery.Recorder.Entries, e => Assert.Same(f.Connection, e.Connection));
        output.WriteLine($"protocol={protocol} physicalCloseCalls={physical.CloseCalls} assets={count} actualEncoderCalls={calls}");
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Invalid_slot_or_missing_item_is_refused_without_asset_changes(int protocol)
    {
        using var f = await Create(protocol); var before = f.Scalar("SELECT sum(count) FROM item_slots");
        await Send(f, protocol, invalid: true);
        var frame = Assert.Single(f.Sender.Outbound.Frames); Assert.Equal((byte)1, frame[0]);
        Assert.Equal(new byte[] { 0, protocol == 2 ? (byte)19 : (byte)2 }, frame[15..]);
        Assert.Equal(before, f.Scalar("SELECT sum(count) FROM item_slots"));
    }

    [Theory] [InlineData(0, false)] [InlineData(0, true)] [InlineData(1, false)] [InlineData(1, true)] [InlineData(2, false)] [InlineData(2, true)]
    public async Task Actual_action_or_native_commit_failure_rolls_back_without_success_and_fresh_retry_succeeds(int protocol, bool native)
    {
        var transactions = new SqliteTransactionProbe();
        using var f = await Create(protocol, configure: transactions.Configure); var before = f.Scalar("SELECT sum(count) FROM item_slots");
        var table = protocol == 2 ? "item_slots" : "items";
        f.Sql(native
            ? $"CREATE TABLE task36_parent(id INTEGER PRIMARY KEY); CREATE TABLE task36_child(id INTEGER REFERENCES task36_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER task36_fail AFTER UPDATE ON {table} BEGIN INSERT INTO task36_child VALUES(404); END"
            : $"CREATE TRIGGER task36_fail BEFORE UPDATE ON {table} BEGIN SELECT RAISE(ABORT,'TASK36 action failure'); END");
        var cause = await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => Send(f, protocol).AsTask());
        Assert.Equal(native ? 787 : 1811, cause.SqliteExtendedErrorCode);
        if (native) SqliteFailureAssertions.FromCommit(Assert.Single(transactions.Transactions), cause);
        Assert.Equal(before, f.Scalar("SELECT sum(count) FROM item_slots")); Assert.Empty(f.Sender.Messages);
        f.Sql("DROP TRIGGER task36_fail"); await Send(f, protocol, sequence: 8);
        Assert.Contains(f.Sender.Outbound.Frames, x => x[0] == 1 && x[15] == 1);
        output.WriteLine($"protocol={protocol} native={native} cause={cause.SqliteExtendedErrorCode} rollbackAssets=True freshRetry=True");
    }

    [Theory] [InlineData(0)] [InlineData(2)]
    public async Task Initial_actual_encoder_failure_preserves_Main_and_closes_original_source(int protocol)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        using var f = await Create(protocol, delivery); var physical = delivery.Add(f.Connection);
        var cause = new IOException("TASK36 initial actual encoder");
        f.Sender.Outbound.Encoder.Fail((target, _) => { Assert.Same(f.Connection, target); return cause; });
        var failure = await Record.ExceptionAsync(() => Send(f, protocol).AsTask());
        var old = Environment.GetEnvironmentVariable("TASK36_EXPECT_ROUTE") == "legacy";
        if (old || protocol == 0)
        {
            if (protocol == 0) Assert.Null(failure); else Assert.Same(cause, failure);
            Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        }
        else
        {
            var uncertain = Assert.IsType<TransactionOutcomeUncertainException>(failure); Assert.Same(cause, uncertain.InnerException);
            Assert.True(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        }
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled();
        Assert.Equal(1, physical.CloseCalls); Assert.Empty(physical.Sends);
        Assert.Equal(protocol == 0 ? 1 : 2, f.Count(protocol == 0 ? 25 : 1000));
        output.WriteLine($"protocol={protocol} old={old} failure={failure?.GetType().Name ?? "none"} assetOnce=True physicalClose=1 blocked={f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)}");
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Whole_scope_failure_preserves_committed_Main_and_closes_physical_source(int protocol)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var cause = new IOException("TASK36 whole scope disposal");
        var old = Environment.GetEnvironmentVariable("TASK36_EXPECT_ROUTE") == "legacy";
        var trace = new RemainingOpeningProtocolCommandFlowTests.Trace();
        using var f = await Create(protocol, delivery, services => {
            if (protocol == 0) RemainingOpeningProtocolCommandFlowTests.DomainObserver.Add<ICreatureNamingDomainService>(services, trace);
            else if (protocol == 1) RemainingOpeningProtocolCommandFlowTests.DomainObserver.Add<ICreatureRuntimeDomainService>(services, trace);
            else RemainingOpeningProtocolCommandFlowTests.DomainObserver.Add<ISharedEffectDomainService>(services, trace);
            var descriptor = services.Last(x => x.ServiceType.IsGenericType && x.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                && x.ServiceType.GenericTypeArguments[0].Name == (old ? (protocol == 0 ? "RenameCreatureCommand" : protocol == 1 ? "HatchCreatureCommand" : "SharedEffectCommand") : (protocol == 0 ? "GameRenameCreatureCommand" : protocol == 1 ? "GameHatchCreatureCommand" : "GameSharedEffectCommand")));
            var wrapper = typeof(ScopeFault<,>).MakeGenericType(descriptor.ServiceType.GenericTypeArguments);
            services.AddScoped(descriptor.ServiceType, provider => Activator.CreateInstance(wrapper,
                ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!), cause)!);
        });
        var physical = delivery.Add(f.Connection);
        var failure = await Record.ExceptionAsync(() => Send(f, protocol).AsTask());
        if (old) Assert.Same(cause, failure);
        else Assert.Same(cause, Assert.IsType<TransactionOutcomeUncertainException>(failure).InnerException);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Attempts).Transaction.Completion);
        Assert.Equal(protocol == 2 ? 2 : 1, f.Count(protocol == 0 ? 25 : protocol == 1 ? 63023 : 1000));
        Assert.Equal(!old && protocol != 1, f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled(); Assert.Equal(1, physical.CloseCalls);
        Assert.Equal("close", physical.Events.Last().Kind);
        output.WriteLine($"protocol={protocol} committed=True physicalClose=1 sends={physical.Sends.Count} scopeCauseSame=True");
    }
    public sealed class ScopeFault<TCommand, TResult>(object actual, Exception cause) : ICommandHandler<TCommand, TResult>, IDisposable where TCommand : class, ICommand<TResult> where TResult : notnull
    {
        public ValueTask<TResult> HandleAsync(CommandExecutionContext context, TCommand command, CancellationToken ct) => ((ICommandHandler<TCommand, TResult>)actual).HandleAsync(context, command, ct);
        public void Dispose() => throw cause;
    }

    [Theory] [InlineData(0)] [InlineData(2)]
    public async Task Replay_failure_closes_captured_physical_connection_without_touching_same_id_replacement(int protocol)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        using var f = await Create(protocol, delivery); var original = delivery.Add(f.Connection);
        await Send(f, protocol); await delivery.Settled();
        var entry = Entry(f); var ordinal = entry.Ordinal;
        var replacement = f.Connection with { }; var next = delivery.Add(replacement); var cause = new IOException("TASK36 replay replacement");
        f.Sender.Outbound.Encoder.Fail((target, _) => { Assert.Same(f.Connection, target); f.Sender.Outbound.Connections.Remove(target.ConnectionId); f.Sender.Outbound.Connections.Add(replacement); return cause; });
        Assert.Same(cause, await Record.ExceptionAsync(() => Send(f, protocol).AsTask()));
        await original.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled();
        Assert.Equal(1, original.CloseCalls); Assert.Equal(0, next.CloseCalls); Assert.Empty(next.Events);
        Assert.Same(next, delivery.Resolve(replacement)); Assert.Null(delivery.Resolve(f.Connection));
        Assert.Same(entry, Entry(f)); Assert.Equal(ordinal, entry.Ordinal); Assert.Equal(EffectOperationState.Committed, entry.State);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(replacement));
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Domain_cancellation_rolls_back_preserves_cause_and_has_no_success(int protocol)
    {
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); var cause = new OperationCanceledException(canceled.Token);
        var trace = new RemainingOpeningProtocolCommandFlowTests.Trace { DomainFailure = cause };
        using var f = await Create(protocol, configure: services => {
            if (protocol == 0) RemainingOpeningProtocolCommandFlowTests.DomainObserver.Add<ICreatureNamingDomainService>(services, trace);
            else if (protocol == 1) RemainingOpeningProtocolCommandFlowTests.DomainObserver.Add<ICreatureRuntimeDomainService>(services, trace);
            else RemainingOpeningProtocolCommandFlowTests.DomainObserver.Add<ISharedEffectDomainService>(services, trace);
        });
        var count = f.Scalar("SELECT sum(count) FROM item_slots");
        Assert.Same(cause, await Record.ExceptionAsync(() => Send(f, protocol).AsTask()));
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, Assert.Single(trace.Attempts).Transaction.Completion);
        Assert.Equal(count, f.Scalar("SELECT sum(count) FROM item_slots")); Assert.Empty(f.Sender.Messages);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Rename_peer_encoder_failure_is_isolated_and_owner_notice_precedes_ack(bool failPeer)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        using var f = await Create(0, delivery); var owner = delivery.Add(f.Connection);
        var peer = AddPeer(f, 2, visible: true); var next = AddPeer(f, 3, visible: true); var hidden = AddPeer(f, 4, visible: false);
        var peerPhysical = delivery.Add(peer); var nextPhysical = delivery.Add(next); var hiddenPhysical = delivery.Add(hidden);
        var cause = new IOException("TASK36 peer rename encoder"); var failed = 0;
        if (failPeer) f.Sender.Outbound.Encoder.Fail((target, _) => { if (!ReferenceEquals(target, peer)) return null; failed++; return cause; });
        await Send(f, 0); await delivery.Settled();
        if (failPeer) await peerPhysical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(failPeer ? 1 : 0, failed); Assert.Equal(failPeer ? 1 : 0, peerPhysical.CloseCalls);
        Assert.Equal(failPeer ? 0 : 1, peerPhysical.Sends.Count); Assert.Single(nextPhysical.Sends); Assert.Empty(hiddenPhysical.Sends);
        Assert.Equal(new ushort[] { 0x65, 0x64, 0x0E }, Split(owner.Sends).Select(Type));
        Assert.Equal(0, owner.CloseCalls); Assert.Equal(0, nextPhysical.CloseCalls); Assert.Equal(0, hiddenPhysical.CloseCalls);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection)); Assert.Equal(1, f.Count(25));
        output.WriteLine($"old={Environment.GetEnvironmentVariable("TASK36_EXPECT_ROUTE") == "legacy"} failPeer={failPeer} nextSent=1 ownerFrames=65,64,0E ownerBlocked=False peerClose={peerPhysical.CloseCalls}");
    }
    [Fact]
    public async Task Shared_effect_delivers_self_ack_absolute_inventory_effect_before_visible_exact_peers()
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        using var f = await Create(2, delivery); var owner = delivery.Add(f.Connection);
        var peer = delivery.Add(AddPeer(f, 2, true)); var next = delivery.Add(AddPeer(f, 3, true)); var hidden = delivery.Add(AddPeer(f, 4, false));
        await Send(f, 2); await delivery.Settled();
        Assert.Equal(new ushort[] { 0xFB, 0x0E, 0xDB }, Split(owner.Sends).Select(Type));
        Assert.Equal((ushort)0xDB, Type(Assert.Single(Split(peer.Sends)))); Assert.Equal((ushort)0xDB, Type(Assert.Single(Split(next.Sends)))); Assert.Empty(hidden.Sends);
        Assert.Same(f.Connection, delivery.Recorder.Entries.First().Connection);
        Assert.All(delivery.Reports, x => Assert.Equal(OutboundOutcome.Delivered, x.Outcome));
        Assert.Equal(2, f.Count(1000));
    }

    private static NetworkConnectionContext AddPeer(ShopFixture f, int id, bool visible)
    {
        f.Sql($"INSERT INTO accounts(account_id,m_id) VALUES({id},'peer{id}'); INSERT INTO characters(character_id,account_id,name,job,level,slot_index) VALUES({id + 1},{id},'peer{id}',0,50,0)");
        var connection = new NetworkConnectionContext(new("task36-peer-" + id), NetworkEndpointKind.Game, 10011);
        Dfo.Server.Testing.TestGameplay.Run(f.Services, () => {
            var registry = f.Services.GetRequiredService<OnlineStateRegistry>(); var online = registry.RegisterConnection(connection); registry.Authenticate(online.OnlineUserId, new(id));
            var session = SelectedSessionIdentity.Create(new(id), new(id + 1), connection.ConnectionId, registry.GetRequiredUser(online.OnlineUserId).Generation, 10011);
            registry.Select(session); registry.EnterWorld(session);
            f.Services.GetRequiredService<ITownAreaDomainService>().Enter(new(session, new(1, 0, 0), new(0, 0, 0, 0, 0), visible));
        });
        f.Sender.Outbound.Connections.Add(connection); return connection;
    }

    internal static async Task<ShopFixture> Create(int protocol, RemainingOpeningProtocolCommandFlowTests.SenderProbe? delivery = null,
        Action<IServiceCollection>? configure = null)
    {
        var f = new ShopFixture(s => {
            s.AddSingleton<IItemPlacementDefinitionSource, Placements>();
            s.AddSingleton<ICreatureRuleSource, Rules>();
            s.AddSingleton<IItemUseRuleDefinitionSource>(new FakeItemUseRules((1000, ItemUseRules.Potion(0) with { SharedEffectSelector = 0 })));
            if (delivery is not null) s.AddSingleton<IOutboundSender>(delivery.Recorder);
            configure?.Invoke(s);
        });
        if (protocol is 0 or 1)
            Dfo.Server.Testing.TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(f.Session.CharacterId,
                new Dfo.Server.Application.Inventory.PersistentItemContainer(ContainerId.ForCharacter(f.Session.CharacterId, InventoryContainerType.Pet), new(InventoryContainerType.Pet, 0),
                    f.Services.GetRequiredService<IItemDomainStore>(), f.Services.GetRequiredService<Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor>(),
                    f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>())));
        if (protocol == 0) { await f.SeedCore(189, StackableItemCore.Create(7, 25, 2), InventoryContainerType.Pet); await f.SeedCore(25, StackableItemCore.Create(5, 63023, 500), InventoryContainerType.Equipment); }
        if (protocol == 1) await f.SeedCore(0, StackableItemCore.Create(5, 63022, 501), InventoryContainerType.Pet);
        if (protocol == 2) await f.Seed(69, 1000, 3);
        return f;
    }
    internal static ValueTask Send(ShopFixture f, int protocol, bool invalid = false, ushort sequence = 7)
    {
        byte[] body;
        ushort type;
        if (protocol == 0) { type = 0x64; using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); writer.Write((short)(invalid ? 0 : 189)); writer.Write((byte)7); writer.Write(1); writer.Write((byte)65); body = stream.ToArray(); }
        else if (protocol == 1) { type = 0x66; body = [7, (byte)(invalid ? 140 : 0), 0]; }
        else if (protocol == 2) { type = 0xFB; body = new byte[6]; BinaryPrimitives.WriteInt32LittleEndian(body, invalid ? 1001 : 1000); BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4), 69); }
        else { type = 0x41; body = [1, 2, 3]; }
        return EffectRequestTransport.SendRaw(f, new(1, type, 0, sequence), body);
    }
    private static EffectOperationEntry Entry(ShopFixture f)
    {
        var ledger = f.Services.GetRequiredService<EffectOperationLedger>();
        var table = typeof(EffectOperationLedger).GetField("ledgers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ledger)!;
        object?[] args = [f.Connection, null]; Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!);
        return Assert.Single(((IDictionary)args[1]!.GetType().GetProperty("Entries")!.GetValue(args[1])!).Values.Cast<EffectOperationEntry>());
    }
    private static IEnumerable<byte[]> Split(IEnumerable<byte[]> batches)
    {
        foreach (var bytes in batches)
            for (var offset = 0; offset < bytes.Length;)
            {
                var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 3)));
                yield return bytes[offset..(offset + length)]; offset += length;
            }
    }
    private static ushort Type(byte[] frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1));
    private sealed class Placements : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<ItemPlacement?>(id switch {
            63022 or 63023 => new(5, 1, new(BusinessInventorySpace.CreatureInventory, 0, 139)) { WorldClass = ItemWorldClass.Creature },
            25 => new(7, 100, new(BusinessInventorySpace.CreatureConsumable, 0, 50)),
            _ => new(2, 100, new(BusinessInventorySpace.Consumable, 0, 55)) });
    }
    private sealed class Rules : ICreatureRuleSource
    {
        public ValueTask<CreatureRule?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<CreatureRule?>(new(id, 20, 50, true, [2, 5], [], id == 63022 ? 63023 : null, id == 63022));
        public ValueTask<CreatureArtifactExperienceRule?> GetArtifactExperienceAsync(int id, CancellationToken ct) => ValueTask.FromResult<CreatureArtifactExperienceRule?>(null);
    }
}
