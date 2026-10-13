using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.World;

internal sealed class TowerRewardTestContent : IExperienceParameterSource, IClearRewardRateSource,
    IGoldDropTableSource, IDropItemPoolSource, IDungeonExperienceProgressionPolicySource, IGoldCarryLimitSource,
    IEquipmentDefinitionSource
{
    public ClearRewardTable Table { get; set; } = new("fixture", [new("default", [new(1, 200, 10000)])],
        [10000, 0], [0, 9500, 0, 500], [0], [1.02], [1], new Dictionary<int, int>(),
        new RarityLadder([500000, 799900, 999900, 1000000, 1000001, 1000002, 1000002]),
        new Dictionary<int, int>(), 1, [], [], Enumerable.Range(1, 200).ToDictionary(x => x, x => new ItemGradeBand(1, 1)));
    public ExperienceParameters Parameters { get; set; } = new([1.3], [1, 1.6], 1, 1, [0.05, 0.1, 0.12, 0.15, 0.2]);
    public int BaseLimitFor(int level) => 100000000;
    public ExperienceParameters Load() => Parameters;
    ClearRewardTable IClearRewardRateSource.Load(string path) => Table;
    GoldDropTable IGoldDropTableSource.Load(string path) => new(Enumerable.Range(1, 200).ToDictionary(x => x, x => 1000),
        Enumerable.Range(1, 200).ToDictionary(x => x, x => 0), [1], (100, 100));
    public DropItemPool Load(DropItemFamily family) => new(family,
        Enumerable.Range(0, 6).ToDictionary(x => (1, x), x => (IReadOnlyList<DropPoolEntry>)[new(3000, 1, 35)]));
    public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(1);
    public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int itemId, CancellationToken ct) =>
        ValueTask.FromResult<EquipmentDefinition?>(itemId == 3000 ? new(1, 1, 1, "weapon", 35) : null);
    DungeonExperienceProgressionPolicy IDungeonExperienceProgressionPolicySource.Load() => new(
        Enumerable.Range(1, 199).Select(x => (uint)x * 1000000).ToArray(), 0, 1, 100);
    public static void Register(IServiceCollection services, TowerRewardTestContent? content = null)
    {
        content ??= new();
        services.AddSingleton<IGoldCarryLimitSource>(content);
        services.AddSingleton<IExperienceParameterSource>(content);
        services.AddSingleton<IClearRewardRateSource>(content);
        services.AddSingleton<IGoldDropTableSource>(content);
        services.AddSingleton<IDropItemPoolSource>(content);
        services.AddSingleton<IEquipmentDefinitionSource>(content);
        services.AddSingleton<IDungeonExperienceProgressionPolicySource>(content);
    }
}
