using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Quests;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.ExpertJobs;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class ExpertStoreProtocolFlowTests
{
    private const ushort OpenStore = 0x0257;
    private const ushort VisitStore = 0x0258;
    private const ushort UseMachine = 0x00EB;
    private const ushort StoreOpened = 0x021B;
    private const ushort StoreClosed = 0x021C;
    private const short FirstSlot = 9;
    private const short SecondSlot = 10;
    private const int Price = 200;

    [Fact]
    public async Task Paid_disjoint_rolls_back_both_players_then_exhausts_the_exact_store_on_retry()
    {
        using var fixture = EquipmentDisjointTests.Fixture(configure: services =>
        {
            RegisterTown(services);
            services.AddSingleton<IExpertJobDefinitionSource, DisjointerContent>();
        }, equipmentGrade: 10);
        var ownerConnection = new NetworkConnectionContext(new("disjoint-owner"), NetworkEndpointKind.Game, 10011);
        var owner = AddOwner(fixture, ownerConnection);
        await SeedPiece(fixture, FirstSlot);
        await SeedPiece(fixture, SecondSlot);

        await Send(fixture, ownerConnection, OpenStore, OpenBody());
        Assert.Equal(new byte[] { 1 }, LastFrame(fixture, ownerConnection, 1, OpenStore)[15..]);
        Assert.Equal(0, LastFrame(fixture, fixture.Connection, 0, StoreOpened)[15]);
        await Send(fixture, fixture.Connection, VisitStore, BitConverter.GetBytes(owner.WireUserId));
        Assert.Equal(1, LastFrame(fixture, fixture.Connection, 1, VisitStore)[15]);

        var gameplay = fixture.Services.GetRequiredService<IExpertStoreGameplayDomainService>();
        var oldVisit = Assert.IsType<ExpertStoreVisit>(
            gameplay.FindUse(fixture.Session, owner.WireUserId, ExpertStoreKind.DisjointMachine));
        var ownerJob = fixture.Services.GetRequiredService<IExpertJobDomainService>().Get(owner.CharacterId);
        fixture.Sql("""
            CREATE TRIGGER fail_machine_update BEFORE UPDATE OF expert_job_exp ON character_expert_job
            WHEN OLD.character_id=3 BEGIN SELECT RAISE(ABORT, 'fixture late machine update'); END;
            """);

        var failure = await Record.ExceptionAsync(() =>
            Send(fixture, fixture.Connection, UseMachine, UseBody(owner.WireUserId, FirstSlot)).AsTask());
        Assert.True(failure is SqliteException, failure?.ToString() ?? string.Join(" ", Frames(fixture, fixture.Connection)
            .Where(frame => IsFrame(frame, 1, UseMachine)).Select(frame => Convert.ToHexString(frame[15..]))));
        Assert.Equal(1811, ((SqliteException)failure!).SqliteExtendedErrorCode);
        Assert.Equal(100000, fixture.Gold);
        Assert.Equal(100000, OwnerGold(fixture));
        Assert.Equal((ushort)45, await fixture.Durability(InventoryContainerType.Main, FirstSlot));
        Assert.Equal(0, fixture.Count(1000));
        Assert.Equal(1, (await ownerJob.GetStateAsync(default)).DisjointMachineEndurance);
        Assert.Same(oldVisit.Store, gameplay.FindUse(fixture.Session, owner.WireUserId, ExpertStoreKind.DisjointMachine)?.Store);
        Assert.DoesNotContain(Frames(fixture, ownerConnection), frame => IsFrame(frame, 0, StoreClosed));
        Assert.DoesNotContain(Frames(fixture, fixture.Connection), frame => IsFrame(frame, 0, StoreClosed));

        fixture.Sql("DROP TRIGGER fail_machine_update");
        await Send(fixture, fixture.Connection, UseMachine, UseBody(owner.WireUserId, FirstSlot));

        Assert.Equal(1, LastFrame(fixture, fixture.Connection, 1, UseMachine)[15]);
        Assert.Equal(100000 - Price, fixture.Gold);
        Assert.Equal(100000 + Price, OwnerGold(fixture));
        Assert.Null(await fixture.Durability(InventoryContainerType.Main, FirstSlot));
        Assert.Equal(11, fixture.Count(1000));
        var job = await ownerJob.GetStateAsync(default);
        Assert.Equal(0, job.DisjointMachineEndurance);
        Assert.Equal(2u, job.Experience);
        Assert.Null(gameplay.FindUse(fixture.Session, owner.WireUserId, ExpertStoreKind.DisjointMachine));
        Assert.Equal(owner.WireUserId, BinaryPrimitives.ReadUInt16LittleEndian(
            LastFrame(fixture, ownerConnection, 0, StoreClosed).AsSpan(15)));
        Assert.Equal(owner.WireUserId, BinaryPrimitives.ReadUInt16LittleEndian(
            LastFrame(fixture, fixture.Connection, 0, StoreClosed).AsSpan(15)));

        fixture.Sql("UPDATE character_expert_job SET disjoint_machine_endurance=1 WHERE character_id=3");
        await Send(fixture, ownerConnection, OpenStore, OpenBody());
        Assert.Equal(new byte[] { 1 }, LastFrame(fixture, ownerConnection, 1, OpenStore)[15..]);
        Assert.Null(await gameplay.ValidateUseAsync(oldVisit, default));
        await Send(fixture, fixture.Connection, UseMachine, UseBody(owner.WireUserId, SecondSlot));

        Assert.Equal(new byte[] { 0, 19 }, LastFrame(fixture, fixture.Connection, 1, UseMachine)[15..]);
        Assert.Equal((ushort)45, await fixture.Durability(InventoryContainerType.Main, SecondSlot));
        Assert.Equal(100000 - Price, fixture.Gold);
        Assert.Equal(100000 + Price, OwnerGold(fixture));
    }

    [Theory]
    [InlineData(true, 22)]
    [InlineData(false, 22)]
    [InlineData(true, 19)]
    public async Task Paid_enchant_settles_recipe_costs_and_only_applies_the_card_on_success(bool succeeds, int crystals)
    {
        const ushort enchant = 0x025A;
        const short cardSlot = 65;
        using var fixture = new ShopFixture(services =>
        {
            RegisterTown(services);
            services.AddSingleton<IExpertJobDefinitionSource>(new EnchanterContent(succeeds));
            services.AddSingleton<IEquipmentEnchantContentSource, EnchanterContent.CardCompatibility>();
        });
        var ownerConnection = new NetworkConnectionContext(new("enchant-owner"), NetworkEndpointKind.Game, 10011);
        var owner = AddOwner(fixture, ownerConnection, ExpertJobType.Enchanter);
        await SeedPiece(fixture, FirstSlot);
        await fixture.SeedCore(cardSlot, StackableItemCore.Create(2, EnchanterContent.Card, 1) with { EnchantUpgradeCount = 2 });
        await fixture.Seed(66, EnchanterContent.Solvent, 5);
        await fixture.Seed(67, EnchanterContent.Crystal, crystals);

        await Send(fixture, ownerConnection, OpenStore, OpenBody(ExpertStoreKind.EnchantShop));
        Assert.Equal(new byte[] { 1 }, LastFrame(fixture, ownerConnection, 1, OpenStore)[15..]);
        await Send(fixture, fixture.Connection, VisitStore, BitConverter.GetBytes(owner.WireUserId));
        Assert.Equal(1, LastFrame(fixture, fixture.Connection, 1, VisitStore)[15]);

        var body = new byte[13];
        BinaryPrimitives.WriteUInt16LittleEndian(body, owner.WireUserId);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(2), EnchanterContent.Recipe);
        body[6] = 2;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(8), FirstSlot);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(11), cardSlot);
        await Send(fixture, fixture.Connection, enchant, body);

        var acknowledgement = LastFrame(fixture, fixture.Connection, 1, enchant);
        var profession = fixture.Services.GetRequiredService<IExpertJobDomainService>().Get(owner.CharacterId);
        var state = await profession.GetStateAsync(default);
        if (crystals < 20)
        {
            Assert.Equal(new byte[] { 0, 22 }, acknowledgement[15..]);
            Assert.Equal(100000, fixture.Gold);
            Assert.Equal(100000, OwnerGold(fixture));
            Assert.Equal(1, fixture.Count(EnchanterContent.Card));
            Assert.Equal(5, fixture.Count(EnchanterContent.Solvent));
            Assert.Equal(crystals, fixture.Count(EnchanterContent.Crystal));
            Assert.Equal(0, InventoryItemCore.Decode(fixture.StoredCore(FirstSlot)).EnchantCardId);
            Assert.Equal(1, state.EnchanterEndurance);
            Assert.Equal(0u, state.Experience);
            return;
        }

        Assert.Equal(1, acknowledgement[15]);
        Assert.Equal(succeeds ? 1 : 0, acknowledgement[16]);
        Assert.Equal(100000 - Price, fixture.Gold);
        Assert.Equal(100000 + Price, OwnerGold(fixture));
        Assert.Equal(0, fixture.Count(EnchanterContent.Card));
        Assert.Equal(0, fixture.Count(EnchanterContent.Solvent));
        Assert.Equal(crystals - 20, fixture.Count(EnchanterContent.Crystal));
        var equipment = InventoryItemCore.Decode(fixture.StoredCore(FirstSlot));
        Assert.Equal(succeeds ? EnchanterContent.Card : 0, equipment.EnchantCardId);
        Assert.Equal(succeeds ? 2 : 0, equipment.EnchantUpgradeCount);
        Assert.Equal(0, state.EnchanterEndurance);
        Assert.Equal(succeeds ? 2u : 0u, state.Experience);
        Assert.Equal(owner.WireUserId, BinaryPrimitives.ReadUInt16LittleEndian(
            LastFrame(fixture, ownerConnection, 0, StoreClosed).AsSpan(15)));
        Assert.Equal(owner.WireUserId, BinaryPrimitives.ReadUInt16LittleEndian(
            LastFrame(fixture, fixture.Connection, 0, StoreClosed).AsSpan(15)));
    }

    [Theory]
    [InlineData(ExpertJobType.Disjointer, 0x00EC, 990, 100, 1)]
    [InlineData(ExpertJobType.Enchanter, 0x0264, 990, 100, 0)]
    [InlineData(ExpertJobType.Disjointer, 0x00F0, 500, 200, 2)]
    public async Task Device_maintenance_applies_the_rule_quote_to_gold_grade_and_endurance(
        ExpertJobType type, ushort opcode, int cost, int endurance, int grade)
    {
        using var fixture = EquipmentDisjointTests.Fixture(configure: services =>
        {
            RegisterTown(services);
            IExpertJobDefinitionSource definition = type == ExpertJobType.Enchanter
                ? new EnchanterContent(true) : new DisjointerContent();
            services.AddSingleton(definition);
        });
        var connection = new NetworkConnectionContext(new("expert-device"), NetworkEndpointKind.Game, 10011);
        var owner = AddOwner(fixture, connection, type);
        if (opcode == 0x00F0)
            fixture.Sql("UPDATE character_expert_job SET expert_job_exp = 20, disjoint_machine_endurance = 100 WHERE character_id = 3;");

        await Send(fixture, connection, opcode, []);

        Assert.Equal(1, LastFrame(fixture, connection, 1, opcode)[15]);
        Assert.Equal(100000 - cost, OwnerGold(fixture));
        var state = await fixture.Services.GetRequiredService<IExpertJobDomainService>()
            .Get(owner.CharacterId).GetStateAsync(default);
        Assert.Equal(grade, state.DisjointMachineGrade);
        Assert.Equal(endurance, type == ExpertJobType.Enchanter ? state.EnchanterEndurance : state.DisjointMachineEndurance);
    }

    private static SelectedSessionIdentity AddOwner(ShopFixture fixture, NetworkConnectionContext connection,
        ExpertJobType type = ExpertJobType.Disjointer)
    {
        fixture.Sql($"""
            INSERT INTO accounts(account_id,m_id) VALUES(2,'expert-owner');
            INSERT INTO characters(character_id,account_id,name,job,level,slot_index) VALUES(3,2,'owner',12,20,0);
            INSERT INTO character_stats(character_id,carried_gold) VALUES(3,100000);
            INSERT INTO character_condition(character_id,pc_room_state) VALUES(3,0);
            INSERT INTO character_pvp_record(character_id) VALUES(3);
            INSERT INTO character_pvp_skill_state(character_id) VALUES(3);
            INSERT INTO character_settings(character_id) VALUES(3);
            INSERT INTO character_expert_job(character_id,expert_job_type,disjoint_machine_endurance,
                disjoint_machine_grade,enchanter_endurance)
            VALUES(3,{(byte)type},{(type == ExpertJobType.Disjointer ? 1 : 0)},
                {(type == ExpertJobType.Disjointer ? 1 : 0)},{(type == ExpertJobType.Enchanter ? 1 : 0)});
            """);
        fixture.Sender.Outbound.Connections.Add(connection);
        return TestGameplay.Run(fixture.Services, () =>
        {
            var registry = fixture.Services.GetRequiredService<OnlineStateRegistry>();
            var user = registry.RegisterConnection(connection);
            registry.Authenticate(user.OnlineUserId, new(2));
            var owner = SelectedSessionIdentity.Create(new(2), new(3), connection.ConnectionId,
                registry.GetRequiredUser(user.OnlineUserId).Generation, 10011);
            registry.Select(owner);
            registry.EnterWorld(owner);
            var position = new CharacterPositionState(1, 0, 0, 0, 0);
            fixture.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().Begin(owner, position, 100);
            var town = fixture.Services.GetRequiredService<ITownAreaDomainService>();
            town.Enter(new TownAreaMember(owner, new(1, 1, 0), position, true));
            town.Enter(new TownAreaMember(fixture.Session, new(1, 1, 0), position, true));
            return owner;
        });
    }

    private static Task SeedPiece(ShopFixture fixture, short slot) =>
        fixture.SeedCore(slot, StackableItemCore.Create(1, 3000, 1234) with { Durability = 45 });

    private static int OwnerGold(ShopFixture fixture) =>
        fixture.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=3");

    private static ValueTask Send(ShopFixture fixture, NetworkConnectionContext connection, ushort opcode, byte[] body) =>
        EffectRequestTransport.SendRaw(fixture, connection,
            new(1, opcode, 0, EffectRequestTransport.FreshSequence(connection)), body);

    private static byte[] OpenBody(ExpertStoreKind kind = ExpertStoreKind.DisjointMachine)
    {
        var body = new byte[15];
        body[0] = (byte)kind;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(5), Price);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(9), 100);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(11), 100);
        return body;
    }

    private static byte[] UseBody(ushort owner, short slot)
    {
        var body = new byte[5];
        BinaryPrimitives.WriteUInt16LittleEndian(body, owner);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(2), slot);
        return body;
    }

    private static IEnumerable<byte[]> Frames(ShopFixture fixture, NetworkConnectionContext connection) =>
        fixture.Sender.Outbound.Entries.Where(entry => !entry.IsClose && entry.Connection.ConnectionId == connection.ConnectionId)
            .SelectMany(fixture.Sender.Outbound.FramesOf);

    private static byte[] LastFrame(ShopFixture fixture, NetworkConnectionContext connection, byte selector, ushort opcode) =>
        Frames(fixture, connection).Last(frame => IsFrame(frame, selector, opcode));

    private static bool IsFrame(byte[] frame, byte selector, ushort opcode) =>
        frame[0] == selector && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == opcode;

    private static void RegisterTown(IServiceCollection services)
    {
        services.AddSingleton<ITownDefinitionSource, CommercialTown>();
        services.AddSingleton<IQuestContentRuleService>(new OrdinaryQuestEffectTests.Rules());
        services.AddSingleton<ISelectedCharacterCurrentViewDomainService, QuestTestCommands.PublishedView>();
    }

    private sealed class CommercialTown : ITownDefinitionSource
    {
        private static readonly TownDefinition Town = new(1, "Expert test",
            [new(0, "commercial.map", TownAreaKind.Normal, 0, 0)]);
        public ValueTask<IReadOnlyList<TownDefinition>> GetAllAsync(CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<TownDefinition>>([Town]);
        public ValueTask<TownDefinition?> GetAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<TownDefinition?>(id == 1 ? Town : null);
        public ValueTask<MapGeometry?> GetMapGeometryAsync(string path, CancellationToken ct) =>
            ValueTask.FromResult<MapGeometry?>(path == "commercial.map" ? new([], [new(0, 0, 1000, 1000)]) : null);
    }

    private sealed class EnchanterContent(bool succeeds) : IExpertJobDefinitionSource
    {
        internal const int Recipe = 10015129;
        internal const int Card = 3619;
        internal const int Solvent = 3227;
        internal const int Crystal = 3166;

        private readonly ExpertJobDefinition definition = new(
            ExpertJobType.Enchanter, [20], [1000], new HashSet<int>(), new HashSet<int>(),
            new Dictionary<int, byte>(), 0, 300, new Dictionary<int, int>(), new Dictionary<byte, int> { [0] = 1 })
        {
            MaximumStoreCharge = 1000,
            Enchantment = new(
                new Dictionary<int, EnchanterCardRecipe>
                {
                    [Recipe] = new(0, 1, 191, [new(Solvent, 5), new(Crystal, 20)],
                        new Dictionary<int, int> { [Card] = 2600313 }),
                },
                new Dictionary<int, EnchanterCardRate> { [1] = new([succeeds ? 100 : 0], new(2, 2)) },
                1, 1, [new(1000, 100)]),
        };

        public ValueTask<ExpertJobDefinition?> FindAsync(ExpertJobType type, CancellationToken ct) =>
            ValueTask.FromResult<ExpertJobDefinition?>(type == ExpertJobType.Enchanter ? definition : null);
        public ValueTask<ExpertJobType?> FindDrawingJobAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<ExpertJobType?>(itemId == Recipe ? ExpertJobType.Enchanter : null);

        internal sealed class CardCompatibility : IEquipmentEnchantContentSource
        {
            public ValueTask<EquipmentEnchantDefinition?> ResolveAsync(int bead, int equipment, CancellationToken ct) =>
                ValueTask.FromResult<EquipmentEnchantDefinition?>(null);

            public ValueTask<bool> ValidateExpertCardAsync(int card, byte upgrade, int? equipment, CancellationToken ct) =>
                ValueTask.FromResult(card == Card && upgrade == 2 && equipment == 3000);
        }
    }

    private sealed class DisjointerContent : IExpertJobDefinitionSource
    {
        private static readonly ExpertJobDefinition Disjointer = new(
            ExpertJobType.Disjointer, [20, 40], [1000], new HashSet<int>(), new HashSet<int>(),
            new Dictionary<int, byte>(), 0, 300, new Dictionary<int, int>(), new Dictionary<byte, int>())
        {
            MaximumStoreCharge = 1000,
            DisjointMachine = new(10, 1, 1, 2, 2, 0, 0, 0,
                new Dictionary<(int, int, int), DisjointMachineMaterialRule> { [(0, 1, 0)] = new(1000, 1, 0, 0, 0) },
                [], [], [new(1000, 100), new(2000, 200)],
                new Dictionary<int, int> { [2] = 500 }, new Dictionary<int, int> { [2] = 1 }),
        };

        public ValueTask<ExpertJobDefinition?> FindAsync(ExpertJobType type, CancellationToken ct) =>
            ValueTask.FromResult<ExpertJobDefinition?>(type == ExpertJobType.Disjointer ? Disjointer : null);
        public ValueTask<ExpertJobType?> FindDrawingJobAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<ExpertJobType?>(null);
    }
}
