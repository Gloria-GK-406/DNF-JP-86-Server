namespace Dfo.Server.Data.Characters;

/// <summary>Archive precision before the final fixed-point projection; no per-level rounding.</summary>
public sealed record CharacterStatValues(
    double HpMax = 0,
    double MpMax = 0,
    double PhysicalAttack = 0,
    double PhysicalDefense = 0,
    double MagicalAttack = 0,
    double MagicalDefense = 0,
    double FireResistance = 0,
    double WaterResistance = 0,
    double DarkResistance = 0,
    double LightResistance = 0,
    double InventoryLimit = 0,
    double HpRegenSpeed = 0,
    double MpRegenSpeed = 0,
    double MoveSpeed = 0,
    double AttackSpeed = 0,
    double CastSpeed = 0,
    double HitRecovery = 0,
    double JumpPower = 0,
    double Weight = 0)
{
    public static CharacterStatValues operator +(CharacterStatValues a, CharacterStatValues b) =>
        new(a.HpMax + b.HpMax,
            a.MpMax + b.MpMax,
            a.PhysicalAttack + b.PhysicalAttack,
            a.PhysicalDefense + b.PhysicalDefense,
            a.MagicalAttack + b.MagicalAttack,
            a.MagicalDefense + b.MagicalDefense,
            a.FireResistance + b.FireResistance,
            a.WaterResistance + b.WaterResistance,
            a.DarkResistance + b.DarkResistance,
            a.LightResistance + b.LightResistance,
            a.InventoryLimit + b.InventoryLimit,
            a.HpRegenSpeed + b.HpRegenSpeed,
            a.MpRegenSpeed + b.MpRegenSpeed,
            a.MoveSpeed + b.MoveSpeed,
            a.AttackSpeed + b.AttackSpeed,
            a.CastSpeed + b.CastSpeed,
            a.HitRecovery + b.HitRecovery,
            a.JumpPower + b.JumpPower,
            a.Weight + b.Weight);

    public CharacterBaseStats ToFixedPoint() =>
        new(Fixed(HpMax),
            Fixed(MpMax),
            Fixed(PhysicalAttack),
            Fixed(PhysicalDefense),
            Fixed(MagicalAttack),
            Fixed(MagicalDefense),
            Fixed(FireResistance),
            Fixed(WaterResistance),
            Fixed(DarkResistance),
            Fixed(LightResistance),
            Fixed(InventoryLimit),
            Fixed(HpRegenSpeed),
            Fixed(MpRegenSpeed),
            Fixed(MoveSpeed),
            Fixed(AttackSpeed),
            Fixed(CastSpeed),
            Fixed(HitRecovery),
            Fixed(JumpPower),
            Fixed(Weight));

    private static int Fixed(double value) =>
        checked((int)Math.Round(value * 10, MidpointRounding.AwayFromZero));
}

