using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.ReviveCoin;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class ExplicitEffectExecutionTests
{
    private static readonly BusinessSlot Slot = new(BusinessInventorySpace.Consumable, 0);

    // Value gate: expiry while a real backing read is suspended must leave inventory untouched,
    // even if the caller catches the failure and commits. Final Record alone cannot ensure this.
    [Fact]
    public async Task Expiry_inside_consumption_after_real_read_prevents_take_and_preserves_committed_storage()
    {
        using var f = new ShopFixture();
        await f.Seed(65, 1000, 3);
        var takes = 0;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var scope = f.Services.GetRequiredService<DomainTransactionScope>();
            await using var tx = scope.Begin();
            using var cap = new TestEffectFootprints.Lease(f.Session);
            var gate = new ReadGate(f.Services.GetRequiredService<IBusinessInventorySlotDomainService>());
            var executor = new ApplyEffectsDomainService([new ConsumeFromContainerEffectHandler(gate,
                f.Services.GetRequiredService<IItemLifecycleDomainService>())], scope,
                f.Services.GetRequiredService<ICharacterDomainStore>(), f.Services.GetRequiredService<ISelectedSessionDomainService>(),
                f.Services.GetRequiredService<IInventoryEffectPreflightDomainService>());
            var pending = executor.ApplyAsync(Context(f, cap), [new ConsumeFromContainerEffect(Slot, 1000, 1)], default).AsTask();
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cap.Dispose();
            gate.Resume.SetResult();
            var error = await Record.ExceptionAsync(() => pending);
            takes = gate.Takes;
            await tx.CommitAsync(default);
            Assert.IsType<InvalidOperationException>(error);
        });
        Assert.Equal((0, 3), (takes, f.Count(1000)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expiry_during_apply_preflight_or_validation_prevents_the_first_business_write(bool validation)
    {
        using var f = new ShopFixture();
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var tx = f.Services.GetRequiredService<DomainTransactionScope>().Begin();
            using var cap = new TestEffectFootprints.Lease(f.Session);
            var gate = new Pause();
            var handler = new GatedGold(f.Services.GetRequiredService<ICharacterDomainService>(), validation ? gate : null);
            var inventory = new GatedPreview(f.Services.GetRequiredService<IInventoryEffectPreflightDomainService>(), validation ? null : gate);
            var executor = new ApplyEffectsDomainService([handler], f.Services.GetRequiredService<IDomainTransactionAccessor>(),
                f.Services.GetRequiredService<ICharacterDomainStore>(), f.Services.GetRequiredService<ISelectedSessionDomainService>(), inventory);
            var pending = executor.ApplyAsync(Context(f, cap), [new SpendGoldEffect(7)], default).AsTask();
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cap.Dispose(); gate.Resume.SetResult();
            var error = await Record.ExceptionAsync(() => pending);
            await tx.CommitAsync(default);
            Assert.Equal(0, handler.Writes);
            Assert.IsType<InvalidOperationException>(error);
        });
        Assert.Equal(100000, f.Gold);
    }

    [Fact]
    public async Task Real_dispatcher_apply_freeze_replay_and_rollback_keep_each_singletons_invocations_separate()
    {
        var witness = new Witness(); var replay = new Replay();
        using var f = Fixture(witness, replay);
        await f.Seed(65, 1000, 5);
        var dispatcher = f.Services.GetRequiredService<INetworkMessageDispatcher>();
        await dispatcher.DispatchAsync(new(f.Connection), Request(1, 1, 2), default);
        await dispatcher.DispatchAsync(new(f.Connection), Request(2, 1, 2, fresh: false), default);
        witness.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => dispatcher.DispatchAsync(new(f.Connection), Request(3, 2, 3), default).AsTask());
        Assert.Equal(4, f.Count(1000)); Assert.Equal(99993, f.Gold);
        await dispatcher.DispatchAsync(new(f.Connection), Request(4, 2, 3, fresh: false), default);
        await dispatcher.DispatchAsync(new(f.Connection), Request(5, 2, 3, fresh: false), default);
        Assert.Equal(3, f.Count(1000)); Assert.Equal(99986, f.Gold);
        Assert.Equal(3, witness.Caps.Distinct().Count());
        Assert.Equal(2, replay.Plans.Count);
        Assert.All(replay.Plans, plan =>
        {
            Assert.Equal(ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.Experience | ReplayProjectionNeeds.Skills, plan.Needs);
            Assert.Equal(Slot, Assert.Single(plan.TouchedSlots));
        });
        Assert.All(witness.Caps, cap => Assert.Throws<InvalidOperationException>(() => cap.RecordEffects(Owner(f), [])));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("main")]
    [InlineData("transaction")]
    [InlineData("frozen")]
    [InlineData("operation")]
    [InlineData("execution")]
    [InlineData("admission")]
    [InlineData("authorization")]
    [InlineData("lease")]
    [InlineData("rejected")]
    public async Task Captured_effects_authority_rejects_before_gold_even_for_an_empty_effect_list(string invalid)
    {
        using var f = Fixture(new Witness(), new Replay());
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var operations = f.Services.GetRequiredService<EffectOperationContext>();
            var authorization = f.Services.GetRequiredService<NetworkCommandAuthorization>();
            using var network = authorization.Enter(() => true, GameMessageMetadata.Empty, NetworkCommandAuthorization.MessageCategory.Effect);
            var context = new CommandExecutionContext(Guid.NewGuid(), typeof(Probe), typeof(ProbeHandler), CommandExecutionKind.Transactional);
            var command = new Probe(f.Session, 2); var entry = Entry(f.Session);
            using var admission = context.EnterLegacyInvocation(f.Services.GetRequiredService<CommandHandlerRegistry>().GetRegistration(typeof(Probe)));
            using var operation = invalid == "rejected" ? operations.EnterWithoutSelectedSession()
                : operations.Enter(entry, new(f.Session, f.Connection), ReplayProjectionNeeds.None);
            using var main = invalid == "rejected" ? null : operations.BeginMain(context, invalid == "main" ? new Ordinary() : command);
            await using var tx = f.Services.GetRequiredService<DomainTransactionScope>().Begin();
            if (invalid != "transaction" && invalid != "rejected") operations.TransactionStarted(context, command);
            using var cap = operations.CaptureLegacy(context, authorization.Capture(), false, () => { });
            if (invalid == "frozen") operations.Freeze(context, command);
            if (invalid == "operation") operation.Dispose();
            if (invalid == "execution") main!.Dispose();
            if (invalid == "admission") admission.Dispose();
            if (invalid == "authorization") network.Dispose();
            if (invalid == "lease") cap.Dispose();
            var target = Context(f, cap);
            if (invalid == "owner") target = target with { AccountId = new(99) };
            var executor = f.Services.GetRequiredService<IApplyEffectsDomainService>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ApplyAsync(target, [], default).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ApplyAsync(target, [new SpendGoldEffect(7)], default).AsTask());
            await tx.CommitAsync(default);
        });
        Assert.Equal(100000, f.Gold);
    }

    [Fact]
    public void Context_requires_non_null_capability_and_fact_getters_remain_usable_after_lease_closes()
    {
        using var f = new ShopFixture();
        Assert.Throws<ArgumentNullException>(() => Context(f, null!));
        TestGameplay.Run(f.Services, () =>
        {
            using var cap = new TestEffectFootprints.Lease(f.Session);
            var context = Context(f, cap); cap.Dispose();
            Assert.Equal(f.Session.AccountId, context.AccountId); Assert.Equal(f.Session.CharacterId, context.CharacterId);
            Assert.Equal(f.Session, context.Session); Assert.Equal(f.Clock.GetUtcNow(), context.At);
            Assert.Throws<InvalidOperationException>(() => context.Footprint.DemandEffects(context.Owner));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_non_request_can_apply_offline_or_with_a_session_and_preview_records_nothing(bool withSession)
    {
        using var f = new ShopFixture();
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            using var cap = new TestEffectFootprints.Lease(new EffectFootprintOwner(f.Session.AccountId, f.Session.CharacterId, null));
            var context = Context(f, cap) with { Session = withSession ? f.Session : null };
            await using var tx = f.Services.GetRequiredService<DomainTransactionScope>().Begin();
            await f.Services.GetRequiredService<IInventoryEffectPreflightDomainService>().PreviewAsync(context, [new SpendGoldEffect(7)], default);
            Assert.Equal(0, cap.RecordedEffects); Assert.Equal(100000, f.Gold);
            await f.Services.GetRequiredService<IApplyEffectsDomainService>().ApplyAsync(context, [new SpendGoldEffect(7)], default);
            Assert.Equal(1, cap.RecordedEffects); cap.Dispose();
            await tx.CommitAsync(default);
        });
        Assert.Equal(99993, f.Gold);
    }

    [Fact]
    public async Task Cera_quote_await_cannot_continue_to_the_wallet_or_gold_after_its_lease_closes()
    {
        var gate = new Pause();
        using var f = new CeraShopFixture(configure: services =>
            services.AddSingleton<ICeraShopRuleService>(provider => new QuoteGate(
                ActivatorUtilities.CreateInstance<Dfo.Server.Application.Inventory.CeraShopRuleService>(provider), gate)));
        await TestGameplay.RunAsync(f.Host.Services, async () =>
        {
            await using var tx = f.Host.Services.GetRequiredService<DomainTransactionScope>().Begin();
            using var cap = new TestEffectFootprints.Lease(f.Host.Session);
            var pending = f.Host.Services.GetRequiredService<ICeraShopDomainService>().PurchaseAsync(f.Host.Session,
                CeraShopTestContent.Input(CeraShopTestContent.Entry()), f.Host.Clock.GetUtcNow(), cap, default).AsTask();
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5)); cap.Dispose(); gate.Resume.SetResult();
            var error = await Record.ExceptionAsync(() => pending);
            await tx.CommitAsync(default);
            Assert.IsType<InvalidOperationException>(error);
        });
        Assert.Equal(100000, f.Cera); Assert.Equal(100000, f.Host.Gold);
        Assert.Equal(0, f.Host.Scalar("SELECT COUNT(*) FROM items"));
    }

    [Fact]
    public async Task Ordinary_Cera_and_daily_coin_requests_use_the_real_dispatchers_non_effect_source()
    {
        using var f = new CeraShopFixture();
        var dispatcher = f.Host.Services.GetRequiredService<INetworkMessageDispatcher>();
        await dispatcher.DispatchAsync(new(f.Host.Connection), new Dfo.Server.Protocol.Game.CeraShopPurchase.CeraShopPurchaseRequest(
            GameMessageMetadata.Empty, CeraShopTestContent.Input(CeraShopTestContent.Entry())), default);
        await dispatcher.DispatchAsync(new(f.Host.Connection), new DailyReviveCoinRequest(GameMessageMetadata.Empty), default);
        Assert.True(f.Cera < 100000);
        Assert.True(f.Host.Count(1) > 0);
    }

    [Fact]
    public async Task Actual_lifecycle_dispatcher_observer_cleanup_uses_explicit_origin_and_closes_its_non_request_lease()
    {
        var source = new LifecycleWitness();
        CleanupApply? cleanup = null;
        using var f = new ShopFixture(services =>
        {
            services.AddSingleton<ILifecycleEffectFootprintSource>(source);
            services.AddSingleton<ISelectedSessionCleanupDomainService>(provider => cleanup = new CleanupApply(
                provider.GetRequiredService<IApplyEffectsDomainService>(), provider.GetRequiredService<DomainTransactionScope>()));
        });
        var root = GameplayCallChain.NewRoot(GameplayCallSource.Connection, new("other-root-cause"));
        f.Services.GetRequiredService<GameplayCloseCauses>().Record(f.Connection, root);
        await f.Services.GetRequiredService<INetworkConnectionLifecycleDispatcher>().DispatchDisconnectedAsync(f.Connection, default);
        var origin = Assert.Single(source.Origins);
        Assert.Equal(GameplayCallSource.Lifecycle, origin.Kind); Assert.Null(origin.SourceConnection);
        Assert.Equal(root.RootId, origin.Chain.RootId); Assert.Equal(root.Id, origin.Chain.ParentId);
        Assert.Equal(root.SourceConnection, origin.Chain.SourceConnection);
        Assert.Equal(99993, f.Gold);
        Assert.NotNull(cleanup!.Cap);
        Assert.Throws<InvalidOperationException>(() => cleanup.Cap!.DemandEffects(Owner(f)));
        await TestGameplay.RunAsync(f.Services, () =>
        {
            Assert.Throws<InvalidOperationException>(() => cleanup.Cap!.DemandEffects(Owner(f)));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Lifecycle_factory_refuses_unknown_default_and_wrong_job_origins_and_is_no_command_injection_exemption()
    {
        var factory = new LifecycleEffectFootprintSource();
        var origin = new InternalCommandOrigin(GameplayCallSource.Lifecycle, GameplayCallChain.NewRoot(GameplayCallSource.Lifecycle), "cleanup");
        Assert.Throws<GameplayOffExecutorException>(() => factory.Open(origin));
        await TestGameplay.RunAsync(() =>
        {
            Assert.Throws<InvalidOperationException>(() => factory.Open(origin with { Chain = default }));
            Assert.Throws<InvalidOperationException>(() => factory.Open(origin));
            Assert.Throws<InvalidOperationException>(() => factory.Open(origin with { Kind = GameplayCallSource.Event }));
            new Dfo.Server.Application.Tests.Commands.CommandHandlerArchitectureTests()
                .ATypeOutsideTheVocabularyIsRefused(typeof(ILifecycleEffectFootprintSource));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Captured_projection_mapping_preserves_every_original_effect_family_slots_and_areas()
    {
        using var f = Fixture(new Witness(), new Replay());
        var placement = new ItemPlacement(5, 1, new(BusinessInventorySpace.CreatureInventory, 0, 1));
        var prepared = new OpeningPreparedGrant(new(3000, 1), new(3000, placement, 1, false, [], null, true), 0);
        var townBefore = new CharacterPositionState(1, 2, 3, 4, 5);
        var townAfter = new CharacterPositionState(2, 3, 4, 5, 6);
        var sourceArea = new TownAreaKey(1, 1, 2);
        var other = Slot with { Index = 1 };
        var equipment = new EquipmentEffectChange(Slot, null!, null);
        (DomainEffect Effect, EffectChange Change, ReplayProjectionNeeds Needs, BusinessSlot[] Slots)[] cases =
        [
            (new ConsumeFromContainerEffect(Slot, 1000, 1), new ContainerConsumptionChange(Slot, 1000, 1, 2), ReplayProjectionNeeds.Inventory, [Slot]),
            (new SetEquipmentUpgradeEffect(null!, 1), equipment, ReplayProjectionNeeds.Inventory, [Slot]),
            (new SetEquipmentEnchantmentEffect(null!, 1), equipment, ReplayProjectionNeeds.Inventory, [Slot]),
            (new SetEquipmentDimensionalEffect(null!, 1, 1), equipment, ReplayProjectionNeeds.Inventory, [Slot]),
            (new SetEquipmentMagicSealEffect(null!, []), equipment, ReplayProjectionNeeds.Inventory, [Slot]),
            (new SetEquipmentForgingEffect(null!, 1), equipment, ReplayProjectionNeeds.Inventory, [Slot]),
            (new DestroyEquipmentEffect(null!), equipment, ReplayProjectionNeeds.Inventory, [Slot]),
            (new GrantStackableEffect(1000, 1), new StackableGrantEffectChange([new(Slot, 1000, 1)], [new(other, null)]), ReplayProjectionNeeds.Inventory, [Slot, other]),
            (new GrantPreparedItemEffect(prepared, ItemDeliveryMode.InventoryOnly), new PreparedItemGrantChange([], [Slot]), ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.Pets, [Slot]),
            (new GrantBasicCharacterItemEffect(3000, 1, placement), new BasicCharacterItemChange([new(default, Slot, 1)]), ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.Pets, [Slot]),
            (new SpendGoldEffect(1), new GoldEffectChange(1), ReplayProjectionNeeds.Inventory, []),
            (new SetQuestRewardGoldEffect(1, 2), new QuestRewardGoldChange(1, 2), ReplayProjectionNeeds.Inventory, []),
            (new GrantOpeningGoldEffect(1), new OpeningVirtualChange(Slot, null!, null!), ReplayProjectionNeeds.Inventory, []),
            (new GrantOpeningVirtualEffect(0, 1, 2, new(1, 1)), new OpeningVirtualChange(Slot, null!, null!), ReplayProjectionNeeds.Inventory, []),
            (new MoveToTownEffect(townBefore, townAfter, true, sourceArea, 1), new TownPositionChange(townBefore, townAfter), ReplayProjectionNeeds.TownLocation, []),
            (new AddDungeonEntryAllowanceEffect(1, 1, default, null, 0), new DungeonEntryAllowanceChange(1), ReplayProjectionNeeds.HerasQuota, []),
            (new SetFatigueStateEffect(null, null!), new FatigueStateChange(null!), ReplayProjectionNeeds.Fatigue, []),
            (new SetItemUsageStateEffect(1, null, null!), new ItemUsageStateChange(1, null!), ReplayProjectionNeeds.ItemUsage, []),
            (new SetCarriedQuestEffect(null!, null), new QuestRowChange(1), ReplayProjectionNeeds.Quests, []),
            (new RemoveTrackedQuestEffect(null!), new QuestRowChange(1), ReplayProjectionNeeds.Quests, []),
            (new SetClearedQuestEffect(1, null, 1), new QuestRowChange(1), ReplayProjectionNeeds.Quests, []),
            (new SetLearnedSkillEffect(0, 0, 1, null, 1), new LearnedSkillChange(0, 0, 1, 1), ReplayProjectionNeeds.Skills, []),
            (new RenamePetEffect(1, 1, []), new PetNameChange(null!), ReplayProjectionNeeds.Pets, []),
            (new AddPetStomachEffect(1, 1), new PetStomachChange(null!), ReplayProjectionNeeds.Pets, []),
            (new CharacterExperienceEffect(null!), new CharacterExperienceChange(null!), ReplayProjectionNeeds.Experience, []),
            (new SetCharacterLevelEffect(null!, 2), new CharacterLevelChange(null!), ReplayProjectionNeeds.Experience | ReplayProjectionNeeds.Skills, []),
            (new SetCharacterGrowTypeEffect(0, 0, 1), new CharacterGrowTypeChange(1), ReplayProjectionNeeds.CharacterAppearance | ReplayProjectionNeeds.Skills, []),
            (new SetCharacterAwakeningEffect(0, 1, 17), new CharacterGrowTypeChange(17), ReplayProjectionNeeds.CharacterAppearance | ReplayProjectionNeeds.Skills, []),
            (new MaterializeSkillBuildsEffect(default, []), new SkillBuildsMaterializedChange(), ReplayProjectionNeeds.CharacterAppearance | ReplayProjectionNeeds.Skills, []),
            (new SetOpeningPremiumEffect(default, 1, 2, new(1, 1)), new OpeningPremiumChange(null!, null!), ReplayProjectionNeeds.Premium, []),
            (new SetDoublePotUsageEffect(null!), new DoublePotUsageChange(null!), ReplayProjectionNeeds.Premium, []),
            (new StartItemCooldownEffect(default, TimeSpan.FromSeconds(1)), new ItemCooldownChange(default, TimeSpan.FromSeconds(1)), ReplayProjectionNeeds.None, []),
            (new StartSpiritPotionEffect(1, TimeSpan.FromSeconds(1)), new SpiritPotionChange(1, TimeSpan.FromSeconds(1)), ReplayProjectionNeeds.None, []),
            (new SetSeriaLuckEffect(1, 2), new SeriaLuckChange(1, 2), ReplayProjectionNeeds.None, []),
        ];
        foreach (var item in cases)
        {
            await TestGameplay.RunAsync(f.Services, () =>
            {
                var operations = f.Services.GetRequiredService<EffectOperationContext>();
                var authorization = f.Services.GetRequiredService<NetworkCommandAuthorization>();
                using var network = authorization.Enter(() => true, GameMessageMetadata.Empty, NetworkCommandAuthorization.MessageCategory.Effect);
                var context = new CommandExecutionContext(Guid.NewGuid(), typeof(Probe), typeof(ProbeHandler), CommandExecutionKind.Transactional);
                var command = new Probe(f.Session, 2); var entry = Entry(f.Session);
                using var admission = context.EnterLegacyInvocation(f.Services.GetRequiredService<CommandHandlerRegistry>().GetRegistration(typeof(Probe)));
                using var operation = operations.Enter(entry, new(f.Session, f.Connection), ReplayProjectionNeeds.None);
                using var main = operations.BeginMain(context, command);
                operations.TransactionStarted(context, command);
                using var cap = operations.CaptureLegacy(context, authorization.Capture(), false, () => { });
                cap.RecordEffects(Owner(f), [new(item.Effect, item.Change)]);
                operations.Freeze(context, command);
                Assert.Equal(item.Needs, entry.Projection!.Needs);
                Assert.Equal(item.Slots.OrderBy(slot => slot.Index), entry.Projection.TouchedSlots.OrderBy(slot => slot.Index));
                if (item.Effect is MoveToTownEffect move)
                    Assert.Equal(new[] { move.SourceArea, new TownAreaKey(move.ChannelId, move.Destination.TownId, move.Destination.AreaId) }.ToHashSet(), entry.Projection.TouchedTownAreas.ToHashSet());
                return Task.CompletedTask;
            });
        }
    }

    [Fact]
    public async Task Closing_the_inner_effect_lease_does_not_reject_legitimate_runtime_commit_callbacks()
    {
        using var f = new ShopFixture();
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var runtime = f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>();
            var key = ItemCooldownKey.ForItem(1000);
            using var cap = new TestEffectFootprints.Lease(f.Session);
            await using var tx = f.Services.GetRequiredService<DomainTransactionScope>().Begin();
            await f.Services.GetRequiredService<IApplyEffectsDomainService>().ApplyAsync(Context(f, cap),
                [new StartItemCooldownEffect(key, TimeSpan.FromSeconds(10)), new StartSpiritPotionEffect(1, TimeSpan.FromHours(1))], default);
            cap.Dispose();
            Assert.False(runtime.IsCoolingDown(f.Session.CharacterId, key));
            await tx.CommitAsync(default);
            Assert.True(runtime.IsCoolingDown(f.Session.CharacterId, key));
            Assert.Equal(1, runtime.SpiritPotionBonusRateOf(f.Session.CharacterId));
        });
    }

    [Fact]
    public async Task Expiry_inside_Heras_apply_quota_read_prevents_Direct_and_committed_bonus_write()
    {
        using var f = new ShopFixture();
        var direct = 0;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var scope = f.Services.GetRequiredService<DomainTransactionScope>();
            await using var tx = scope.Begin();
            using var cap = new TestEffectFootprints.Lease(f.Session);
            var current = await f.Services.GetRequiredService<IHerasDungeonDomainService>()
                .ReadQuotaAtAsync(f.Session.CharacterId, f.Clock.GetUtcNow(), default);
            var quota = new QuotaGate(f.Services.GetRequiredService<IUsageQuotaDomainService>());
            var events = new HerasEvents(f.Services.GetRequiredService<IEventService>());
            var heras = ActivatorUtilities.CreateInstance<Dfo.Server.Application.World.HerasDungeonDomainService>(f.Services, quota, events);
            var executor = new ApplyEffectsDomainService([new DungeonEntryAllowanceEffectHandler(heras)], scope,
                f.Services.GetRequiredService<ICharacterDomainStore>(), f.Services.GetRequiredService<ISelectedSessionDomainService>(),
                f.Services.GetRequiredService<IInventoryEffectPreflightDomainService>());
            var pending = executor.ApplyAsync(Context(f, cap),
                [new AddDungeonEntryAllowanceEffect(3900, 1, current.Period, current.Bonus, current.Used)], default).AsTask();
            await quota.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cap.Dispose(); quota.Resume.SetResult();
            var error = await Record.ExceptionAsync(() => pending);
            direct = events.DirectCalls;
            await tx.CommitAsync(default);
            Assert.IsType<InvalidOperationException>(error);
        });
        Assert.Equal((0, 0), (direct, f.Scalar("SELECT COALESCE(SUM(value),0) FROM character_daily_counters WHERE counter_key LIKE 'heras.3900.bonus:%'")));
    }

    private static EffectExecutionContext Context(ShopFixture f, IEffectFootprintRecorder cap) =>
        new(f.Session.AccountId, f.Session.CharacterId, f.Clock.GetUtcNow(), cap, f.Session);
    private static EffectFootprintOwner Owner(ShopFixture f) => new(f.Session.AccountId, f.Session.CharacterId, f.Session);
    private static EffectOperationEntry Entry(SelectedSessionIdentity session) => new(session, 1, 1, 3, 1, new string('A', 64), 1);
    private static EffectRequest Request(long ordinal, ushort sequence, byte level, bool fresh = true) =>
        new((new GameMessageMetadata(1, 3, 0, sequence) { InboundOrdinal = ordinal, SequenceIsFresh = fresh }).WithRawBody([level]), level);
    private static ShopFixture Fixture(Witness witness, Replay replay) => new(services =>
    {
        foreach (var registration in services.Where(value => value.ImplementationInstance is MessageHandlerRegistration { MessageType: ClientMessageType.GameExit }).ToArray()) services.Remove(registration);
        foreach (var descriptor in services.Where(d => d.ImplementationInstance is CommandHandlerRegistration registration
            && registration.CommandType == typeof(Dfo.Server.Protocol.Game.Exit.GameExitCommand)).ToArray()) services.Remove(descriptor);
        services.RegisterMessageHandler<ProbeNetworkHandler>(ClientMessageType.GameExit);
        services.AddCommandHandler<Probe, bool, ProbeHandler>(CommandExecutionKind.Transactional);
        services.AddSingleton(witness); services.AddSingleton<IReplayStateProjectionDelivery>(replay);
    });
    private sealed record EffectRequest(GameMessageMetadata Metadata, byte Level) : IEffectSettlementRequest
    { public ClientMessageType MessageType => ClientMessageType.GameExit; public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.None; }
    private sealed record Probe(SelectedSessionIdentity Session, byte Level) : ICommand<bool>, IRequestSettlementCommand;
    private sealed record Ordinary : ICommand<bool>;
    private sealed class ProbeNetworkHandler(ICommandBus bus, ISelectedSessionDomainService sessions) : IClientMessageHandler
    {
        public bool CanHandle(IClientNetworkMessage message) => message is EffectRequest;
        public async ValueTask HandleAsync(NetworkDispatchContext context, IClientNetworkMessage message, CancellationToken ct) =>
            _ = await bus.SendAsync(new Probe(sessions.FindByConnection(context.Connection.ConnectionId)!, ((EffectRequest)message).Level), ct);
    }
    private sealed class Witness { public bool Fail; public List<IEffectFootprintRecorder> Caps { get; } = []; }
    private sealed class ProbeHandler(IApplyEffectsDomainService effects, ICharacterDomainService characters, Witness witness, TimeProvider clock) : ICommandHandler<Probe, bool>
    {
        public async ValueTask<bool> HandleAsync(CommandExecutionContext context, Probe command, CancellationToken ct)
        {
            witness.Caps.Add(context.LegacyFootprint);
            var progress = await characters.Get(command.Session.CharacterId).ProgressAsync(ct);
            await effects.ApplyAsync(new(command.Session.AccountId, command.Session.CharacterId, clock.GetUtcNow(), context.LegacyFootprint, command.Session),
                [new ConsumeFromContainerEffect(Slot, 1000, 1), new SpendGoldEffect(7), new SetCharacterLevelEffect(progress!, command.Level)], ct);
            if (witness.Fail) { witness.Fail = false; throw new IOException("before commit"); }
            return true;
        }
    }
    private sealed class Replay : IReplayStateProjectionDelivery
    {
        public List<ReplayProjectionRequest> Plans { get; } = [];
        public ValueTask RefreshAsync(NetworkConnectionContext connection, SelectedSessionIdentity session, ReplayProjectionRequest request, GameMessageMetadata? metadata, CancellationToken ct)
        { Plans.Add(request); return ValueTask.CompletedTask; }
    }
    private class Pause
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask WaitAsync() { Reached.TrySetResult(); await Resume.Task; }
    }
    private sealed class GatedPreview(IInventoryEffectPreflightDomainService inner, Pause? gate) : IInventoryEffectPreflightDomainService
    {
        public async ValueTask<InventoryEffectPreviewResult> PreviewAsync(EffectExecutionContext context, IReadOnlyList<DomainEffect> effects, CancellationToken ct)
        { var result = await inner.PreviewAsync(context, effects, ct); if (gate is not null) await gate.WaitAsync(); return result; }
    }
    private sealed class GatedGold(ICharacterDomainService characters, Pause? gate) : EffectHandler<SpendGoldEffect>
    {
        public int Writes;
        public override async ValueTask ValidateAsync(EffectExecutionContext context, SpendGoldEffect effect, CancellationToken ct)
        { if (gate is not null) await gate.WaitAsync(); }
        public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, SpendGoldEffect effect, CancellationToken ct)
        { Writes++; return new GoldEffectChange((await characters.Get(context.CharacterId).TrySpendGoldAsync(effect.Amount, ct))!.Value); }
    }
    private sealed class ReadGate(IBusinessInventorySlotDomainService inner) : Pause, IBusinessInventorySlotDomainService
    {
        public int Reads; public int Takes;
        public BoundInventorySlot? Find(CharacterId owner, BusinessSlot slot) => inner.Find(owner, slot) is { } found ? found with { Container = new Bag(found.Container, this) } : null;
        public BoundInventorySlot Get(CharacterId owner, BusinessSlot slot) => Find(owner, slot)!;
        public BoundInventorySlot FromStorage(InventoryStorageSlot slot) => inner.FromStorage(slot);
        private sealed class Bag(IBusinessItemContainer inner, ReadGate gate) : IBusinessItemContainer
        {
            public BusinessContainerId Id => inner.Id; public ContainerId StorageId => inner.StorageId;
            public BusinessInventorySpace Space => inner.Space; public bool IsDefault => inner.IsDefault;
            public bool IsPersistent => inner.IsPersistent;
            public ValueTask<int> OpenSlotCountAsync(CancellationToken ct) => inner.OpenSlotCountAsync(ct);
            public ValueTask<IEquipmentInstance?> GetEquipmentAsync(int index, CancellationToken ct) => inner.GetEquipmentAsync(index, ct);
            public async ValueTask<ItemReference?> GetAsync(int index, CancellationToken ct)
            { var item = await inner.GetAsync(index, ct); if (++gate.Reads == 2) await gate.WaitAsync(); return item; }
            public ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(CancellationToken ct) => inner.GetAllAsync(ct);
            public ValueTask<ItemRecord> TakeAsync(ItemReference item, int? count, CancellationToken ct)
            { gate.Takes++; return inner.TakeAsync(item, count, ct); }
            public ValueTask<ItemReference> PutAsync(int index, ItemRecord item, CancellationToken ct) => inner.PutAsync(index, item, ct);
            public ValueTask<ItemReference> UpdateAsync(ItemReference item, Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct) => inner.UpdateAsync(item, change, ct);
            public ValueTask FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
            public ValueTask<BusinessItemAdditionResult> AddAsync(int item, int count, CancellationToken ct) => inner.AddAsync(item, count, ct);
        }
    }
    private sealed class QuoteGate(ICeraShopRuleService inner, Pause gate) : ICeraShopRuleService
    {
        public CeraShopRestrictionPlanningResult PlanRestrictions(CeraShopQuote quote, CeraShopRestrictionSnapshot state, DateTimeOffset now) => inner.PlanRestrictions(quote, state, now);
        public async ValueTask<CeraShopQuoteResult> QuoteAsync(CeraShopPurchaseInput input, int level, DateTimeOffset now, CancellationToken ct)
        { var result = await inner.QuoteAsync(input, level, now, ct); await gate.WaitAsync(); return result; }
        public CeraShopPlanningResult Plan(CeraShopQuote quote, IReadOnlyDictionary<BusinessInventorySpace, IReadOnlyList<ItemReference>> held, int gold, AccountCeraBalances balances, int identitySeed) =>
            inner.Plan(quote, held, gold, balances, identitySeed);
    }
    private sealed class LifecycleWitness : ILifecycleEffectFootprintSource
    {
        public List<InternalCommandOrigin> Origins { get; } = [];
        public ILifecycleEffectFootprintLease Open(InternalCommandOrigin origin)
        { Origins.Add(origin); return new LifecycleEffectFootprintSource().Open(origin); }
    }
    private sealed class QuotaGate(IUsageQuotaDomainService inner) : Pause, IUsageQuotaDomainService
    {
        private int bonusReads;
        public async ValueTask<UsageQuotaValue?> ReadAsync(CharacterId character, UsageQuotaKey key, CancellationToken ct)
        {
            var value = await inner.ReadAsync(character, key, ct);
            if (key.Family == UsageQuotaFamily.HerasDungeon && key.Name == "bonus" && ++bonusReads == 2)
                await WaitAsync();
            return value;
        }
        public ValueTask<IReadOnlyDictionary<int, ConsumableUsageRow>> ReadConsumablesAsync(CharacterId character, CancellationToken ct) => inner.ReadConsumablesAsync(character, ct);
        public ValueTask<MirrorDailyQuota> ReadMirrorAsync(CharacterId character, DateOnly day, CancellationToken ct) => inner.ReadMirrorAsync(character, day, ct);
    }
    private sealed class HerasEvents(IEventService inner) : IEventService
    {
        public int DirectCalls;
        public async ValueTask DirectAsync<TEvent>(TEvent payload, CancellationToken ct = default) where TEvent : class, IDomainEvent
        { if (payload is HerasQuotaRecorded) DirectCalls++; await inner.DirectAsync(payload, ct); }
        public ValueTask<Guid> EnqueueAsync<TEvent>(TEvent payload, CancellationToken ct = default) where TEvent : class, IDomainEvent => inner.EnqueueAsync(payload, ct);
        public ValueTask<NotificationDelivery> ProjectAsync<TNotification>(NotificationAudience audience, TNotification notification, CancellationToken ct = default)
            where TNotification : class, INotification<TNotification> => inner.ProjectAsync(audience, notification, ct);
    }
    private sealed class CleanupApply(IApplyEffectsDomainService effects, DomainTransactionScope transactions) : ISelectedSessionCleanupDomainService
    {
        public IEffectFootprintRecorder? Cap;
        public async ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(SelectedSessionIdentity session, bool includeOwner, IEffectFootprintRecorder footprint, CancellationToken ct)
        {
            Cap = footprint;
            await using var tx = transactions.Begin();
            await effects.ApplyAsync(new(session.AccountId, session.CharacterId, DateTimeOffset.UtcNow, footprint, session), [new SpendGoldEffect(7)], ct);
            await tx.CommitAsync(ct);
            return new(session.ConnectionId, [], [], [], PartyNotificationPlan.Empty);
        }
    }
}
