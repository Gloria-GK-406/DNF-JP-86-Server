using Dfo.Server.Data.Characters;
namespace Dfo.Server.Application.Characters;

internal static class CharacterBaseStatProjection
{
    public static CharacterDerivedStatsStorageRecord Apply(CharacterDerivedStatsStorageRecord original, CharacterBaseStats stats) => original with
    {
        HpMaximum = stats.HpMax,
        MpMaximum = stats.MpMax,
        PhysicalAttack = stats.PhysicalAttack,
        PhysicalDefense = stats.PhysicalDefense,
        MagicalAttack = stats.MagicalAttack,
        MagicalDefense = stats.MagicalDefense,
        FireResistance = stats.FireResistance,
        WaterResistance = stats.WaterResistance,
        DarkResistance = stats.DarkResistance,
        LightResistance = stats.LightResistance,
        InventoryLimit = stats.InventoryLimit,
        HpRegenSpeed = stats.HpRegenSpeed,
        MpRegenSpeed = stats.MpRegenSpeed,
        MoveSpeed = stats.MoveSpeed,
        AttackSpeed = stats.AttackSpeed,
        CastSpeed = stats.CastSpeed,
        HitRecovery = stats.HitRecovery,
        JumpPower = stats.JumpPower,
        Weight = stats.Weight,
    };
}
