using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Creatures;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Creatures;
using Dfo.Server.Persistence.Schema;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Application.Tests.Creatures;

public sealed class CreatureRuntimeDomainServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Food_consumption_and_pet_stomach_settle_together(bool failCommit)
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        f.Enter();
        var food = new FoodInventory { Scope = f.Scope };
        food.Seed(3, 24, 2);
        var slots = new BusinessInventorySlotDomainService(food.Containers(), new InventorySlotMappingRuleService());
        var source = new BusinessSlot(BusinessInventorySpace.QuickSlot, 0);
        var executor = new ApplyEffectsDomainService([new ConsumeFromContainerEffectHandler(slots),
            new AddPetStomachEffectHandler(f.Runtime)], f.Scope, new SqliteCharacterDomainStore(f.Path), f.Sessions, ConsumptionEffectPreflight.For(slots));
        await using (var tx = f.Scope.Begin())
        {
            var item = (await slots.Get(f.Session.CharacterId, source).Container.ReadSlotAsync(0, default))!;
            var pet = (await f.Runtime.Of(f.Session.CharacterId).CurrentPetAsync(default))!;
            var rules = new ItemUseEffectRuleService(new ItemUseJudgementService(new FakeItemUseRules()));
            var plan = rules.Build(ItemUseRules.Potion(0), source, item,
                new ItemUseCandidate(60, QuestJobRequirement.Priest, ItemUsePlace.Village, false), pet.ItemUid, f.Clock.GetUtcNow());
            Assert.Equal(ItemUsability.Allowed, plan.Status);
            var applied = await executor.ApplyAsync(new(f.Session.AccountId, f.Session.CharacterId, f.Clock.GetUtcNow(), f.Session),
                plan.Effects, default);
            Assert.Equal(80, Assert.Single(applied.Changes<PetStomachChange>()).Change.After.Stomach);
            Assert.Empty(f.Runtime.DrainProjections());
            if (failCommit)
            {
                f.Scope.Current!.Enlist(new object(), () => new PrepareCallback(() => throw new IOException("prepare failed")));
                await Assert.ThrowsAsync<IOException>(async () => await tx.CommitAsync(default));
            }
            else await tx.CommitAsync(default);
        }
        Assert.Equal(failCommit ? 2 : 1, food.CountIn(3));
        Assert.Equal(failCommit ? 50L : 80L, await f.StomachAsync());
        if (failCommit) Assert.Empty(f.Runtime.DrainProjections());
        else Assert.NotEmpty(f.Runtime.DrainProjections());
    }

    [Fact]
    public async Task Discard_drops_cached_stomach_and_pending_projection_without_Leave_writing_durable_balance()
    {
        await using var f = new Fixture();
        await f.SeedAsync(); f.Enter();
        await f.Runtime.AdvanceAsync(f.Session, default, combat: true);
        f.Clock.Advance(TimeSpan.FromSeconds(60));
        await using (var connection = new SqliteConnection($"Data Source={f.Path}"))
        {
            await connection.OpenAsync(); await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE items SET creature_field04=80 WHERE item_uid=101";
            await command.ExecuteNonQueryAsync();
        }
        f.Runtime.QueueRoomExperience(f.Session, DungeonRunId.New(), new(0,0), 10);
        f.Runtime.DiscardExact(f.Session);
        await f.Runtime.LeaveAsync(f.Session, default);
        Assert.Equal(80, await f.StomachAsync());
        Assert.Empty(f.Runtime.DrainProjections()); Assert.Empty(f.Runtime.DueSessions());
        var replacement = f.Session with { ConnectionGeneration = 2 };
        f.Enter(replacement);
        f.Runtime.QueueRoomExperience(replacement, DungeonRunId.New(), new(0,0), 10);
        f.Runtime.DiscardExact(f.Session);
        await f.Runtime.LeaveAsync(f.Session, default);
        Assert.Contains(replacement, f.Runtime.DueSessions());
        Assert.Equal(80, await f.StomachAsync());
    }

    private sealed class FoodInventory() : FakeMainInventory(7);

    [Fact]
    public async Task Instance_read_projects_the_current_combat_balance_without_writing_storage()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        f.Enter();
        await f.Runtime.AdvanceAsync(f.Session, default, combat: true);
        f.Clock.Advance(TimeSpan.FromSeconds(30));
        f.Runtime.ChangeCombat(f.Session.CharacterId, false);
        await using var tx = f.Scope.Begin();
        var pet = (await f.Runtime.Of(f.Session.CharacterId).FindPetAsync(101, default))!;
        var state = await pet.ReadAsync(default);
        Assert.Equal(49.5, state.ExactStomach, 8);
        Assert.Equal(49, state.Stomach);
        Assert.Equal(50, await f.StomachAsync());
    }

    [Fact]
    public async Task Two_handles_share_latest_progress_and_fractional_stomach_without_lost_updates()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        f.Enter();
        await f.Runtime.AdvanceAsync(f.Session, default, combat: true);
        f.Clock.Advance(TimeSpan.FromSeconds(30));
        IPetInstance first;
        await using (var tx = f.Scope.Begin())
        {
            var pets = f.Runtime.Of(f.Session.CharacterId);
            first = (await pets.FindPetAsync(101, default))!;
            var second = (await pets.CurrentPetAsync(default))!;
            await first.AddStomachAsync(10, default);
            await second.AddExpAsync(20, default);
            await first.AddStomachAsync(5, default);
            var state = await second.ReadAsync(default);
            Assert.Equal(64.5, state.ExactStomach, 8);
            Assert.Equal(110, state.Experience);
            state.NameBytes[0] = 0;
            Assert.Equal(0x80, (await first.ReadAsync(default)).NameBytes[0]);
            await tx.CommitAsync(default);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await first.AddStomachAsync(1, default));
        f.Clock.Advance(TimeSpan.FromSeconds(30));
        await f.Runtime.AdvanceAsync(f.Session, default);
        Assert.Equal(64, await f.StomachAsync());
        Assert.Equal(110, await f.ExperienceAsync());
    }

    [Fact]
    public async Task Rolled_back_instance_is_unusable_and_fresh_lookup_sees_durable_state()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        f.Enter();
        await f.Runtime.AdvanceAsync(f.Session, default);
        IPetInstance pet;
        await using (f.Scope.Begin())
        {
            pet = (await f.Runtime.Of(f.Session.CharacterId).FindPetAsync(101, default))!;
            await pet.AddStomachAsync(30, default);
            await pet.AddExpAsync(20, default);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await pet.ReadAsync(default));
        Assert.Empty(f.Runtime.DrainProjections());
        await using var tx = f.Scope.Begin();
        var fresh = (await f.Runtime.Of(f.Session.CharacterId).FindPetAsync(101, default))!;
        var state = await fresh.ReadAsync(default);
        Assert.Equal(50, state.Stomach);
        Assert.Equal(90, state.Experience);
    }

    [Fact]
    public async Task ItemGrowthPetDoesNotReceiveOrdinaryRoomExperience()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        f.Rules.HungerEnabled = false;
        f.Enter();
        await using (var tx = f.Scope.Begin())
        {
            Assert.Null(await f.Runtime.GainExperienceAsync(f.Session, 20, default));
            await tx.CommitAsync(default);
        }
        Assert.Equal(90L, await f.ExperienceAsync());
        Assert.Equal(100L, await f.StomachAsync());
    }
    [Fact]
    public async Task AnotherRoomQueuedDuringCommitDoesNotReplayCommittedReward()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        f.Enter();
        await f.Runtime.AdvanceAsync(f.Session, default);
        var run = new DungeonRunId(Guid.NewGuid());
        var first = new MazeCell(1, 2);
        var second = new MazeCell(2, 2);
        f.Runtime.QueueRoomExperience(f.Session, run, first, 5);
        await using (var tx = f.Scope.Begin())
        {
            f.Scope.Current!.Enlist(new object(), () => new CommitCallback(() =>
            {
                f.Runtime.QueueRoomExperience(f.Session, run, second, 5);
                f.Runtime.QueueRoomExperience(f.Session, run, first, 5);
            }));
            await f.Runtime.AdvanceAsync(f.Session, default);
            await f.Runtime.AdvanceAsync(f.Session, default);
            await tx.CommitAsync(default);
        }
        Assert.Equal(95L, await f.ExperienceAsync());
        Assert.Contains(f.Session, f.Runtime.DueSessions());
        await f.Runtime.AdvanceAsync(f.Session, default);
        Assert.Equal(100L, await f.ExperienceAsync());
        await f.Runtime.AdvanceAsync(f.Session, default);
        Assert.Equal(100L, await f.ExperienceAsync());
    }
    [Fact]
    public async Task ReturningAtExactZeroRevivesBeforeDelayedRestTick()
    {
        await using var f = new Fixture();
        await f.SeedAsync(stomach: 1);
        f.Enter();
        await f.Runtime.AdvanceAsync(f.Session, default);
        f.Runtime.ChangeCombat(f.Session.CharacterId, true);
        f.Clock.Advance(TimeSpan.FromSeconds(60));
        await f.Runtime.AdvanceAsync(f.Session, default);
        f.Runtime.DrainProjections();
        f.Runtime.ChangeCombat(f.Session.CharacterId, false);
        f.Clock.Advance(TimeSpan.FromSeconds(5));
        var revived = Assert.Single(await f.Runtime.AdvanceAsync(f.Session, default));
        Assert.Equal(0, revived.Before.ExactStomach);
        Assert.Equal(1, revived.After.Stomach);
        Assert.Equal(1L, await f.StomachAsync());
        Assert.Empty(await f.Runtime.AdvanceAsync(f.Session, default));
    }

    [Fact]
    public async Task QueuedRoomRewardRetriesAfterRollbackAndKeepsOriginalPetAcrossMove()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        f.Enter();
        await f.Runtime.AdvanceAsync(f.Session, default);
        var run = new DungeonRunId(Guid.NewGuid());
        var cell = new MazeCell(1, 2);
        f.Runtime.QueueRoomExperience(f.Session, run, cell, 5);
        f.Runtime.QueueRoomExperience(f.Session, run, cell, 5);
        await using (f.Scope.Begin())
            await f.Runtime.AdvanceAsync(f.Session, default);
        Assert.Equal(90L, await f.ExperienceAsync());
        Assert.Contains(f.Session, f.Runtime.DueSessions());
        await using (var connection = new SqliteConnection($"Data Source={f.Path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE item_slots SET list_type=7,slot_index=0 WHERE core_uid=101";
            await command.ExecuteNonQueryAsync();
        }
        await f.Runtime.AdvanceAsync(f.Session, default);
        Assert.Equal(95L, await f.ExperienceAsync());
        f.Runtime.QueueRoomExperience(f.Session, run, cell, 5);
        await f.Runtime.AdvanceAsync(f.Session, default);
        Assert.Equal(95L, await f.ExperienceAsync());
        var award = Assert.Single(f.Runtime.DrainProjections());
        Assert.False(Assert.Single(award.Changes).Equipped);
    }

    [Fact]
    public async Task RestAndCombatAccumulateFractionsAcrossAdvances()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        f.Enter();
        Assert.Empty(await f.Runtime.AdvanceAsync(f.Session, default));
        f.Clock.Advance(TimeSpan.FromSeconds(180));
        Assert.Empty(await f.Runtime.AdvanceAsync(f.Session, default));
        Assert.Equal(50L, await f.StomachAsync());
        f.Clock.Advance(TimeSpan.FromSeconds(180));
        Assert.Equal(51, Assert.Single(await f.Runtime.AdvanceAsync(f.Session, default)).After.Stomach);
        f.Runtime.ChangeCombat(f.Session.CharacterId, true);
        f.Clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(50, Assert.Single(await f.Runtime.AdvanceAsync(f.Session, default)).After.Stomach);
        Assert.Equal(50L, await f.StomachAsync());
    }

    [Fact]
    public async Task ReenterDoesNotCreditOfflineRest()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        f.Enter();
        await f.Runtime.AdvanceAsync(f.Session, default);
        await f.Runtime.LeaveAsync(f.Session, default);
        f.Clock.Advance(TimeSpan.FromDays(2));
        var replacement = f.Session with
        {
            ConnectionId = new NetworkConnectionId("Game:pet-2"), ConnectionGeneration = 2
        };
        f.Enter(replacement);
        Assert.Empty(await f.Runtime.AdvanceAsync(replacement, default));
        Assert.Equal(50L, await f.StomachAsync());
    }

    [Fact]
    public async Task FeedAndExperienceRollBackWithTheirUseCase()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        f.Enter();
        await using (f.Scope.Begin())
        {
            Assert.Equal(80, (await (await f.Runtime.Of(f.Session.CharacterId).CurrentPetAsync(default))!.AddStomachAsync(30, default))!.After.Stomach);
            Assert.Equal(110, (await f.Runtime.GainExperienceAsync(f.Session, 20, default))!.After.Experience);
        }
        Assert.Equal(50L, await f.StomachAsync());
        Assert.Equal(90L, await f.ExperienceAsync());

        await using (var tx = f.Scope.Begin())
        {
            Assert.Equal(80, (await (await f.Runtime.Of(f.Session.CharacterId).CurrentPetAsync(default))!.AddStomachAsync(30, default))!.After.Stomach);
            var growth = await f.Runtime.GainExperienceAsync(f.Session, 20, default);
            Assert.Equal(2, growth!.After.Level);
            await tx.CommitAsync(default);
        }
        Assert.Equal(80L, await f.StomachAsync());
        Assert.Equal(110L, await f.ExperienceAsync());
        Assert.Equal(2L, await f.LevelAsync());
    }

    [Fact]
    public async Task OldSessionCannotOverwriteReplacementAfterPrepare()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        f.Enter();
        var replacement = f.Session with
        {
            ConnectionId = new NetworkConnectionId("Game:pet-replacement"), ConnectionGeneration = 2
        };
        await using (var tx = f.Scope.Begin())
        {
            await f.Runtime.AdvanceAsync(f.Session, default);
            f.Scope.Current!.Enlist(new object(), () => new PrepareCallback(() => f.Enter(replacement)));
            await tx.CommitAsync(default);
        }
        Assert.Equal(replacement, Assert.Single(f.Runtime.DueSessions()));
        Assert.Empty(await f.Runtime.AdvanceAsync(f.Session, default));
    }

    [Fact]
    public async Task DeadWornPetRevivesInTownAndNonHungerPetStaysFull()
    {
        await using var f = new Fixture();
        await f.SeedAsync(stomach: 0);
        f.Enter();
        Assert.Equal(1, Assert.Single(await f.Runtime.AdvanceAsync(f.Session, default)).After.Stomach);
        Assert.Equal(1L, await f.StomachAsync());

        f.Rules.HungerEnabled = false;
        f.Runtime.ChangeCombat(f.Session.CharacterId, true);
        f.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(100, Assert.Single(await f.Runtime.AdvanceAsync(f.Session, default)).After.Stomach);
        Assert.Equal(100L, await f.StomachAsync());
    }

    [Fact]
    public async Task RoomExperienceIsOncePerCellAndRollsBackItsReceipt()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        f.Enter();
        await f.Runtime.AdvanceAsync(f.Session, default);
        var run = new DungeonRunId(Guid.NewGuid());
        var cell = new MazeCell(1, 2);

        await using (f.Scope.Begin())
        {
            Assert.Equal(95, (await f.Runtime.GainRoomExperienceAsync(f.Session, run, cell, 5, default))!.After.Experience);
            Assert.Null(await f.Runtime.GainRoomExperienceAsync(f.Session, run, cell, 5, default));
        }
        Assert.Equal(90L, await f.ExperienceAsync());

        await using (var tx = f.Scope.Begin())
        {
            Assert.Equal(95, (await f.Runtime.GainRoomExperienceAsync(f.Session, run, cell, 5, default))!.After.Experience);
            await tx.CommitAsync(default);
        }
        await using (var tx = f.Scope.Begin())
        {
            Assert.Null(await f.Runtime.GainRoomExperienceAsync(f.Session, run, cell, 5, default));
            Assert.Equal(100, (await f.Runtime.GainRoomExperienceAsync(f.Session, run,
                new MazeCell(2, 2), 5, default))!.After.Experience);
            await tx.CommitAsync(default);
        }
        Assert.Equal(100L, await f.ExperienceAsync());

        await using (var tx = f.Scope.Begin())
        {
            Assert.Equal(105, (await f.Runtime.GainRoomExperienceAsync(f.Session,
                new DungeonRunId(Guid.NewGuid()), cell, 5, default))!.After.Experience);
            await tx.CommitAsync(default);
        }
    }

    [Fact]
    public async Task CombatTransitionSettlesElapsedTimeUnderThePreviousModeAndKeepsFractions()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        f.Enter();
        await f.Runtime.AdvanceAsync(f.Session, default);

        f.Clock.Advance(TimeSpan.FromSeconds(180));
        f.Runtime.ChangeCombat(f.Session.CharacterId, true); // Rest credited half a point.
        f.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Empty(await f.Runtime.AdvanceAsync(f.Session, default)); // 50.5 - 0.5 = 50.
        f.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(49, Assert.Single(await f.Runtime.AdvanceAsync(f.Session, default)).After.Stomach);

        f.Runtime.ChangeCombat(f.Session.CharacterId, false);
        f.Clock.Advance(TimeSpan.FromSeconds(180));
        Assert.Equal(50, Assert.Single(await f.Runtime.AdvanceAsync(f.Session, default)).After.Stomach);
        Assert.Equal(50L, await f.StomachAsync());
    }

    [Fact]
    public async Task IntegerZeroBeforeExactExhaustionDoesNotCountAsDeath()
    {
        await using var f = new Fixture();
        await f.SeedAsync(stomach: 1);
        f.Enter();
        await f.Runtime.AdvanceAsync(f.Session, default);
        f.Runtime.ChangeCombat(f.Session.CharacterId, true);

        f.Clock.Advance(TimeSpan.FromSeconds(59));
        var nearlyEmpty = Assert.Single(await f.Runtime.AdvanceAsync(f.Session, default));
        Assert.Equal(0, nearlyEmpty.After.Stomach);
        Assert.True(nearlyEmpty.After.ExactStomach > 0);

        f.Clock.Advance(TimeSpan.FromSeconds(1));
        var exhausted = Assert.Single(await f.Runtime.AdvanceAsync(f.Session, default));
        Assert.Equal(0, exhausted.After.Stomach);
        Assert.Equal(0, exhausted.After.ExactStomach);
    }

    [Fact]
    public async Task ProjectionsAppearOnlyAfterCommitInActionOrderAndRollbackDiscardsThem()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        f.Enter();
        await f.Runtime.AdvanceAsync(f.Session, default);
        Assert.Empty(f.Runtime.DrainProjections());

        await using (var tx = f.Scope.Begin())
        {
            await (await f.Runtime.Of(f.Session.CharacterId).CurrentPetAsync(default))!.AddStomachAsync(30, default);
            await f.Runtime.GainExperienceAsync(f.Session, 20, default);
            Assert.Empty(f.Runtime.DrainProjections());
            f.Scope.Current!.Enlist(new object(), () =>
                new PrepareCallback(() => Assert.Empty(f.Runtime.DrainProjections())));
            await tx.CommitAsync(default);
        }
        var projections = f.Runtime.DrainProjections();
        Assert.Equal(2, projections.Count);
        Assert.All(projections, projection => Assert.Equal(f.Session, projection.Session));
        Assert.Equal(80, Assert.Single(projections[0].Changes).After.Stomach);
        Assert.Equal(110, Assert.Single(projections[1].Changes).After.Experience);
        Assert.Empty(f.Runtime.DrainProjections());

        await using (f.Scope.Begin())
        {
            await (await f.Runtime.Of(f.Session.CharacterId).CurrentPetAsync(default))!.AddStomachAsync(30, default);
            await f.Runtime.GainExperienceAsync(f.Session, 10, default);
        }
        Assert.Empty(f.Runtime.DrainProjections());
        Assert.Equal(80L, await f.StomachAsync());
        Assert.Equal(110L, await f.ExperienceAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"dfo-pet-runtime-{Guid.NewGuid():N}.db");
        public DomainTransactionScope Scope { get; } = new();
        public MutableClock Clock { get; } = new();
        public RuleSource Rules { get; } = new();
        public Sessions Sessions { get; } = new();
        public SelectedSessionIdentity Session { get; } = SelectedSessionIdentity.Create(
            new AccountId(1), new CharacterId(7), new NetworkConnectionId("Game:pet-1"), 1, 10011);
        public CreatureRuntimeDomainService Runtime { get; }

        public Fixture()
        {
            CharacterDatabaseSchema.Ensure(
                new SqliteConnectionStringBuilder { DataSource = this.Path }.ToString());
            this.Runtime = new CreatureRuntimeDomainService(
                new SqliteCreatureDomainStore(this.Path), new CreatureRuleService(this.Rules), this.Scope,
                this.Sessions, this.Clock);
        }

        public void Enter(SelectedSessionIdentity? session = null)
        {
            var selected = session ?? this.Session;
            this.Sessions.Select(selected);
            this.Runtime.Enter(selected);
        }

        public async Task SeedAsync(int stomach = 50)
        {
            await using var connection = new SqliteConnection($"Data Source={this.Path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO accounts(account_id,m_id) VALUES(1,'pet-runtime');
                INSERT INTO characters(character_id,account_id,name,job,level)
                    VALUES(7,1,'owner',3,60);
                INSERT INTO items(item_uid,item_core,creature_field04,creature_mode_flag,
                    creature_progress_value,creature_mode1_field0a,creature_mode1_field0b,
                    creature_field_after_value,creature_text,creature_tail_flag)
                    VALUES(101,@core,@stomach,0,90,0,0,1,X'80',0);
                INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
                    VALUES('character',7,3,25,'world',101,101,1);
                """;
            var core = new byte[82];
            core[0] = 5;
            BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1), 63001);
            BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5), 101);
            command.Parameters.AddWithValue("@core", core);
            command.Parameters.AddWithValue("@stomach", stomach);
            await command.ExecuteNonQueryAsync();
        }

        public Task<long> StomachAsync() => this.ScalarAsync("creature_field04");
        public Task<long> ExperienceAsync() => this.ScalarAsync("creature_progress_value");
        public Task<long> LevelAsync() => this.ScalarAsync("creature_field_after_value");

        private async Task<long> ScalarAsync(string column)
        {
            await using var connection = new SqliteConnection($"Data Source={this.Path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {column} FROM items WHERE item_uid=101";
            return (long)(await command.ExecuteScalarAsync())!;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            File.Delete(this.Path);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 29, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => this.now;
        public void Advance(TimeSpan amount) => this.now += amount;
    }

    private sealed class RuleSource : ICreatureRuleSource
    {
        public bool HungerEnabled { get; set; } = true;
        public ValueTask<CreatureRule?> GetAsync(int templateId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<CreatureRule?>(new(templateId, 20, 20, this.HungerEnabled,
                [100, 200, 300], [], null, false));
        public ValueTask<CreatureArtifactExperienceRule?> GetArtifactExperienceAsync(
            int templateId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<CreatureArtifactExperienceRule?>(null);
    }

    private sealed class Sessions : ISelectedSessionDomainService
    {
        private SelectedSessionIdentity? selected;
        public SelectedSessionIdentity? Select(SelectedSessionIdentity session)
        {
            var previous = this.selected;
            this.selected = session;
            return previous;
        }
        public bool ReleaseExact(SelectedSessionIdentity session)
        {
            if (this.selected != session) return false;
            this.selected = null;
            return true;
        }
        public SelectedSessionIdentity? Find(AccountId accountId) =>
            this.selected?.AccountId == accountId ? this.selected : null;
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) =>
            this.selected?.CharacterId == characterId ? this.selected : null;
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) =>
            this.selected?.WireUserId == wireUserId ? this.selected : null;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) =>
            this.selected?.ConnectionId == connectionId ? this.selected : null;
    }

    private sealed class PrepareCallback(Action callback) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            callback();
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
    private sealed class CommitCallback(Action callback) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct) { callback(); return ValueTask.CompletedTask; }
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
}
