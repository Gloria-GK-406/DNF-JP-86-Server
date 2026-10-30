using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Creatures;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Creatures;

public sealed class CreatureNamingDomainServiceTests
{
    private static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
        new AccountId(1), new CharacterId(2), new NetworkConnectionId("rename"), 1, 10011);
    private static readonly BusinessSlot CardSlot = new(BusinessInventorySpace.CreatureConsumable, 0);

    [Fact]
    public async Task Valid_card_renames_equipped_uid_and_consumes_one_card()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var store = new FakeCreatureStore();
            var (service, scope) = Service(store);
            await using var tx = scope.Begin();
            var receipt = await TestEffectFootprints.Run(Session, cap => service.RenameAsync(Session, CardSlot, [0x82, 0xA0], cap, default));

            Assert.NotNull(receipt);
            Assert.Equal(new byte[] { 0x82, 0xA0 }, receipt.Creature.NameBytes);
            Assert.Equal(1, receipt.RemainingSourceCore!.Count);
            Assert.Equal(1, store.Card.Item!.Count);
            Assert.Equal(1, store.Saves);
        });
    }

    [Theory]
    [InlineData(24, 500)]
    [InlineData(25, 499)]
    public async Task Wrong_card_or_equipped_uid_cannot_rename_for_free(int cardId, int wornUid)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var store = new FakeCreatureStore(cardId, wornUid);
            var (service, scope) = Service(store);
            await using var tx = scope.Begin();
            var receipt = await TestEffectFootprints.Run(Session, cap => service.RenameAsync(Session, CardSlot, [0x41], cap, default));

            Assert.Null(receipt);
            Assert.Equal(0, store.Saves);
            Assert.Equal(2, store.Card.Item!.Count);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Expired_card_or_worn_creature_cannot_rename_or_consume(bool expiredCard)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var store = new FakeCreatureStore(cardExpiry: expiredCard ? 1 : 0,
                wornExpiry: expiredCard ? 0 : 1);
            var (service, scope) = Service(store);
            await using var tx = scope.Begin();

            var receipt = await TestEffectFootprints.Run(Session, cap => service.RenameAsync(Session, CardSlot, [0x41], cap, default));

            Assert.Null(receipt);
            Assert.Equal(0, store.Saves);
            Assert.Equal(2, store.Card.Item!.Count);
        });
    }

    [Fact]
    public async Task Permanent_locked_card_and_pet_can_rename_after_2038_without_changing_growth()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var store=new FakeCreatureStore(cardExpiry:0,wornExpiry:0);
            await store.Card.UpdateAsync(store.Card.Item!, x => x with { EquipmentLockId=3 },default);
            var before=(await store.CurrentAsync(default))!;
            var clock=new AdvanceableTimeProvider(new DateTimeOffset(2050,1,1,0,0,0,TimeSpan.Zero));
            var (service,scope)=Service(store,clock);await using var tx=scope.Begin();
            var receipt=await TestEffectFootprints.Run(Session, cap => service.RenameAsync(Session,CardSlot,[], cap,default));
            Assert.NotNull(receipt);Assert.Empty(receipt.Creature.NameBytes);
            Assert.Equal((before.ItemUid,before.ItemTemplateId,before.Stomach,before.Experience,before.Level,before.ModeFlag,before.Mode1Field0A,before.Mode1Field0B,before.TailFlag,before.ExactStomach),
                (receipt.Creature.ItemUid,receipt.Creature.ItemTemplateId,receipt.Creature.Stomach,receipt.Creature.Experience,receipt.Creature.Level,
                    receipt.Creature.ModeFlag,receipt.Creature.Mode1Field0A,receipt.Creature.Mode1Field0B,receipt.Creature.TailFlag,receipt.Creature.ExactStomach));
            Assert.Equal((byte)3,receipt.RemainingSourceCore!.EquipmentLockId);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x1F)]
    [InlineData(0x7F)]
    public async Task Invalid_name_cannot_consume_a_card(byte value)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var store=new FakeCreatureStore();var (service,scope)=Service(store);await using var tx=scope.Begin();
            Assert.Null(await TestEffectFootprints.Run(Session, cap => service.RenameAsync(Session,CardSlot,[value], cap,default)));
            Assert.Equal(2,store.Card.Item!.Count);Assert.Equal(0,store.Saves);
        });
    }

    [Fact]
    public async Task Caller_array_mutation_after_plan_construction_cannot_change_the_committed_pet_name()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var store=new FakeCreatureStore();var name=Enumerable.Repeat((byte)0x41,12).ToArray();
            var plan=new SpecialConsumableEffectPlanRuleService(null!,null!,null!,null!,null!, clearance: TestRequired.Absent<Dfo.Server.Abstractions.Quests.IQuestClearancePlanRuleService>()).Rename(new(CardSlot,store.Card.Item,
                await store.GetContainer(BusinessInventorySpace.WornCreature).GetAsync(0,default),await store.CurrentAsync(default),
                System.Collections.Immutable.ImmutableArray.CreateRange(name),DateTimeOffset.UtcNow));
            name[0]=0;
            var effect=Assert.Single(plan.OfType<RenamePetEffect>());
            using var footprint = new Dfo.Server.Application.Tests.Support.TestEffectFootprints.Lease(Session);
            var result=await new RenamePetEffectHandler(new FakeCreatureDomain(store)).ApplyAsync(new(Session.AccountId,Session.CharacterId,DateTimeOffset.UtcNow,footprint,Session),effect,default);
            Assert.Equal(Enumerable.Repeat((byte)0x41,12),Assert.IsType<PetNameChange>(result).Creature.NameBytes);
        });
    }

    private static (CreatureNamingDomainService Service, DomainTransactionScope Scope) Service(FakeCreatureStore store,TimeProvider? clock=null)
    {
        var scope = TestRequired.TransactionScope();
        var creatures = new FakeCreatureDomain(store,clock);
        var slots = new Slots(store);
        var sessions = new FakeSessions();
        var executor = new ApplyEffectsDomainService([new ConsumeFromContainerEffectHandler(slots, store.Card.Lifecycle), new RenamePetEffectHandler(creatures)],
            scope, new EffectCharacterStore(new(Session.AccountId, Session.CharacterId)), sessions, ConsumptionEffectPreflight.For(slots));
        return (new CreatureNamingDomainService(creatures, sessions, clock ?? TimeProvider.System,
            new SpecialConsumableEffectPlanRuleService(null!, null!, null!, null!, null!, clearance: TestRequired.Absent<Dfo.Server.Abstractions.Quests.IQuestClearancePlanRuleService>()), executor), scope);
    }
    private sealed class Slots(FakeCreatureStore store) : IBusinessInventorySlotDomainService
    {
        public BoundInventorySlot? Find(CharacterId id, BusinessSlot slot) => slot.Index == 0 && store.FindContainer(slot.Space) is { } container ? new(slot,container) : null;
        public BoundInventorySlot Get(CharacterId id, BusinessSlot slot) => Find(id,slot) ?? throw new InvalidOperationException();
        public BoundInventorySlot FromStorage(InventoryStorageSlot slot) => throw new NotSupportedException();
    }

    private sealed class FakeCreatureDomain(FakeCreatureStore store,TimeProvider? clock=null) : ICreatureDomainService, ICreatureInstanceDomainService
    {
        public ICreatureStore GetStore(CharacterId characterId) => store;
        public ICreatureCollection Of(CharacterId characterId) => TestCreatureCollection.Create(characterId,
            new Dfo.Server.Application.Transactions.DomainTransaction(), store,
            state => store.SaveAsync(state, default).GetAwaiter().GetResult(), new NamingRules(),clock);
    }

    private sealed class NamingRules : ICreatureRuleService
    {
        public ValueTask<CreatureRule?> GetAsync(int template, CancellationToken ct) =>
            ValueTask.FromResult<CreatureRule?>(new(template, 20, 50, true, [2, 5], [], null, false));
        public ValueTask<CreatureArtifactExperienceRule?> GetArtifactExperienceAsync(int template, CancellationToken ct) =>
            ValueTask.FromResult<CreatureArtifactExperienceRule?>(null);
        public ValueTask<int> BirthExpirationAsync(int template, DateTimeOffset at, CancellationToken ct) => ValueTask.FromResult(0);
    }

    private sealed class FakeCreatureStore(int cardId = 25, int wornUid = 500,
        int cardExpiry = 0, int wornExpiry = 0) : ICreatureStore
    {
        public TestCreatureContainer Card { get; } = new(BusinessInventorySpace.CreatureConsumable,
            ItemSnapshot.FromCore(StackableItemCore.Create(7, cardId, 2) with
                { ExpireTime = cardExpiry }, true));
        private TestCreatureContainer Worn { get; } = new(BusinessInventorySpace.WornCreature,
            ItemSnapshot.FromCore(StackableItemCore.Create(5, 63023, wornUid) with
                { ExpireTime = wornExpiry }, false, new(wornUid), creature: new CreatureState(wornUid, 63023, 100, 0, 1, [], 0, 0, 0, 0).ToDetail()));
        private CreatureState Current { get; set; } = new(500, 63023, 100, 0, 1, [], 0, 0, 0, 0);
        public int Saves { get; private set; }
        public BusinessInventoryStoreKind Kind => BusinessInventoryStoreKind.Creature;
        public IBusinessItemContainer? FindContainer(BusinessInventorySpace space, bool isDefault = false) => space switch
        {
            BusinessInventorySpace.CreatureConsumable => Card,
            BusinessInventorySpace.WornCreature => Worn,
            _ => null,
        };
        public IBusinessItemContainer GetContainer(BusinessInventorySpace space, bool isDefault = false) =>
            FindContainer(space) ?? throw new InvalidOperationException();
        public ValueTask<CreatureState?> FindAsync(int itemUid, CancellationToken ct) =>
            ValueTask.FromResult<CreatureState?>(itemUid == Current.ItemUid ? Current : null);
        public ValueTask<IReadOnlyList<CreatureState>> ReadAllAsync(CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<CreatureState>>([Current]);
        public ValueTask<CreatureState?> CurrentAsync(CancellationToken ct) =>
            ValueTask.FromResult<CreatureState?>(Current);
        public ValueTask SaveAsync(CreatureState creature, CancellationToken ct)
        {
            Current = creature;
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

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => session;
        public bool ReleaseExact(SelectedSessionIdentity session) => true;
        public SelectedSessionIdentity? Find(AccountId accountId) => Session;
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => Session;
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => Session;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => Session;
    }
}
