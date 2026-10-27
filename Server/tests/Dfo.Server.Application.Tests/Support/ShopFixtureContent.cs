using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Support;

// ShopFixture exercises real business services, stores and protocol commands against declared
// test content. Incidental current-view/quota reads must not acquire the developer's archive.
// Archive integration fixtures opt back into the real sources with RegisterArchive.
internal sealed class ShopFixtureContent : IGoldCarryLimitSource, IPremiumServiceDefinitionSource,
    ITownDefinitionSource, IDespairTowerContentSource, IEpicBookDefinitionSource,
    ICollectionBoxDefinitionSource, IDungeonDefinitionSource, IAltarDungeonContentSource,
    ITournamentDungeonContentSource, IGoldSilverHornContentSource, IDimensionDungeonContentSource, ILuckyStarRentalContentSource
{
    internal static void Register(IServiceCollection services)
    {
        var content = new ShopFixtureContent();
        var character = new TestCharacterContent();
        var rewards = new Dfo.Server.Application.Tests.World.TowerRewardTestContent();
        services.AddSingleton<ICharacterStatDefinitionSource>(character);
        services.AddSingleton<ISkillRuleDefinitionSource>(character);
        services.AddSingleton<IGoldCarryLimitSource>(content);
        services.AddSingleton<IClearRewardRateSource>(rewards);
        services.AddSingleton<IGoldDropTableSource>(rewards);
        services.AddSingleton<IDropItemPoolSource>(rewards);
        services.AddSingleton<IPremiumServiceDefinitionSource>(content);
        services.AddSingleton<ITownDefinitionSource>(content);
        services.AddSingleton<IDespairTowerContentSource>(content);
        services.AddSingleton<IDimensionDungeonContentSource>(content);
        services.AddSingleton<ILuckyStarRentalContentSource>(content);
        services.AddSingleton<IEpicBookDefinitionSource>(content);
        services.AddSingleton<ICollectionBoxDefinitionSource>(content);
        services.AddSingleton<IDungeonDefinitionSource>(content);
        services.AddSingleton<IAltarDungeonContentSource>(content);
        services.AddSingleton<ITournamentDungeonContentSource>(content);
        services.AddSingleton<IGoldSilverHornContentSource>(content);
        services.AddSingleton<ICreatureEquipmentLifetimeSource, StubCreatureEquipmentLifetimeSource>();
    }

    internal static void RegisterArchive(IServiceCollection services)
    {
        services.AddSingleton<ICharacterStatDefinitionSource, PvfCharacterStatDefinitionSource>();
        services.AddSingleton<ISkillRuleDefinitionSource, PvfSkillPointDefinitionSource>();
        services.AddSingleton<IGoldCarryLimitSource, PvfGoldCarryLimitSource>();
        services.AddSingleton<IClearRewardRateSource, PvfClearRewardRateSource>();
        services.AddSingleton<IGoldDropTableSource>(p => p.GetRequiredService<PvfDropRateTableSource>());
        services.AddSingleton<IDropItemPoolSource, PvfDropItemPoolSource>();
        services.AddSingleton<IPremiumServiceDefinitionSource, PvfPremiumServiceDefinitionSource>();
        services.AddSingleton<ITownDefinitionSource, PvfTownDefinitionSource>();
        services.AddSingleton<IDespairTowerContentSource, PvfDespairTowerContentSource>();
        services.AddSingleton<IDimensionDungeonContentSource, PvfDimensionDungeonContentSource>();
        services.AddSingleton<ILuckyStarRentalContentSource, PvfLuckyStarRentalContentSource>();
        services.AddSingleton<IEpicBookDefinitionSource, PvfEpicBookDefinitionSource>();
        services.AddSingleton<ICollectionBoxDefinitionSource, PvfCollectionBoxDefinitionSource>();
        services.AddSingleton<IDungeonDefinitionSource, PvfDungeonDefinitionSource>();
        services.AddSingleton<IAltarDungeonContentSource, PvfAltarDungeonContentSource>();
        services.AddSingleton<ITournamentDungeonContentSource, PvfTournamentDungeonContentSource>();
        services.AddSingleton<IGoldSilverHornContentSource, PvfGoldSilverHornContentSource>();
        services.AddSingleton<ICreatureEquipmentLifetimeSource, PvfCreatureEquipmentLifetimeSource>();
    }

    LuckyStarRentalCatalog ILuckyStarRentalContentSource.Load() => new(400, 100_000, 0, new HashSet<int>(), []);
    public ValueTask<int?> RentPointsAsync(int id, CancellationToken ct) => ValueTask.FromResult<int?>(null);
    public ValueTask<IReadOnlySet<byte>?> UsableJobsAsync(int id, CancellationToken ct) => ValueTask.FromResult<IReadOnlySet<byte>?>(null);

    public int BaseLimitFor(int level) => 100_000_000;
    public IReadOnlyList<int> ReadQuestHelperMinimumDropRates() => [100, 70, 30];
    public DoublePotRules ReadDoublePotRules() => new(8, new HashSet<int> { 7110 });
    public PremiumContractLevels ReadContractLevels() => new(5, 10);
    public double ReadGrowthExperienceBonusRate() => .2;
    public double ReadBlackDiamondExperienceBonusRate() => .05;
    public IReadOnlyList<int> ReadGrowthBossEquipmentBonusPercents() => [20, 23, 27, 30];
    public int ReadFreeWeaknessDailyLimit() => 10;
    public int ReadDungeonBuffDailyLimit() => 10;
    public int ReadAutoRepairDailyLimit() => 6;
    public int ReadFreeGoldCardDailyLimit() => 10;

    // No inn spawn is declared; teardown still persists the exact observed town/coordinates.
    public ValueTask<IReadOnlyList<TownDefinition>> GetAllAsync(CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<TownDefinition>>([]);
    public ValueTask<TownDefinition?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<TownDefinition?>(null);
    public ValueTask<MapGeometry?> GetMapGeometryAsync(string path, CancellationToken ct) => ValueTask.FromResult<MapGeometry?>(null);
    DespairTowerCatalog IDespairTowerContentSource.Load() => new(1, 1, []);
    IReadOnlyList<EpicPieceDefinition> IEpicBookDefinitionSource.Load() => [];
    ValueTask<IReadOnlyList<CollectionBoxDefinition>> ICollectionBoxDefinitionSource.GetAllAsync(CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<CollectionBoxDefinition>>([]);
    ValueTask<CollectionBoxDefinition?> ICollectionBoxDefinitionSource.GetAsync(int id, CancellationToken ct) =>
        ValueTask.FromResult<CollectionBoxDefinition?>(null);
    // Existing lifecycle fixtures construct the run and maze explicitly. This declaration supplies
    // only ordinary clear/leave policy; entry tests provide their own structural content.
    ValueTask<DungeonDefinition?> IDungeonDefinitionSource.GetAsync(int id, CancellationToken ct) =>
        ValueTask.FromResult<DungeonDefinition?>(new(id, "fixture", 1, false, []));
    ValueTask<AltarDungeonDefinition> IAltarDungeonContentSource.GetAsync(int id, CancellationToken ct) =>
        ValueTask.FromResult(new AltarDungeonDefinition(id, id == 11007 ? AltarDungeonKind.Ultimate : AltarDungeonKind.Endless,
            1, 1, 1, 0, [], new(0, 0, 0, 0, [], [], [], [], ""), 3));
    ValueTask<TournamentDefinition> ITournamentDungeonContentSource.GetAsync(int id, CancellationToken ct) =>
        ValueTask.FromResult(new TournamentDefinition(id, 1, 1, 1, 0, 0, [], [], [], [], [], 0));
    GoldSilverHornContent IGoldSilverHornContentSource.Load() => new(4123, 3);
    DimensionDungeonContent IDimensionDungeonContentSource.Load() => new(new HashSet<int>(), 3, 0,
        new Dictionary<(int, int), DimensionChroniclePool>(), new(3037, 1, DropRarity.Common, DropItemKind.Stackable));
}
