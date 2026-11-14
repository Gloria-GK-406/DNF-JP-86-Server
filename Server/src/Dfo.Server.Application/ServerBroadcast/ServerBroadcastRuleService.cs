using System.Collections.Frozen;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.ServerBroadcast;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.DomainEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.ServerBroadcast;

/// <summary>Reads <c>ServerBroadcast:*</c> once; <c>Enabled</c> turns every kind off together.</summary>
internal sealed class ServerBroadcastRuleService : IServerBroadcastRuleService
{
    /// <summary>The second awakening, in the grow type's awakening nibble.</summary>
    private const int SecondAwakening = 2;

    private readonly FrozenSet<ServerBroadcastKind> enabled;
    private readonly IExperienceRuleService experience;
    private readonly IEquipmentDefinitionSource equipment;
    private readonly IExtraDropContentSource extraDrops;

    public ServerBroadcastRuleService(IServerConfigService settings, IExperienceRuleService experience,
        IEquipmentDefinitionSource equipment, IExtraDropContentSource extraDrops)
    {
        this.experience = experience;
        this.equipment = equipment;
        this.extraDrops = extraDrops;
        enabled = settings.GetConfig<bool>("ServerBroadcast:Enabled")
            ? Enum.GetValues<ServerBroadcastKind>().Where(kind => settings.GetConfig<bool>("ServerBroadcast:" + kind)).ToFrozenSet()
            : FrozenSet<ServerBroadcastKind>.Empty;
        ForgingMinimumStage = Positive(settings, "ServerBroadcast:ForgingMinimumStage");
        LotteryMinimumRarity = Positive(settings, "ServerBroadcast:LotteryMinimumRarity");
        AncientDungeonMinimumRarity = Positive(settings, "ServerBroadcast:AncientDungeonMinimumRarity");
    }

    public bool Announces(ServerBroadcastKind kind) => enabled.Contains(kind);

    public int? TopLevelReached(int before, int after) =>
        experience.CharacterScale().TopLevel is var top && before < top && after == top ? top : null;

    public bool IsSecondAwakening(int growType) => growType >> 4 == SecondAwakening;

    public int ForgingMinimumStage { get; }

    private int LotteryMinimumRarity { get; }

    private int AncientDungeonMinimumRarity { get; }

    public async ValueTask<bool> IsNotableLotteryPrizeAsync(int itemId, CancellationToken ct) =>
        await RarityAsync(itemId, ct).ConfigureAwait(false) >= LotteryMinimumRarity;

    // The 70 server announced non-stackable finds in such dungeons by a flag this archive does not
    // carry readably; rarity stands in for it.
    public async ValueTask<bool> IsNotableAncientFindAsync(int dungeonId, int itemId, CancellationToken ct) =>
        extraDrops.Load().Dungeons.TryGetValue(dungeonId, out var type) && type == ExtraDropDungeonType.Ancient
        && await RarityAsync(itemId, ct).ConfigureAwait(false) >= AncientDungeonMinimumRarity;

    // Only equipment carries a rarity worth announcing; anything else never reaches a threshold.
    private async ValueTask<int> RarityAsync(int itemId, CancellationToken ct) =>
        (await equipment.GetEquipmentAsync(itemId, ct).ConfigureAwait(false))?.Rarity ?? -1;

    private static int Positive(IServerConfigService settings, string key) =>
        settings.GetConfig<int>(key) is var value and > 0
            ? value : throw new InvalidOperationException($"{key} must be positive.");
}

internal static class ServerBroadcastRegistration
{
    internal static IServiceCollection AddServerBroadcast(this IServiceCollection services)
    {
        services.TryAddSingleton<IServerBroadcastRuleService, ServerBroadcastRuleService>();
        services.AddDomainEvent<EquipmentUpgradeAnnounced>("broadcast.equipment-upgrade", 1);
        services.AddDomainEvent<VendingPrizeAnnounced>("broadcast.vending-prize", 1);
        services.AddDomainEvent<BoxRewardAnnounced>("broadcast.box-reward", 1);
        services.AddDomainEvent<EquipmentForgingAnnounced>("broadcast.equipment-forging", 1);
        services.AddDomainEvent<RecipeCraftAnnounced>("broadcast.recipe-craft", 1);
        services.AddDomainEvent<DespairTowerFloorAnnounced>("broadcast.despair-tower-floor", 1);
        services.AddDomainEvent<AncientDungeonItemAnnounced>("broadcast.ancient-dungeon-item", 1);
        return services;
    }
}
