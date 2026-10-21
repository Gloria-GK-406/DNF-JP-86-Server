using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class ApplyEffectsTests
{
    private static readonly BusinessSlot Source = new(BusinessInventorySpace.QuickSlot, 0);
    private static readonly EffectExecutionContext Offline = new(new AccountId(6), new CharacterId(60),
        new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Cross_account_context_is_rejected_before_any_consumption()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var f = new Inventory();
            f.Seed(3, 8474, 5);
            await using (f.Scope.Begin())
                await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await f.Executor().ApplyAsync(
                    Offline with { AccountId = new AccountId(7) }, [new ConsumeFromContainerEffect(Source, 8474, 1)], default));
            Assert.Equal(5, f.CountIn(3));
        });
    }

    [Fact]
    public async Task Multiple_consumptions_accumulate_from_current_transaction_state_without_a_session()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var f = new Inventory();
            f.Seed(3, 8474, 5);
            await using (var tx = f.Scope.Begin())
            {
                var result = await f.Executor().ApplyAsync(Offline,
                    [new ConsumeFromContainerEffect(Source, 8474, 1), new ConsumeFromContainerEffect(Source, 8474, 2)], default);
                Assert.Equal([4, 2], result.Changes<ContainerConsumptionChange>().Select(x => x.RemainingCount));
                await tx.CommitAsync(default);
            }
            Assert.Equal(2, f.CountIn(3));
        });
    }

    [Fact]
    public async Task Equal_indices_in_different_business_spaces_consume_different_piles()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var f = new Inventory();
            f.Seed(3, 8474, 5);
            f.Seed(65, 9000, 7);
            await using (var tx = f.Scope.Begin())
            {
                await f.Executor().ApplyAsync(Offline, [new ConsumeFromContainerEffect(Source, 8474, 2),
                    new ConsumeFromContainerEffect(new(BusinessInventorySpace.Consumable, 0), 9000, 3)], default);
                await tx.CommitAsync(default);
            }
            Assert.Equal(3, f.CountIn(3));
            Assert.Equal(4, f.CountIn(65));
        });
    }

    [Fact]
    public async Task Combined_consumption_exceeding_a_pile_rolls_back_the_first_consumption()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var f = new Inventory();
            f.Seed(3, 8474, 5);
            await using (f.Scope.Begin())
                await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await f.Executor().ApplyAsync(Offline,
                    [new ConsumeFromContainerEffect(Source, 8474, 3), new ConsumeFromContainerEffect(Source, 8474, 3)], default));
            Assert.Equal(5, f.CountIn(3));
        });
    }

    [Fact]
    public async Task Unknown_effect_is_rejected_before_any_consumption_even_when_caller_commits()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var f = new Inventory();
            f.Seed(3, 8474, 5);
            await using (var tx = f.Scope.Begin())
            {
                await Assert.ThrowsAsync<NotSupportedException>(async () =>
                    await f.Executor().ApplyAsync(Offline, [new ConsumeFromContainerEffect(Source, 8474, 1), new Unknown()], default));
                await tx.CommitAsync(default);
            }
            Assert.Equal(5, f.CountIn(3));
        });
    }

    [Theory]
    [InlineData(9000, false)]
    [InlineData(8474, true)]
    public async Task Disagreeing_or_expired_target_is_rejected_before_any_consumption(int expectedId, bool expired)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var f = new Inventory();
            f.Seed(3, 8474, 5);
            f.Seed(65, 8474, 5);
            if (expired)
            {
                await using var seed = f.Scope.Begin();
                var target = f.Slots.Get(Offline.CharacterId, new(BusinessInventorySpace.Consumable, 0));
                var item = (await target.Container.GetAsync(0, default))!;
                await target.Container.UpdateAsync(item, x => x with { ExpireTime = (int)Offline.At.ToUnixTimeSeconds() }, default);
                await seed.CommitAsync(default);
            }
            await using (var tx = f.Scope.Begin())
            {
                await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await f.Executor().ApplyAsync(Offline,
                    [new ConsumeFromContainerEffect(Source, 8474, 1),
                        new ConsumeFromContainerEffect(new(BusinessInventorySpace.Consumable, 0), expectedId, 1)], default));
                await tx.CommitAsync(default);
            }
            Assert.Equal(5, f.CountIn(3));
            Assert.Equal(5, f.CountIn(65));
        });
    }

    [Fact]
    public async Task Committed_consumption_records_the_cooldown_and_potion_for_its_session()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var f = new Inventory();
            f.Seed(3, 8474, 5);
            var clock = new AdvanceableTimeProvider(Offline.At);
            var runtime = new OnlineCharacterRuntimeDomainService(clock);
            var sessions = new Sessions();
            runtime.Begin(sessions.Current, InTown, 100);
            var key = ItemCooldownKey.ForItem(8474);
            await using (var tx = f.Scope.Begin())
            {
                await f.Executor(new StartItemCooldownEffectHandler(runtime, f.Scope),
                    new StartSpiritPotionEffectHandler(runtime, f.Scope))
                    .ApplyAsync(Offline with { Session = sessions.Current },
                        [new ConsumeFromContainerEffect(Source, 8474, 1),
                            new StartItemCooldownEffect(key, TimeSpan.FromSeconds(10)),
                            new StartSpiritPotionEffect(1, TimeSpan.FromHours(1))], default);
                Assert.False(runtime.IsCoolingDown(Offline.CharacterId, key));
                await tx.CommitAsync(default);
            }
            Assert.Equal(4, f.CountIn(3));
            Assert.True(runtime.IsCoolingDown(Offline.CharacterId, key));
            Assert.Equal(1, runtime.SpiritPotionBonusRateOf(Offline.CharacterId));
        });
    }

    [Fact]
    public async Task Late_effect_failure_rolls_back_consumption_and_memory_effects()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var f = new Inventory();
            f.Seed(3, 8474, 5);
            var clock = new AdvanceableTimeProvider(Offline.At);
            var cooldowns = new OnlineCharacterRuntimeDomainService(clock);
            var potion = cooldowns;
            var sessions = new Sessions();
            cooldowns.Begin(sessions.Current, InTown, 100);
            var key = ItemCooldownKey.ForItem(8474);
            await using (f.Scope.Begin())
            {
                await Assert.ThrowsAsync<IOException>(async () => await f.Executor(
                    new StartItemCooldownEffectHandler(cooldowns, f.Scope),
                    new StartSpiritPotionEffectHandler(potion, f.Scope), new ThrowHandler())
                    .ApplyAsync(Offline with { Session = sessions.Current },
                        [new ConsumeFromContainerEffect(Source, 8474, 1),
                            new StartItemCooldownEffect(key, TimeSpan.FromSeconds(10)),
                            new StartSpiritPotionEffect(1, TimeSpan.FromHours(1)), new Throw()], default));
            }
            Assert.Equal(5, f.CountIn(3));
            Assert.False(cooldowns.IsCoolingDown(Offline.CharacterId, key));
            Assert.Equal(0, potion.SpiritPotionBonusRateOf(Offline.CharacterId));
        });
    }

    [Fact]
    public async Task Failed_transaction_commit_keeps_consumption_cooldown_and_potion_invisible()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var f = new Inventory();
            f.Seed(3, 8474, 5);
            var clock = new AdvanceableTimeProvider(Offline.At);
            var cooldowns = new OnlineCharacterRuntimeDomainService(clock);
            var potion = cooldowns;
            var sessions = new Sessions();
            cooldowns.Begin(sessions.Current, InTown, 100);
            var key = ItemCooldownKey.ForItem(8474);
            await using (var tx = f.Scope.Begin())
            {
                await f.Executor(new StartItemCooldownEffectHandler(cooldowns, f.Scope),
                    new StartSpiritPotionEffectHandler(potion, f.Scope))
                    .ApplyAsync(Offline with { Session = sessions.Current },
                        [new ConsumeFromContainerEffect(Source, 8474, 1),
                            new StartItemCooldownEffect(key, TimeSpan.FromSeconds(10)),
                            new StartSpiritPotionEffect(1, TimeSpan.FromHours(1))], default);
                f.Scope.Current!.Enlist(new object(), () => new RecordingParticipant("fail", [], true)
                    { CommitFailure = new IOException("commit failed") });
                Assert.False(cooldowns.IsCoolingDown(Offline.CharacterId, key));
                await Assert.ThrowsAsync<IOException>(async () => await tx.CommitAsync(default));
            }
            Assert.Equal(5, f.CountIn(3));
            Assert.False(cooldowns.IsCoolingDown(Offline.CharacterId, key));
            Assert.Equal(0, potion.SpiritPotionBonusRateOf(Offline.CharacterId));
        });
    }

    [Fact]
    public async Task Replaced_session_does_not_receive_an_old_cooldown_commit()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var f = new Inventory();
            var cooldowns = new OnlineCharacterRuntimeDomainService(new AdvanceableTimeProvider(Offline.At));
            var sessions = new Sessions();
            cooldowns.Begin(sessions.Current, InTown, 100);
            var key = ItemCooldownKey.ForItem(8474);
            await using (var tx = f.Scope.Begin())
            {
                await f.Executor(new StartItemCooldownEffectHandler(cooldowns, f.Scope))
                    .ApplyAsync(Offline with { Session = sessions.Current }, [new StartItemCooldownEffect(key, TimeSpan.FromSeconds(10))], default);
                // The replacement enters the world and begins its own record before the old commit lands.
                sessions.Current = sessions.Current with { ConnectionGeneration = 2 };
                cooldowns.Begin(sessions.Current, InTown, 100);
                await tx.CommitAsync(default);
            }
            Assert.False(cooldowns.IsCoolingDown(Offline.CharacterId, key));
        });
    }

    private static readonly Dfo.Server.Data.Characters.CharacterPositionState InTown = new(1, 0, 0, 0, 0);

    private sealed class Inventory : FakeMainInventory
    {
        public IBusinessInventorySlotDomainService Slots { get; }
        public Inventory() => Slots = new BusinessInventorySlotDomainService(Containers(), new InventorySlotMappingRuleService());
        public ApplyEffectsDomainService Executor(params IEffectHandler[] additional) =>
            new([new ConsumeFromContainerEffectHandler(Slots, Lifecycle), .. additional], Scope,
                new EffectCharacterStore(new(Offline.AccountId, Offline.CharacterId)), new Sessions(), ConsumptionEffectPreflight.For(Slots));
    }
    private sealed record Unknown : DomainEffect;
    private sealed record Throw : DomainEffect;
    private sealed class ThrowHandler : EffectHandler<Throw>
    {
        public override ValueTask ValidateAsync(EffectExecutionContext context, Throw effect, CancellationToken ct) => ValueTask.CompletedTask;
        public override ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, Throw effect, CancellationToken ct) => throw new IOException("effect failed");
    }
    private sealed class Sessions : ISelectedSessionDomainService
    {
        // Test double: the current selection counts as being in the world, and
        // remembers which sessions were fenced.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            Dfo.Server.Data.World.SelectedSessionIdentity, bool> testDoubleFences = new();
        void Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.EnterWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) { }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.Fence(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
            && testDoubleFences.TryAdd(session, true) | true;
        Dfo.Server.Data.World.SelectedSessionStanding? Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.StandingOf(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
                ? new(Dfo.Server.Data.World.SelectedSessionPhase.InWorld, testDoubleFences.ContainsKey(session), false)
                : null;
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsInWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session)
                is { Phase: Dfo.Server.Data.World.SelectedSessionPhase.InWorld };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsSelected(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session) is { ConnectionRevoked: false };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Characters.CharacterId characterId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByCharacter(characterId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            ushort wireUserId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByWireUserId(wireUserId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Networking.NetworkConnectionId connectionId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByConnection(connectionId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }

        public SelectedSessionIdentity Current { get; set; } = SelectedSessionIdentity.Create(
            Offline.AccountId, Offline.CharacterId, new NetworkConnectionId("effects"), 1, 10011);
        public SelectedSessionIdentity? FindByCharacter(CharacterId id) => Current;
        public SelectedSessionIdentity? Find(AccountId id) => Current;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId id) => Current;
        public SelectedSessionIdentity? FindByWireUserId(ushort id) => Current;
        public SelectedSessionIdentity? Select(SelectedSessionIdentity value) => throw new NotSupportedException();
        public bool ReleaseExact(SelectedSessionIdentity value) => throw new NotSupportedException();
    }
}
