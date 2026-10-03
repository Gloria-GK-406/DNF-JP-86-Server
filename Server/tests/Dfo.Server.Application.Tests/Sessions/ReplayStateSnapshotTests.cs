using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Infrastructure.Configuration;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class ReplayStateSnapshotTests
{
    [Fact]
    public async Task Another_selected_settlement_cannot_commit_between_the_replay_read_and_its_batch_delivery()
    {
        using var fixture = new ShopFixture(services =>
        {
            services.AddSingleton<INetworkMessageSender, ReplayBatchSender>();
            services.AddCommandHandler<AddGoldProbe, GoldProbeResult, AddGoldProbeHandler>(CommandExecutionKind.Transactional);
        });
        var sender = (ReplayBatchSender)fixture.Services.GetRequiredService<INetworkMessageSender>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sender.BeforeSend = async () => { entered.SetResult(); await finish.Task; };
        var replay = fixture.Services.GetRequiredService<IReplayStateProjectionDelivery>().RefreshAsync(fixture.Connection,
            fixture.Session, new(ReplayProjectionNeeds.Inventory, []), default).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Direct invocation runs through Capture/45 immediately. This is a different async flow
        // from replay, and uses the real selected pipeline/transaction/SQLite gold table.
        var newer = fixture.Services.GetRequiredService<ICommandBus>().SendAsync(new AddGoldProbe(fixture.Session)).AsTask();
        try
        {
            Assert.False(newer.IsCompleted);
            Assert.Equal(100000, fixture.Gold);
        }
        finally { finish.TrySetResult(); await replay.WaitAsync(TimeSpan.FromSeconds(5)); await newer.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.Equal(100100, fixture.Gold);
        Assert.Equal(100000, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(sender.Batches[0][0].AsSpan(26)));
        Assert.Equal(0, fixture.Services.GetRequiredService<SelectedSessionTransitionGate>().TrackedEntryCount);
    }

    [Fact]
    public async Task Registered_delivery_reads_fresh_inventory_and_keeps_the_exact_connection_and_exclusive_lease_through_batch()
    {
        using var fixture = new ShopFixture(services => services.AddSingleton<INetworkMessageSender, ReplayBatchSender>());
        var sender = (ReplayBatchSender)fixture.Services.GetRequiredService<INetworkMessageSender>();
        var delivery = fixture.Services.GetRequiredService<IReplayStateProjectionDelivery>();
        var gate = fixture.Services.GetRequiredService<SelectedSessionTransitionGate>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sender.BeforeSend = async () => { entered.SetResult(); await finish.Task; };
        await fixture.Seed(69, 1000, 2);
        var pending = delivery.RefreshAsync(fixture.Connection, fixture.Session, new(ReplayProjectionNeeds.Inventory, []), default).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task exclusive;
        using (ExecutionContext.SuppressFlow())
            exclusive = Task.Run(async () => { await using var held = await gate.AcquireAsync(fixture.Session.AccountId, UserAccessMode.Exclusive, default); });
        Assert.False(exclusive.IsCompleted);
        finish.SetResult(); await pending; await exclusive.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(fixture.Connection, sender.Connection);
        Assert.Equal(6, Assert.Single(sender.Batches).Length);
        Assert.All(sender.Batches[0], x => Assert.Equal(0, x[0]));
        await fixture.Seed(69, 1000, 5); sender.BeforeSend = null;
        await delivery.RefreshAsync(fixture.Connection, fixture.Session, new(ReplayProjectionNeeds.Inventory, []), default);
        Assert.NotEqual(sender.Batches[0][0], sender.Batches[1][0]);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await delivery.RefreshAsync(
            fixture.Connection with { }, fixture.Session, new(ReplayProjectionNeeds.Inventory, []), default));
        Assert.Equal(2, sender.Batches.Count); Assert.Equal(0, gate.TrackedEntryCount);
        await using (var exclusiveOwner = await gate.BorrowOrAcquireExclusiveAsync(fixture.Session.AccountId, default))
            await delivery.RefreshAsync(fixture.Connection, fixture.Session, new(ReplayProjectionNeeds.Inventory, []), default);
        await using (var sharedOwner = await gate.BorrowOrAcquireSharedAsync(fixture.Session.AccountId, default))
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await delivery.RefreshAsync(fixture.Connection,
                fixture.Session, new(ReplayProjectionNeeds.Inventory, []), default));
        Assert.Equal(3, sender.Batches.Count); Assert.Equal(0, gate.TrackedEntryCount);
    }

    [SkippableFact]
    public async Task Appearance_only_refresh_completes_equipped_creature_marker_and_encodes_two_current_frames_without_entry_maintenance()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is unavailable.");
        await using var fixture = new LearningFixture(configure: services =>
        {
            new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
            services.AddSingleton<INetworkMessageSender, ReplayBatchSender>();
        });
        await fixture.SeedAsync();
        fixture.Sql("""
            INSERT INTO character_condition(character_id,pc_room_state) VALUES(71,0);
            INSERT INTO character_stats(character_id) VALUES(71);
            INSERT INTO character_pvp(character_id) VALUES(71);
            INSERT INTO character_settings(character_id) VALUES(71);
            INSERT INTO character_expert_job(character_id) VALUES(71);
            """);
        var store = fixture.Services.GetRequiredService<IItemDomainStore>();
        var equipment = new PersistentItemContainer(ContainerId.ForCharacter(fixture.Session.CharacterId, InventoryContainerType.Equipment), new(InventoryContainerType.Equipment, 0), store, fixture.Scope,
            fixture.Services.GetRequiredService<IItemPlacementRuleService>(),
            fixture.Services.GetRequiredService<IItemSlotRuleService>());
        await using (var tx = fixture.Scope.Begin())
        {
            var session = store.SessionIn(fixture.Scope.Current!);
            await session.PutAsync(equipment.Id, 25, await session.CreateAsync(ItemSnapshot.FromCore(
                StackableItemCore.Create(5, 63000, 1), false, creature: new(50, 1, 0, 0, 0, 1, [65], 0)), default), default);
            await tx.CommitAsync(default);
        }
        var before = fixture.Scalar("SELECT id FROM item_slots WHERE owner_id=71 AND list_type=3 AND slot_index=25");
        var snapshot = await fixture.Services.GetRequiredService<ICommandBus>().SendAsync(new ReadReplayStateCommand(fixture.Session,
            new(ReplayProjectionNeeds.CharacterAppearance, [])));
        var pet = snapshot.Inventory!.Character().Containers.Single(x => x.ContainerType == InventoryContainerType.Equipment).Items.Single();
        Assert.NotNull(pet.Noti2CreatureMarker);
        var connection = fixture.Services.GetRequiredService<SelectedSessionSafety>().Capture(fixture.Session).Connection!;
        await fixture.Services.GetRequiredService<IReplayStateProjectionDelivery>().RefreshAsync(connection, fixture.Session,
            new(ReplayProjectionNeeds.CharacterAppearance, []), default);
        var sender = (ReplayBatchSender)fixture.Services.GetRequiredService<INetworkMessageSender>();
        var frames = Assert.Single(sender.Batches); Assert.Equal(2, frames.Length);
        Assert.All(frames, frame => { Assert.Equal(0, frame[0]); Assert.Equal(2, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1))); });
        Assert.Equal(before, fixture.Scalar("SELECT id FROM item_slots WHERE owner_id=71 AND list_type=3 AND slot_index=25"));
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM item_slots WHERE owner_id=71 AND list_type=3 AND slot_index=24"));
    }

    [Fact]
    public async Task Inventory_only_reads_current_sqlite_without_requesting_a_skill_ledger()
    {
        // ShopFixture's ledger throws. An inventory projection must not read that unrelated fact.
        using var fixture = new ShopFixture();
        await fixture.Seed(69, 1000, 2);
        var first = await Read(fixture, ReplayProjectionNeeds.Inventory);
        await fixture.Seed(69, 1000, 5);
        var current = await Read(fixture, ReplayProjectionNeeds.Inventory);
        Assert.Equal(2, MainItem(first).Core.Value);
        Assert.Equal(5, MainItem(current).Core.Value);
        Assert.Null(current.Skills); Assert.Null(current.Points);
        Assert.Equal(fixture.Session, current.Session);
        Assert.Equal(fixture.Clock.GetUtcNow(), current.At);
        Assert.Equal(5, fixture.Count(1000));
        Assert.Equal(100000, fixture.Gold);
    }

    [Fact]
    public async Task Wrong_owner_stale_session_unknown_needs_and_nonphysical_slots_cannot_read()
    {
        using var fixture = new ShopFixture(); var bus = fixture.Services.GetRequiredService<ICommandBus>();
        var original = fixture.Session;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await bus.SendAsync(
            new ReadReplayStateCommand(original with { AccountId = new(999) }, new(ReplayProjectionNeeds.Inventory, []))));
        fixture.Select(2);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await bus.SendAsync(
            new ReadReplayStateCommand(original, new(ReplayProjectionNeeds.Inventory, []))));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await bus.SendAsync(
            new ReadReplayStateCommand(fixture.Session, new((ReplayProjectionNeeds)1024, []))));
        await Assert.ThrowsAsync<InventorySlotMappingException>(async () => await bus.SendAsync(
            new ReadReplayStateCommand(fixture.Session, new(ReplayProjectionNeeds.Inventory,
                [new(BusinessInventorySpace.VirtualResource, 0)]))));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await bus.SendAsync(
            new ReadReplayStateCommand(fixture.Session, new(ReplayProjectionNeeds.Inventory,
                [new(BusinessInventorySpace.GuardianGem, 0)]))));
        Assert.Equal(100000, fixture.Gold);
    }

    [Fact]
    public async Task Replacement_while_waiting_for_lifecycle_lease_rejects_the_original_read()
    {
        using var fixture = new ShopFixture(); var gate = fixture.Services.GetRequiredService<SelectedSessionTransitionGate>();
        var original = fixture.Session;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task holder;
        using (ExecutionContext.SuppressFlow())
            holder = Task.Run(async () =>
            {
                await using var exclusive = await gate.AcquireAsync(original.AccountId, UserAccessMode.Exclusive, default);
                held.SetResult(); await release.Task;
            });
        await held.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            // Invoke on this flow: Capture runs synchronously before the first 45 lease await.
            // The exclusive holder is a different flow, so this cannot accidentally borrow it.
            var pending = fixture.Services.GetRequiredService<ICommandBus>().SendAsync(
                new ReadReplayStateCommand(original, new(ReplayProjectionNeeds.Inventory, []))).AsTask();
            Assert.False(pending.IsCompleted);
            fixture.Select(2); release.SetResult();
            await Assert.ThrowsAsync<InvalidOperationException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { release.TrySetResult(); await holder.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.Equal(0, gate.TrackedEntryCount);
    }

    [Fact]
    public async Task Inventory_and_account_balances_are_read_from_one_real_sqlite_snapshot()
    {
        var barriers = new ReadBarriers();
        using var fixture = new ShopFixture(services => services.AddSingleton<ICharacterInventoryEntryRepository>(provider =>
            new DelayedInventory(provider.GetRequiredService<InventoryEntryAssembly>(), barriers)));
        var read = Read(fixture, ReplayProjectionNeeds.Inventory);
        await barriers.Read.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            fixture.Sql("UPDATE character_stats SET carried_gold=200000 WHERE character_id=1");
            await fixture.SeedShared(3262, 7);
            barriers.Release.TrySetResult();
            var snapshot = await read.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(100000, snapshot.Inventory!.Character().VirtualResources.Single(x => x.SlotIndex == 0).Value);
            Assert.Equal(0, snapshot.Inventory.Account().VirtualResources.SingleOrDefault(x => x.ItemId == 3262)?.Value ?? 0);
            var current = await Read(fixture, ReplayProjectionNeeds.Inventory);
            Assert.Equal(200000, current.Inventory!.Character().VirtualResources.Single(x => x.SlotIndex == 0).Value);
            Assert.Equal(7, current.Inventory.Account().VirtualResources.Single(x => x.ItemId == 3262).Value);
        }
        finally { barriers.Release.TrySetResult(); await read.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task Direct_snapshot_without_a_transaction_is_refused()
    {
        using var fixture = new ShopFixture();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Services
            .GetRequiredService<IReplayStateDomainService>().ReadAsync(fixture.Session,
                new(ReplayProjectionNeeds.Inventory, []), default));
    }

    [Fact]
    public async Task A_late_current_read_failure_preserves_all_stored_inventory_and_balances()
    {
        using var fixture = new ShopFixture(services => services.AddSingleton<ICharacterInventoryEntryRepository>(provider =>
            new ThrowingInventory(provider.GetRequiredService<InventoryEntryAssembly>())));
        await fixture.Seed(69, 1000, 2);
        await Assert.ThrowsAsync<IOException>(() => Read(fixture, ReplayProjectionNeeds.Inventory));
        Assert.Equal(2, fixture.Count(1000)); Assert.Equal(100000, fixture.Gold);
        Assert.Equal(0, fixture.Services.GetRequiredService<SelectedSessionTransitionGate>().TrackedEntryCount);
    }

    [Fact]
    public async Task One_captured_at_keeps_fatigue_item_and_premium_periods_consistent_across_awaited_day_change()
    {
        var clock = new Clock(); var rules = new UsageRules();
        await using var fixture = new LearningFixture(configure: services =>
        {
            services.AddSingleton<TimeProvider>(clock); services.AddSingleton<IItemUsageRuleSource>(rules);
            services.AddSingleton<IServerConfigService>(new ServerConfigService(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Premium:BlackDiamondEnabled"] = "true",
                    ["Premium:BlackDiamondExtraFatigue"] = "32", ["Fatigue:BaseTotal"] = "156" }).Build()));
        });
        await fixture.SeedAsync();
        await using (var tx = fixture.Scope.Begin())
        {
            await fixture.Services.GetRequiredService<ICharacterFatigueDomainService>().SpendAsync(fixture.Session.CharacterId, 7, default);
            await fixture.Services.GetRequiredService<IItemUsageDomainService>().RecordUseAsync(fixture.Session.CharacterId, 1, default);
            await fixture.Services.GetRequiredService<IPremiumServiceUsageTable>().TryWriteAsync(fixture.Session.CharacterId,
                "lottery_double_reward_used", null, 1, clock.Now, default);
            await tx.CommitAsync(default);
        }
        var at = clock.Now;
        rules.BeforeRead = () => clock.Now = at.AddMinutes(2);
        var state = await fixture.Services.GetRequiredService<ICommandBus>().SendAsync(new ReadReplayStateCommand(fixture.Session,
            new(ReplayProjectionNeeds.Fatigue | ReplayProjectionNeeds.ItemUsage | ReplayProjectionNeeds.Premium, [])));
        Assert.Equal(at, state.At); Assert.Equal(at.AddMinutes(2), clock.Now);
        Assert.Equal(7, state.Fatigue!.Value.Used);
        Assert.Equal(1, Assert.Single(state.ItemUsage).UsedCount);
        Assert.Equal(1, state.PremiumUsage.Single(x => x.SlotIndex == 1).UsedCount);
        Assert.Equal(at.AddDays(365).ToUnixTimeSeconds(), state.Premiums.Single(x => x.PremiumType == 56).ExpiresAtUnixSeconds);
        // All three lazy reads stayed pure: a later current-day query resets the view, not old stored rows.
        Assert.Equal(7, (await fixture.Services.GetRequiredService<ICharacterFatigueTable>().ReadAsync(fixture.Session.CharacterId, default))!.Used);
        Assert.Equal(1, (await fixture.Services.GetRequiredService<IConsumableUsageTable>().ReadAsync(fixture.Session.CharacterId, 1, default))!.UsedCount);
        Assert.Equal(0, (await fixture.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(fixture.Session.CharacterId, default)).Used);
    }

    [SkippableFact]
    public async Task Current_inventory_snapshot_keeps_legacy_worn_rows_while_selection_load_normalizes_once()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is unavailable.");
        await using var fixture = new LearningFixture(useArchive: true); await fixture.SeedAsync();
        var store = fixture.Services.GetRequiredService<IItemDomainStore>();
        var equipment = new PersistentItemContainer(ContainerId.ForCharacter(fixture.Session.CharacterId, InventoryContainerType.Equipment), new(InventoryContainerType.Equipment, 0), store, fixture.Scope,
            fixture.Services.GetRequiredService<IItemPlacementRuleService>(),
            fixture.Services.GetRequiredService<IItemSlotRuleService>());
        await using (var tx = fixture.Scope.Begin())
        {
            var session = store.SessionIn(fixture.Scope.Current!);
            await session.PutAsync(equipment.Id, 24, await session.CreateAsync(ItemSnapshot.FromCore(
                StackableItemCore.Create(5, 63000, 1), false, creature: new(50, 1, 0, 0, 0, 1, [65], 0)), default), default);
            await tx.CommitAsync(default);
        }
        var identity = fixture.Scalar("SELECT id FROM item_slots WHERE owner_id=71 AND list_type=3 AND slot_index=24");
        var snapshot = await fixture.Services.GetRequiredService<ICommandBus>().SendAsync(new ReadReplayStateCommand(fixture.Session,
            new(ReplayProjectionNeeds.Inventory, [])));
        Assert.Equal(24, snapshot.Inventory!.Character().Containers.Single(x => x.ContainerType == InventoryContainerType.Equipment).Items.Single().SlotIndex);
        Assert.Equal(identity, fixture.Scalar("SELECT id FROM item_slots WHERE owner_id=71 AND list_type=3 AND slot_index=24"));
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM item_slots WHERE owner_id=71 AND list_type=3 AND slot_index=25"));
        await using (var tx = fixture.Scope.Begin())
        {
            var entry = await fixture.Services.GetRequiredService<ICharacterInventoryEntryDomainService>()
                .LoadAsync(fixture.Session.AccountId, fixture.Session.CharacterId, default);
            Assert.Equal(25, entry.Containers.Single(x => x.ContainerType == InventoryContainerType.Equipment).Items.Single().SlotIndex);
            await tx.CommitAsync(default);
        }
        Assert.Equal(identity, fixture.Scalar("SELECT id FROM item_slots WHERE owner_id=71 AND list_type=3 AND slot_index=25"));
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM item_slots WHERE owner_id=71 AND list_type=3 AND slot_index=24"));
    }

    private static Task<ReplayStateSnapshot> Read(ShopFixture f, ReplayProjectionNeeds needs) =>
        f.Services.GetRequiredService<ICommandBus>().SendAsync(new ReadReplayStateCommand(f.Session, new(needs, []))).AsTask();
    private static InventoryItemState MainItem(ReplayStateSnapshot s) => s.Inventory!.Character().Containers
        .Single(x => x.ContainerType == InventoryContainerType.Main).Items.Single();
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 9, 29, 5, 59, 59, TimeSpan.FromHours(8));
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class UsageRules : IItemUsageRuleSource
    {
        public Action? BeforeRead;
        public ValueTask<ItemUsageRule?> GetAsync(int id, CancellationToken ct)
        { BeforeRead?.Invoke(); return ValueTask.FromResult<ItemUsageRule?>(new(3, 0, true)); }
    }
    private sealed class ReadBarriers
    {
        public TaskCompletionSource Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class DelayedInventory(ICharacterInventoryEntryRepository inner, ReadBarriers barriers) : ICharacterInventoryEntryRepository
    {
        public async ValueTask<CharacterInventoryEntryStorageRecord> LoadCharacterAsync(Dfo.Server.Data.Accounts.AccountId account,
            CharacterId character, CancellationToken ct)
        {
            var result = await inner.LoadCharacterAsync(account, character, ct);
            barriers.Read.TrySetResult(); await barriers.Release.Task.WaitAsync(ct); return result;
        }
    }
    private sealed class ThrowingInventory(ICharacterInventoryEntryRepository inner) : ICharacterInventoryEntryRepository
    {
        public async ValueTask<CharacterInventoryEntryStorageRecord> LoadCharacterAsync(Dfo.Server.Data.Accounts.AccountId account,
            CharacterId character, CancellationToken ct)
        { await inner.LoadCharacterAsync(account, character, ct); throw new IOException("late current inventory read"); }
    }
    // Exercise the registered Protocol delivery, command bus and actual encoders. Socket registry,
    // cap validation and captured transport are separately exercised by PreparedBatchTests.
    private sealed class ReplayBatchSender(IServiceProvider provider) : INetworkMessageSender
    {
        public List<byte[][]> Batches { get; } = [];
        public NetworkConnectionContext? Connection { get; private set; }
        public Func<Task>? BeforeSend { get; set; }
        public async ValueTask SendBatchAsync(NetworkConnectionContext expected, IReadOnlyList<IServerNetworkMessage> messages, CancellationToken ct)
        {
            var registrations = provider.GetServices<ServerMessageEncoderRegistration>().ToDictionary(x => x.MessageType);
            var frames = messages.SelectMany(message => ((IServerMessageEncoder)provider.GetRequiredService(registrations[message.GetType()].EncoderType))
                .Encode(message)).Select(x => x.ToArray()).ToArray();
            if (BeforeSend is not null) await BeforeSend();
            Connection = expected; Batches.Add(frames);
        }
        public ValueTask SendAsync(NetworkConnectionId id, IServerNetworkMessage message, CancellationToken ct) =>
            ValueTask.FromException(new NotSupportedException("Replay must use the typed batch."));
        public ValueTask SendExplicitAsync(NetworkConnectionId id, ExplicitMessageDelivery delivery, CancellationToken ct) =>
            ValueTask.FromException(new NotSupportedException("Replay must not repeat explicit historical delivery."));
    }
    private sealed record AddGoldProbe(Dfo.Server.Data.World.SelectedSessionIdentity Session)
        : ICommand<GoldProbeResult>, ISelectedSessionCommand, IAccountCommand
    {
        public Dfo.Server.Data.Accounts.AccountId AccountId => Session.AccountId;
    }
    private sealed record GoldProbeResult(int Gold);
    private sealed class AddGoldProbeHandler(ICharacterCarriedGoldTable gold) : ICommandHandler<AddGoldProbe, GoldProbeResult>
    {
        public async ValueTask<GoldProbeResult> HandleAsync(CommandExecutionContext context, AddGoldProbe command, CancellationToken ct) =>
            new(await gold.AddAsync(command.Session.CharacterId, 100, int.MaxValue, ct));
    }
}
