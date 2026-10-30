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
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class ReplayStateSnapshotTests
{
    [Fact]
    public async Task Another_selected_settlement_cannot_commit_between_the_replay_read_and_its_batch_delivery()
    {
        using var fixture = new ShopFixture(services =>
        {
            services.AddSingleton<INetworkMessageSender, ReplayBatchSender>();
            services.AddFixtureCommand<AddGoldProbe, GoldProbeResult, AddGoldProbeHandler>(FixtureUnitKind.Transactional);
        });
        var sender = (ReplayBatchSender)fixture.Services.GetRequiredService<INetworkMessageSender>();
        // The replay (read and batch) is one CMD and the settlement another: the settlement runs after the batch.
        await TestGameplay.RunAsync(fixture.Services, async () =>
            await new ReplayFixture(fixture.Services).RefreshAsync(fixture.Connection,
            fixture.Session, new(ReplayProjectionNeeds.Inventory, []), null, default));
        Assert.Equal(100000, fixture.Gold);
        await DomainCommandFixture.Bus(fixture.Services).SendAsync(new AddGoldProbe(fixture.Session)).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(100100, fixture.Gold);
        Assert.Equal(100000, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(sender.Batches[0][0].AsSpan(26)));
    }

    [Fact]
    public async Task Registered_delivery_reads_fresh_inventory_and_keeps_the_exact_connection_through_its_batch()
    {
        using var fixture = new ShopFixture(services => services.AddSingleton<INetworkMessageSender, ReplayBatchSender>());
        var sender = (ReplayBatchSender)fixture.Services.GetRequiredService<INetworkMessageSender>();
        var delivery = new ReplayFixture(fixture.Services);
        await fixture.Seed(69, 1000, 2);
        // The delivery runs as the CMD it belongs to in production; no other CMD can take the lease mid-batch.
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
        await delivery.RefreshAsync(fixture.Connection, fixture.Session, new(ReplayProjectionNeeds.Inventory, []), null, default);
        Assert.Same(fixture.Connection, sender.Connection);
        Assert.Equal(6, Assert.Single(sender.Batches).Length);
        Assert.All(sender.Batches[0], x => Assert.Equal(0, x[0]));
        await fixture.Seed(69, 1000, 5);
        await delivery.RefreshAsync(fixture.Connection, fixture.Session, new(ReplayProjectionNeeds.Inventory, []), null, default);
        Assert.NotEqual(sender.Batches[0][0], sender.Batches[1][0]);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await delivery.RefreshAsync(
            fixture.Connection with { }, fixture.Session, new(ReplayProjectionNeeds.Inventory, []), null, default));
        Assert.Equal(2, sender.Batches.Count);
        await delivery.RefreshAsync(fixture.Connection, fixture.Session, new(ReplayProjectionNeeds.Inventory, []), null, default);
        Assert.Equal(3, sender.Batches.Count);
        });
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Appearance_only_refresh_completes_equipped_creature_marker_and_encodes_two_current_frames_without_entry_maintenance(bool inDungeon)
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is unavailable.");
        await using var fixture = new LearningFixture(configure: services =>
        {
            new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
            RecordedOutbound.AddTo(services);
            services.AddSingleton<INetworkMessageSender, ReplayBatchSender>();
            services.AddSingleton<Dfo.Server.Abstractions.World.IDungeonParticipationDomainService>(
                TestRequired.Absent<Dfo.Server.Abstractions.World.IDungeonParticipationDomainService>((method, _) =>
                    method.Name == "Find" && inDungeon
                        ? new Dfo.Server.Abstractions.World.DungeonParticipationOwner(
                            Dfo.Server.Abstractions.World.DungeonParticipationMode.Ordinary, Guid.NewGuid())
                        : null));
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
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using (var tx = fixture.Scope.Begin())
            {
                var session = store.SessionIn(fixture.Scope.Current!);
                await session.PutAsync(equipment.Id, 25, await session.CreateAsync(ItemSnapshot.FromCore(
                    StackableItemCore.Create(5, 63000, 1), false, creature: new(50, 1, 0, 0, 0, 1, [65], 0)), default), default);
                await tx.CommitAsync(default);
            }
        });
        var before = fixture.Scalar("SELECT id FROM item_slots WHERE owner_id=71 AND list_type=3 AND slot_index=25");
        var snapshot = await new ReplayFixture(fixture.Services).ReadAsync(fixture.Session,
            new(ReplayProjectionNeeds.CharacterAppearance, []), null);
        var pet = snapshot.Inventory!.Character().Containers.Single(x => x.ContainerType == InventoryContainerType.Equipment).Items.Single();
        Assert.Equal(inDungeon ? 1 : 0, snapshot.Appearance!.Public().Identity!.UserState);
        Assert.NotNull(pet.Noti2CreatureMarker);
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            var connection = fixture.Services.GetRequiredService<SelectedSessionSafety>().Capture(fixture.Session, null).Connection!;
            await new ReplayFixture(fixture.Services).RefreshAsync(connection, fixture.Session,
                new(ReplayProjectionNeeds.CharacterAppearance, []), null, default);
        });
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
        using var fixture = new ShopFixture(); var bus = DomainCommandFixture.Bus(fixture.Services);
        var original = fixture.Session;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Dfo.Server.Application.Tests.Support.ReplayStateFixture.ReadAsync(fixture.Services,original with { AccountId = new(999) }, new(ReplayProjectionNeeds.Inventory, []), null));
        fixture.Select(2);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Dfo.Server.Application.Tests.Support.ReplayStateFixture.ReadAsync(fixture.Services,original, new(ReplayProjectionNeeds.Inventory, []), null));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await Dfo.Server.Application.Tests.Support.ReplayStateFixture.ReadAsync(fixture.Services,fixture.Session, new((ReplayProjectionNeeds)4096, []), null));
        await Assert.ThrowsAsync<InventorySlotMappingException>(async () => await Dfo.Server.Application.Tests.Support.ReplayStateFixture.ReadAsync(fixture.Services,fixture.Session, new(ReplayProjectionNeeds.Inventory,
                [new(BusinessInventorySpace.VirtualResource, 0)]), null));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await Dfo.Server.Application.Tests.Support.ReplayStateFixture.ReadAsync(fixture.Services,fixture.Session, new(ReplayProjectionNeeds.Inventory,
                [new(BusinessInventorySpace.GuardianGem, 0)]), null));
        Assert.Equal(100000, fixture.Gold);
    }

    [Fact]
    public async Task A_replacement_selected_before_the_original_read_runs_rejects_that_read()
    {
        using var fixture = new ShopFixture();
        var original = fixture.Session;
        // The replacement is a CMD that completes before the original read's CMD runs.
        fixture.Select(2);
        var pending = Dfo.Server.Application.Tests.Support.ReplayStateFixture.ReadAsync(fixture.Services,original, new(ReplayProjectionNeeds.Inventory, []), null).AsTask();
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Inventory_and_account_balances_are_read_from_one_real_sqlite_snapshot()
    {
        var barriers = new ReadBarriers();
        using var fixture = new ShopFixture(services => services.AddSingleton<ICharacterInventoryEntryRepository>(provider =>
            new DelayedInventory(provider.GetRequiredService<InventoryEntryAssembly>(), barriers)));
        // Staged on another account: the seed is a CMD and cannot run while the read's CMD is paused, so the
        // mid-read write below moves the row to account 1 with raw SQL instead.
        await fixture.SeedShared(3262, 7, account: 99);
        var read = Read(fixture, ReplayProjectionNeeds.Inventory);
        await barriers.Read.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            fixture.Sql("UPDATE character_stats SET carried_gold=200000 WHERE character_id=1");
            fixture.Sql("UPDATE item_slots SET owner_id=1 WHERE owner_scope='account' AND owner_id=99 AND list_type=240");
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
                new(ReplayProjectionNeeds.Inventory, []), null, default));
    }

    [Fact]
    public async Task A_late_current_read_failure_preserves_all_stored_inventory_and_balances()
    {
        using var fixture = new ShopFixture(services => services.AddSingleton<ICharacterInventoryEntryRepository>(provider =>
            new ThrowingInventory(provider.GetRequiredService<InventoryEntryAssembly>())));
        await fixture.Seed(69, 1000, 2);
        await Assert.ThrowsAsync<IOException>(() => Read(fixture, ReplayProjectionNeeds.Inventory));
        Assert.Equal(2, fixture.Count(1000)); Assert.Equal(100000, fixture.Gold);
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
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using (var tx = fixture.Scope.Begin())
            {
                await fixture.Services.GetRequiredService<ICharacterFatigueDomainService>().SpendAsync(fixture.Session.CharacterId, 7, default);
                await fixture.Services.GetRequiredService<IItemUsageDomainService>().RecordUseAsync(fixture.Session.CharacterId, 1, default);
                await fixture.Services.GetRequiredService<IPremiumServiceUsageTable>().TryWriteAsync(fixture.Session.CharacterId,
                    "lottery_double_reward_used", null, 1, clock.Now, default);
                await tx.CommitAsync(default);
            }
        });
        var at = clock.Now;
        rules.BeforeRead = () => clock.Now = at.AddMinutes(2);
        var state = await new ReplayFixture(fixture.Services).ReadAsync(fixture.Session,
            new(ReplayProjectionNeeds.Fatigue | ReplayProjectionNeeds.ItemUsage | ReplayProjectionNeeds.Premium, []), null);
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
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using (var tx = fixture.Scope.Begin())
            {
                var session = store.SessionIn(fixture.Scope.Current!);
                await session.PutAsync(equipment.Id, 24, await session.CreateAsync(ItemSnapshot.FromCore(
                    StackableItemCore.Create(5, 63000, 1), false, creature: new(50, 1, 0, 0, 0, 1, [65], 0)), default), default);
                await tx.CommitAsync(default);
            }
        });
        var identity = fixture.Scalar("SELECT id FROM item_slots WHERE owner_id=71 AND list_type=3 AND slot_index=24");
        var snapshot = await new ReplayFixture(fixture.Services).ReadAsync(fixture.Session,
            new(ReplayProjectionNeeds.Inventory, []), null);
        Assert.Equal(24, snapshot.Inventory!.Character().Containers.Single(x => x.ContainerType == InventoryContainerType.Equipment).Items.Single().SlotIndex);
        Assert.Equal(identity, fixture.Scalar("SELECT id FROM item_slots WHERE owner_id=71 AND list_type=3 AND slot_index=24"));
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM item_slots WHERE owner_id=71 AND list_type=3 AND slot_index=25"));
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using (var tx = fixture.Scope.Begin())
            {
                var entry = await fixture.Services.GetRequiredService<ICharacterInventoryEntryDomainService>()
                    .LoadAsync(fixture.Session.AccountId, fixture.Session.CharacterId, default);
                Assert.Equal(25, entry.Containers.Single(x => x.ContainerType == InventoryContainerType.Equipment).Items.Single().SlotIndex);
                await tx.CommitAsync(default);
            }
        });
        Assert.Equal(identity, fixture.Scalar("SELECT id FROM item_slots WHERE owner_id=71 AND list_type=3 AND slot_index=25"));
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM item_slots WHERE owner_id=71 AND list_type=3 AND slot_index=24"));
    }

    private static Task<ReplayStateSnapshot> Read(ShopFixture f, ReplayProjectionNeeds needs) =>
        new ReplayFixture(f.Services).ReadAsync(f.Session, new(needs, []), null).AsTask();
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
    // Exercise the registered Protocol delivery, command bus and actual encoders, now through the real registration
    // adapter and outbox (TASK-25): this forwards every call to it and reads back the prepared batches it registered.
    // A single send or an explicit delivery fails where it is registered, as the old double refused them. Socket
    // registry, cap validation and captured transport are separately exercised by PreparedBatchTests.
    private sealed class ReplayBatchSender : INetworkMessageSender
    {
        private readonly RecordedOutbound outbound;

        public ReplayBatchSender(RecordedOutbound outbound)
        {
            this.outbound = outbound;
            outbound.FailAny(_ => outbound.ExplicitInFlight is null && !outbound.BatchInFlight,
                () => new NotSupportedException("Replay must use the typed batch."));
            outbound.FailAny(_ => outbound.ExplicitInFlight is not null,
                () => new NotSupportedException("Replay must not repeat explicit historical delivery."));
        }

        public List<byte[][]> Batches => [.. outbound.Batches.Select(batch => batch.Frames)];
        public NetworkConnectionContext? Connection => outbound.Batches.LastOrDefault().Connection;
        public Func<Task>? BeforeSend { get; set; }
        public async ValueTask SendBatchAsync(NetworkConnectionContext expected, IReadOnlyList<IServerNetworkMessage> messages, CancellationToken ct)
        {
            if (BeforeSend is not null) await BeforeSend();
            await outbound.Network.SendBatchAsync(expected, messages, ct);
        }
        public ValueTask SendAsync(NetworkConnectionId id, IServerNetworkMessage message, CancellationToken ct) =>
            outbound.Network.SendAsync(id, message, ct);
        public ValueTask SendExplicitAsync(NetworkConnectionId id, ExplicitMessageDelivery delivery, CancellationToken ct) =>
            outbound.Network.SendExplicitAsync(id, delivery, ct);
    }
    private sealed class ReplayFixture(IServiceProvider services)
    {
        internal ValueTask<ReplayStateSnapshot> ReadAsync(Dfo.Server.Data.World.SelectedSessionIdentity session, ReplayProjectionRequest request,GameMessageMetadata? metadata) =>
            new(TestGameplay.RunAsync(services,async()=>{
                var safety=services.GetRequiredService<SelectedSessionSafety>();var recipient=safety.Capture(session,metadata);
                await using var tx=services.GetRequiredService<Dfo.Server.Application.Transactions.DomainTransactionScope>().Begin();
                var snapshot=await services.GetRequiredService<IReplayStateDomainService>().ReadAsync(session,request,metadata,default);
                safety.DemandCurrent(recipient);await tx.CommitAsync(default);return snapshot;
            }));
        internal async ValueTask RefreshAsync(NetworkConnectionContext connection,Dfo.Server.Data.World.SelectedSessionIdentity session,ReplayProjectionRequest request,GameMessageMetadata? metadata,CancellationToken ct)
        {
            var safety=services.GetRequiredService<SelectedSessionSafety>();var recipient=safety.Capture(session,metadata);
            if(!ReferenceEquals(connection,recipient.Connection))throw new InvalidOperationException("Replay requires its exact original connection.");
            var snapshot=await ReadAsync(session,request,metadata);safety.DemandCurrent(recipient);
            await services.GetRequiredService<IReplayStateProjectionSink>().ProjectAsync(connection,session,metadata,snapshot,null,ct);
        }
    }
    private sealed record AddGoldProbe(Dfo.Server.Data.World.SelectedSessionIdentity Session)
        : ICommand<GoldProbeResult>, IDomainFixtureParticipants
    {
        public Dfo.Server.Data.Accounts.AccountId AccountId => Session.AccountId;
        public IReadOnlyList<Dfo.Server.Data.World.SelectedSessionIdentity> Participants=>[Session];
    }
    private sealed record GoldProbeResult(int Gold);
    private sealed class AddGoldProbeHandler(ICharacterCarriedGoldTable gold) : ICommandHandler<AddGoldProbe, GoldProbeResult>
    {
        public async ValueTask<GoldProbeResult> HandleAsync(CommandExecutionContext context, AddGoldProbe command, CancellationToken ct) =>
            new(await gold.AddAsync(command.Session.CharacterId, 100, int.MaxValue, ct));
    }
}
