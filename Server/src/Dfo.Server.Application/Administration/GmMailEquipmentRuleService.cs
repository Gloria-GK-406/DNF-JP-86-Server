using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Administration;

internal sealed class GmMailEquipmentRuleService(IEquipmentDefinitionSource equipment,
    IEquipmentOutworldContentSource outworld, IEquipmentUpgradeRuleService upgrades,
    ILogger<GmMailEquipmentRuleService> logger) : IGmMailEquipmentRuleService
{
    public async ValueTask<GmMailEquipmentCapabilities> CapabilitiesAsync(ItemSnapshot core, CancellationToken token)
    {
        try { return await ReadCapabilitiesAsync(core, token); }
        catch (InvalidDataException error)
        {
            logger.LogWarning(error, "GM equipment growth configuration unavailable for item {ItemId}.", core.ItemId);
            return new(0, 0, false, "装备成长规则读取失败，暂不可配置强化。", "装备成长规则读取失败，暂不可配置增幅。", new Dictionary<byte, ushort>());
        }
    }

    private async ValueTask<GmMailEquipmentCapabilities> ReadCapabilitiesAsync(ItemSnapshot core, CancellationToken token)
    {
        // Admission uses an unsealed, full-durability template. GM creates the requested final state;
        // it does not execute a player's random upgrade, charge resources, or mutate an owned item.
        var ordinary = core with { SealFlag = 0, Attribute = 0, AmplifyType = 0, AmplifyValue = 0 };
        var maxNormal = await MaximumAsync(ordinary, EquipmentUpgradeKind.Reinforcement, token);
        var definition = await equipment.GetEquipmentAsync(core.ItemId, token);
        var content = await outworld.GetAsync(token);
        IReadOnlyDictionary<byte, ushort> values = new Dictionary<byte, ushort>();
        var canAmplify = false;
        var maxAmplify = 0;
        if (definition is not null && content is not null && content.InitialValues.TryGetValue(definition.Rarity, out var declared)
            && Enumerable.Range(1, 4).All(type => declared.TryGetValue((byte)type, out var value) && value > 0))
        {
            var amplified = ordinary with { AmplifyType = 3, AmplifyValue = declared[3] };
            canAmplify = (await upgrades.CheckAmplificationAsync(amplified, token)).Rejection == EquipmentAmplificationRejection.None;
            if (canAmplify)
            {
                values = declared;
                maxAmplify = await MaximumAsync(amplified, EquipmentUpgradeKind.Amplification, token);
            }
        }
        return new(maxNormal, maxAmplify, canAmplify,
            maxNormal == 0 ? "该装备类型或模板规则不支持强化。" : null,
            canAmplify ? null : "该装备的等级、稀有度或模板规则不支持增幅。", values);
    }

    public async ValueTask<ItemSnapshot> ConfigureAsync(ItemSnapshot core, GmMailEquipmentOptions options, CancellationToken token)
    {
        var configured = options.Seal == "unsealed" ? core with { SealFlag = 0 } : core;
        if (options.Growth == "none") return configured;
        var capabilities = await CapabilitiesAsync(core, token);
        if (options.Growth == "reinforcement")
        {
            if (options.Level > capabilities.MaxReinforcementLevel)
                throw Invalid($"物品 {core.ItemId} 不支持所选强化等级（最高 {capabilities.MaxReinforcementLevel}）。");
        }
        else
        {
            if (!capabilities.CanAmplify || options.Level > capabilities.MaxAmplificationLevel
                || !capabilities.AmplifyValues.TryGetValue((byte)options.AmplifyType, out var value))
                throw Invalid($"物品 {core.ItemId} 不支持所选增幅配置。");
            configured = configured with { AmplifyType = (byte)options.AmplifyType, AmplifyValue = value };
        }
        return configured with { Attribute = (byte)((core.Attribute & 0xE0) | options.Level) };
    }

    private async ValueTask<int> MaximumAsync(ItemSnapshot core, EquipmentUpgradeKind kind, CancellationToken token)
    {
        for (byte level = 0; level < 31; level++)
        {
            var quote = await upgrades.QuoteReinforcementAsync(core with { Attribute = level }, token, kind);
            if (quote.Rejection != EquipmentReinforcementRejection.None) return level;
        }
        return 31;
    }

    private static GmMailException Invalid(string message) => new(GmMailRefusal.Invalid, message);
}
