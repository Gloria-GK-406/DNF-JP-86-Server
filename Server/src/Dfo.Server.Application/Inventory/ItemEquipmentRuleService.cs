using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemEquipmentRuleService(IEquipmentDefinitionSource equipment,
    IEquipmentOutworldContentSource outworld, IEquipmentUpgradeRuleService upgrades,
    IEquipmentForgingContentSource forging, IItemSocketDefinitionSource sockets,
    ILogger<ItemEquipmentRuleService> logger) : IItemEquipmentRuleService
{
    public async ValueTask<ItemEquipmentCapabilities> CapabilitiesAsync(ItemSnapshot core, CancellationToken token)
    {
        ItemEquipmentCapabilities growth;
        try { growth = await ReadCapabilitiesAsync(core, token); }
        catch (InvalidDataException error)
        {
            logger.LogWarning(error, "Equipment growth configuration unavailable for item {ItemId}.", core.ItemId);
            growth = new(0, 0, false, "装备成长规则读取失败，暂不可配置强化。", "装备成长规则读取失败，暂不可配置增幅。", new Dictionary<byte, ushort>());
        }
        // Forging and sockets are independent of growth: one unreadable table must not hide the other options.
        try { growth = growth with { MaxForgeLevel = await MaxForgeAsync(core.ItemId, token) }; }
        catch (InvalidDataException error) { logger.LogWarning(error, "Equipment forging configuration unavailable for item {ItemId}.", core.ItemId); }
        try
        {
            var types = await SocketTypesAsync(core.ItemId, token);
            growth = growth with { SocketCount = types.Length, SocketClass = SocketClass(types) };
        }
        catch (InvalidDataException error) { logger.LogWarning(error, "Equipment socket layout unavailable for item {ItemId}.", core.ItemId); }
        return growth;
    }

    private async ValueTask<ItemEquipmentCapabilities> ReadCapabilitiesAsync(ItemSnapshot core, CancellationToken token)
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

    public async ValueTask<ItemSnapshot> ConfigureAsync(ItemSnapshot core, ItemEquipmentOptions options, CancellationToken token)
    {
        var configured = options.Seal == "unsealed" ? core with { SealFlag = 0 } : core;
        if (options.Forge != 0)
        {
            int max;
            try { max = await MaxForgeAsync(core.ItemId, token); }
            catch (InvalidDataException) { throw Invalid($"物品 {core.ItemId} 的锻造规则读取失败，暂不可配置锻造。"); }
            if (max == 0) throw Invalid($"物品 {core.ItemId} 不是武器或锻造规则不可用，不能配置锻造。");
            if (options.Forge < 0 || options.Forge > max) throw Invalid($"物品 {core.ItemId} 不支持所选锻造等级（最高 {max}）。");
            configured = configured with { GenuineUpgrade = (byte)options.Forge };
        }
        if (options.Socket)
        {
            ImmutableArray<ushort> types;
            try { types = await SocketTypesAsync(core.ItemId, token); }
            catch (InvalidDataException) { throw Invalid($"物品 {core.ItemId} 的徽章孔规则读取失败，暂不可开孔。"); }
            if (types.IsEmpty) throw Invalid($"物品 {core.ItemId} 没有可开启的徽章孔。");
            // Same state as EquipmentInstance.OpenSocketsAsync: opened, empty sockets.
            configured = configured with { EmblemSocketCount = (byte)types.Length, EmblemId1 = -1, EmblemId2 = types.Length == 2 ? -1 : 0 };
        }
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

    private async ValueTask<int> MaxForgeAsync(int itemId, CancellationToken token)
    {
        if ((await equipment.GetEquipmentAsync(itemId, token))?.EquipmentType != "weapon") return 0;
        return (await forging.GetAsync(token))?.MaxLevel is { } max and > 0 and <= byte.MaxValue ? max : 0;
    }

    // Ordinary (non-avatar) equipment only; weapons and titles declare no layout.
    private async ValueTask<ImmutableArray<ushort>> SocketTypesAsync(int itemId, CancellationToken token)
    {
        var definition = await sockets.TargetAsync(itemId, token);
        return definition is { Avatar: false } && !definition.OpenTypes.IsDefaultOrEmpty && definition.OpenTypes.Length <= 2 ? definition.OpenTypes : [];
    }

    internal static string? SocketClass(ImmutableArray<ushort> types)
    {
        if (types.IsDefaultOrEmpty) return null;
        var letters = types.Select(type => type switch { 1 => "A", 2 => "B", 4 => "C", 8 => "D", 16 => "S", 0xFFEF => "M", _ => "?" }).Distinct().ToArray();
        return string.Join('/', letters);
    }

    private static ArgumentException Invalid(string message) => new(message);
}
