using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Creatures;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Creatures;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Application.Tests.Creatures;

public sealed class CreatureTransformationTests
{
    private static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
        new AccountId(1), new CharacterId(2), new NetworkConnectionId("transform"), 1, 10011);

    [Fact]
    public async Task Egg_handle_expires_after_move_and_refound_egg_returns_pet_that_can_grow()
    {
        var fixture = new Fixture(new CreatureState(500, 63022, 31, 42, 7, [0x41], 1, 2, 3, 4),
            BusinessInventorySpace.CreatureInventory);
        await using var tx = fixture.Scope.Begin();
        var pets = new FakeCreatureDomain(fixture.Store, fixture.Scope, fixture.Rules).Of(Session.CharacterId);
        var egg = (await pets.FindEggAsync(500, default))!;
        var container = fixture.Store.GetContainer(BusinessInventorySpace.CreatureInventory);
        var source = (await container.GetAsync(0, default))!;
        await container.PutAsync(10, await container.TakeAsync(source, null, default), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => egg.BirthAsync(default).AsTask());
        egg = (await pets.FindEggAsync(500, default))!;
        var pet = await egg.BirthAsync(default);
        Assert.NotNull(pet);
        Assert.Equal(500, pet.ItemUid);
        Assert.Equal(63023, (await pet.ReadAsync(default)).ItemTemplateId);
        Assert.Equal(10, ((PetInstance)pet).BirthReceipt!.Slot.Index);
        Assert.Null(await egg.BirthAsync(default));
        Assert.Null(await pets.FindEggAsync(500, default));
        await pet.AddExpAsync(2, default);
        await pet.RenameAsync([0x42], default);
        var state = await pet.ReadAsync(default);
        Assert.Equal((2, 2, 100), (state.Experience, state.Level, state.Stomach));
        Assert.Equal(new byte[] { 0x42 }, state.NameBytes);
    }

    [Fact]
    public async Task Hatch_reuses_egg_uid_and_initializes_creature_detail()
    {
        var egg = new CreatureState(500, 63022, 31, 42, 7, [0x41], 1, 2, 3, 4);
        var fixture = new Fixture(egg, BusinessInventorySpace.CreatureInventory);
        await using var tx = fixture.Scope.Begin();
        var result = await fixture.Service.HatchAsync(Session,
            new BusinessSlot(BusinessInventorySpace.CreatureInventory, 0), default);

        Assert.NotNull(result);
        Assert.Equal(500, result.After.ItemUid);
        Assert.Equal(63023, result.After.ItemTemplateId);
        Assert.Equal((100, 0, 1), (result.After.Stomach, result.After.Experience, result.After.Level));
        Assert.Empty(result.After.NameBytes);
        Assert.Equal(500, result.Core.ToCore().Value);
        Assert.Equal(63023, result.Core.ItemId);
        Assert.Equal(1, fixture.Store.Saves);
    }

    [Fact]
    public async Task Auto_evolution_preserves_progress_and_refuses_quest_or_unverified_target()
    {
        var pet = new CreatureState(500, 63000, 61, 2151, 20, [0x41], 0, 0, 0, 0);
        var fixture = new Fixture(pet, BusinessInventorySpace.WornCreature);
        await using var tx = fixture.Scope.Begin();
        var result = await (await fixture.Service.Of(Session.CharacterId).CurrentPetAsync(default))!.EvolveAsync(default);

        Assert.NotNull(result);
        Assert.Equal(pet with
        {
            ItemTemplateId = 63001, NameBytes = result.After.NameBytes, ExactStomach = pet.ExactStomach
        }, result.After);
        Assert.Equal(pet.NameBytes, result.After.NameBytes);
        Assert.Equal(63001, result.Core.ItemId);
        Assert.Equal(500, result.Core.ToCore().Value);

        fixture.Rules.Rules[63001] = fixture.Rules.Rules[63001] with { CreatureId = 99 };
        fixture.Store.Set(pet);
        Assert.Null(await (await fixture.Service.Of(Session.CharacterId).CurrentPetAsync(default))!.EvolveAsync(default));
        Assert.Equal(1, fixture.Store.Saves);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("not-egg")]
    [InlineData("stale-session")]
    [InlineData("wrong-slot")]
    [InlineData("wrong-target")]
    public async Task Invalid_hatch_is_refused_without_saving_creature(string cause)
    {
        var fixture = new Fixture(new CreatureState(500, cause == "not-egg" ? 63000 : 63022,
            100, 0, 1, [], 0, 0, 0, 0), BusinessInventorySpace.CreatureInventory);
        await using var tx = fixture.Scope.Begin();
        if (cause == "expired")
            await fixture.Store.GetContainer(BusinessInventorySpace.CreatureInventory).UpdateAsync(
                (await fixture.Store.GetContainer(BusinessInventorySpace.CreatureInventory).GetAsync(0, default))!, x => x with { ExpireTime = 1 }, default);
        if (cause == "wrong-target")
            fixture.Rules.Rules[63023] = fixture.Rules.Rules[63023] with { CreatureId = 99 };
        var session = cause == "stale-session" ? Session with { ConnectionGeneration = 2 } : Session;
        var slot = new BusinessSlot(BusinessInventorySpace.CreatureInventory, cause == "wrong-slot" ? 140 : 0);
        Assert.Null(await fixture.Service.HatchAsync(session, slot, default));
        Assert.Equal(0, fixture.Store.Saves);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Hatch_and_level_up_evolution_commit_or_rollback_core_details_and_clock_on_the_same_sqlite_uid(bool commit)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"dfo-creature-transform-{Guid.NewGuid():N}.db");
        try
        {
            CharacterDatabaseSchema.Ensure(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO accounts(account_id,m_id) VALUES(1,'pet-transform');
                    INSERT INTO characters(character_id,account_id,name,job,level)
                        VALUES(2,1,'owner',3,60);
                    INSERT INTO items(item_uid,item_core,creature_field04,creature_mode_flag,
                        creature_progress_value,creature_mode1_field0a,creature_mode1_field0b,
                        creature_field_after_value,creature_text,creature_tail_flag)
                        VALUES(500,@egg,31,1,42,2,3,7,X'41',4),
                              (501,@pet,61,0,2150,0,0,19,X'41',0);
                    INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
                        VALUES('character',2,7,0,'world',500,500,1),
                              ('character',2,3,25,'world',501,501,1);
                    """;
                command.Parameters.AddWithValue("@egg", Core(63022, 500));
                command.Parameters.AddWithValue("@pet", Core(63000, 501));
                await command.ExecuteNonQueryAsync();
            }

            var scope = new DomainTransactionScope();
            var domain = new SqliteCreatureAdapter(path, scope);
            var rules = new FakeRules();
            rules.Rules[63000] = rules.Rules[63000] with
            {
                ExperienceThresholds = [.. Enumerable.Range(1, 18).Select(x => x * 100), 2151]
            };
            var runtime = new CreatureRuntimeDomainService(new SqliteCreatureDomainStore(path),
                new CreatureRuleService(rules, new FakeLifetimes()), scope, new FakeSessions(), TimeProvider.System, domain);
            domain.Instances = runtime;
            runtime.Enter(Session);
            await runtime.AdvanceAsync(Session, default);
            await using (var transaction = scope.Begin())
            {
                Assert.NotNull(await runtime.HatchAsync(Session,
                    new BusinessSlot(BusinessInventorySpace.CreatureInventory, 0), default));
                Assert.Null(await runtime.HatchAsync(Session,
                    new BusinessSlot(BusinessInventorySpace.CreatureInventory, 0), default));
                var growth = await runtime.GainExperienceAsync(Session, 1, default);
                Assert.NotNull(growth?.Transformation);
                Assert.Equal(2, growth.Transformation.CreatureId);
                Assert.Equal(501, growth.After.ItemUid);
                if (commit) await transaction.CommitAsync(default);
            }

            await runtime.AdvanceAsync(Session, default);
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT item_uid, substr(item_core,2,4), creature_field04,
                        creature_progress_value, creature_field_after_value,
                        hex(creature_text)
                    FROM items WHERE item_uid IN (500,501) ORDER BY item_uid;
                    """;
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(500, reader.GetInt64(0));
                Assert.Equal(commit ? 63023 : 63022, BinaryPrimitives.ReadInt32LittleEndian((byte[])reader.GetValue(1)));
                Assert.Equal(commit ? (100L, 0L, 1L, "") : (31L, 42L, 7L, "41"),
                    (reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetString(5)));
                Assert.True(await reader.ReadAsync());
                Assert.Equal(501, reader.GetInt64(0));
                Assert.Equal(commit ? 63001 : 63000, BinaryPrimitives.ReadInt32LittleEndian((byte[])reader.GetValue(1)));
                Assert.Equal(commit ? (61L, 2151L, 20L, "41") : (61L, 2150L, 19L, "41"),
                    (reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetString(5)));
                Assert.False(await reader.ReadAsync());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static byte[] Core(int template, int uid)
    {
        var core = new byte[82];
        core[0] = 5;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1), template);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5), uid);
        return core;
    }

    private sealed class SqliteCreatureAdapter(string path, DomainTransactionScope scope)
        : ICreatureDomainService, ICreatureStore, ICharacterBusinessInventoryDomainService
    {
        private readonly SqliteCreatureDomainStore creatures = new(path);
        private readonly SqliteItemDomainStore items = new(path);
        public ICreatureInstanceDomainService Instances { get; set; } = null!;
        public ICreatureCollection Of(CharacterId characterId) => Instances.Of(characterId);
        public ICreatureStore GetStore(CharacterId characterId) => this;
        public IBusinessInventoryStore GetStore(CharacterId characterId, BusinessInventoryStoreKind kind) => this;
        public BusinessInventoryStoreKind Kind => BusinessInventoryStoreKind.Creature;
        public IBusinessItemContainer? FindContainer(BusinessInventorySpace space, bool isDefault = false) => space switch
        {
            BusinessInventorySpace.CreatureInventory => new SqliteContainer(this, space,
                InventoryContainerType.Pet, 0),
            BusinessInventorySpace.WornCreature => new SqliteContainer(this, space,
                InventoryContainerType.Equipment, 25),
            _ => null
        };
        public IBusinessItemContainer GetContainer(BusinessInventorySpace space, bool isDefault = false) =>
            FindContainer(space) ?? throw new InvalidOperationException();
        public ValueTask<CreatureState?> FindAsync(int itemUid, CancellationToken ct) =>
            CreatureSession().ReadAsync(Session.CharacterId, itemUid, ct);
        public ValueTask<IReadOnlyList<CreatureState>> ReadAllAsync(CancellationToken ct) =>
            CreatureSession().ReadAllAsync(Session.CharacterId, ct);
        public ValueTask<CreatureState?> CurrentAsync(CancellationToken ct) =>
            CreatureSession().ReadCurrentAsync(Session.CharacterId, ct);
        public ValueTask SaveAsync(CreatureState creature, CancellationToken ct)
        {
            var session = CreatureSession();
            var reference = session.FindItemAsync(Session.CharacterId, creature.ItemUid, ct).GetAwaiter().GetResult()!;
            session.WriteAsync(reference, creature, null, ct).GetAwaiter().GetResult();
            return ValueTask.CompletedTask;
        }
        public ValueTask ReviveIfEmptyAsync(int itemUid, CancellationToken ct) =>
            throw new NotSupportedException();
        private ICreatureDomainStoreSession CreatureSession() =>
            creatures.SessionIn(scope.Current ?? throw new InvalidOperationException());
        private IItemDomainStoreSession ItemSession() =>
            items.SessionIn(scope.Current ?? throw new InvalidOperationException());

        private sealed class SqliteContainer(SqliteCreatureAdapter owner, BusinessInventorySpace space,
            InventoryContainerType type, int physical) : IBusinessItemContainer
        {
            public BusinessContainerId Id => new(space, StorageId);
            public ContainerId StorageId => ContainerId.ForCharacter(Session.CharacterId, type);
            public BusinessInventorySpace Space => space;
            public ValueTask<IEquipmentInstance?> GetEquipmentAsync(int index, CancellationToken ct) => ValueTask.FromResult<IEquipmentInstance?>(null);
            public async ValueTask<ItemReference?> GetAsync(int index, CancellationToken ct) => index != 0 ? null : (await owner.ItemSession().GetAsync(StorageId, physical, ct))?.WithBusinessSlot(new(space, index));
            public async ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(CancellationToken ct) => await GetAsync(0, ct) is { } item ? [item] : [];
            public ValueTask<ItemRecord> TakeAsync(ItemReference item, int? count, CancellationToken ct) => owner.ItemSession().TakeAsync(item, count, ct);
            public ValueTask<ItemReference> PutAsync(int index, ItemRecord item, CancellationToken ct) => owner.ItemSession().PutAsync(StorageId, physical, item, ct);
            public ValueTask<ItemReference> UpdateAsync(ItemReference item, Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct) => owner.ItemSession().UpdateAsync(item, change, ct);
            public ValueTask FlushAsync(CancellationToken ct) => ValueTask.CompletedTask;
            public ValueTask<BusinessItemAdditionResult> AddAsync(int itemId, int count,
                CancellationToken ct) => throw new NotSupportedException();
        }
    }

    private sealed class Fixture
    {
        public DomainTransactionScope Scope { get; } = new();
        public FakeCreatureStore Store { get; }
        public FakeRules Rules { get; } = new();
        public CreatureRuntimeDomainService Service { get; }

        public Fixture(CreatureState state, BusinessInventorySpace space)
        {
            Store = new FakeCreatureStore(state, space);
            Service = new CreatureRuntimeDomainService(new RuntimeStore(Store),
                new CreatureRuleService(Rules, new FakeLifetimes()), Scope,
                new FakeSessions(), TimeProvider.System, new Inventories(Store));
        }
    }

    private sealed class RuntimeStore(FakeCreatureStore store) : ICreatureDomainStore
    {
        public object StoreIdentity { get; } = new();
        public bool IsDurable => false;
        public ICreatureDomainStoreSession SessionIn(IDomainTransaction transaction) =>
            new TestCreatureCollection.Session(store,
                state => store.SaveAsync(state, default).GetAwaiter().GetResult());
    }

    private sealed class Inventories(FakeCreatureStore store) : ICharacterBusinessInventoryDomainService
    {
        public IBusinessInventoryStore GetStore(CharacterId characterId, BusinessInventoryStoreKind kind) => store;
    }

    private sealed class FakeRules : ICreatureRuleSource
    {
        public Dictionary<int, CreatureRule> Rules { get; } = new()
        {
            [63022] = Rule(63022, 20, true, 63023),
            [63023] = Rule(63023, 20),
            [63000] = Rule(63000, 1) with
            {
                OutputTemplateId = 63001,
                Evolutions = [new CreatureEvolution(20, null, 2)]
            },
            [63001] = Rule(63001, 2)
        };

        private static CreatureRule Rule(int template, int creatureId, bool egg = false, int? hatch = null) =>
            new(template, creatureId, 50, true, [2, 5], [], hatch, egg);

        public ValueTask<CreatureRule?> GetAsync(int itemTemplateId, CancellationToken ct) =>
            ValueTask.FromResult(Rules.GetValueOrDefault(itemTemplateId));
        public ValueTask<CreatureArtifactExperienceRule?> GetArtifactExperienceAsync(int itemTemplateId,
            CancellationToken ct) => ValueTask.FromResult<CreatureArtifactExperienceRule?>(null);
    }

    private sealed class FakeLifetimes : ICreatureEquipmentLifetimeSource
    {
        public ValueTask<CreatureEquipmentLifetime> GetAsync(int itemTemplateId,
            DateTimeOffset preparedAt, CancellationToken ct) => ValueTask.FromResult(default(CreatureEquipmentLifetime));
    }

    private sealed class FakeCreatureDomain(FakeCreatureStore store, DomainTransactionScope scope, FakeRules rules) : ICreatureDomainService
    {
        public ICreatureStore GetStore(CharacterId characterId) => store;
        public ICreatureCollection Of(CharacterId characterId) => TestCreatureCollection.Create(characterId,
            scope.Current!, store, state => store.SaveAsync(state, default).GetAwaiter().GetResult(),
            new CreatureRuleService(rules, new FakeLifetimes()));
    }

    private sealed class FakeCreatureStore : ICreatureStore
    {
        private CreatureState state;
        private readonly BusinessInventorySpace occupied;
        private readonly TestCreatureContainer container;
        public int Saves { get; private set; }
        public FakeCreatureStore(CreatureState state, BusinessInventorySpace space)
        {
            this.state = state;
            occupied = space;
            container = new TestCreatureContainer(space,
                ItemSnapshot.FromCore(StackableItemCore.Create(5, state.ItemTemplateId, state.ItemUid), false, new(state.ItemUid), creature: state.ToDetail()));
        }
        public void Set(CreatureState value) => state = value;
        public BusinessInventoryStoreKind Kind => BusinessInventoryStoreKind.Creature;
        public IBusinessItemContainer? FindContainer(BusinessInventorySpace space, bool isDefault = false) =>
            space == occupied ? container : null;
        public IBusinessItemContainer GetContainer(BusinessInventorySpace space, bool isDefault = false) =>
            FindContainer(space) ?? throw new InvalidOperationException();
        public ValueTask<CreatureState?> FindAsync(int itemUid, CancellationToken ct) =>
            ValueTask.FromResult<CreatureState?>(itemUid == state.ItemUid ? state : null);
        public ValueTask<IReadOnlyList<CreatureState>> ReadAllAsync(CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<CreatureState>>([state]);
        public ValueTask<CreatureState?> CurrentAsync(CancellationToken ct) =>
            ValueTask.FromResult<CreatureState?>(occupied == BusinessInventorySpace.WornCreature ? state : null);
        public ValueTask SaveAsync(CreatureState creature, CancellationToken ct)
        {
            state = creature;
            Saves++;
            return ValueTask.CompletedTask;
        }
        public ValueTask ReviveIfEmptyAsync(int itemUid, CancellationToken ct) => ValueTask.CompletedTask;
    }

    private sealed class FakeSessions : ISelectedSessionDomainService
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

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => session;
        public bool ReleaseExact(SelectedSessionIdentity session) => true;
        public SelectedSessionIdentity? Find(AccountId accountId) => Session;
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => Session;
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => Session;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => Session;
    }
}
