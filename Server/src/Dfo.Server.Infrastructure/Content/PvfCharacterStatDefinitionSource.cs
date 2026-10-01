using Dfo.Pvf.Schema;
using System.Collections.Frozen;
using System.Globalization;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfCharacterStatDefinitionSource(PvfArchiveProvider provider) : ICharacterStatDefinitionSource
{
    private static readonly PvfFieldFamily GrowTypeFields = new("[growtype ");
    private static readonly PvfFieldFamily AwakeningFields = new("[awakening ");

    private static class Fields
    {
        internal static readonly PvfField<bool> InitialValueField = PvfField.Marker("[initial value]", optional: true);
        internal static readonly PvfField<double> HPMAXField = PvfField.Number("[HP MAX]", optional: true);
        internal static readonly PvfField<double> MPMAXField = PvfField.Number("[MP MAX]", optional: true);
        internal static readonly PvfField<double> PhysicalAttackField = PvfField.Number("[physical attack]", optional: true);
        internal static readonly PvfField<double> PhysicalDefenseField = PvfField.Number("[physical defense]", optional: true);
        internal static readonly PvfField<double> MagicalAttackField = PvfField.Number("[magical attack]", optional: true);
        internal static readonly PvfField<double> MagicalDefenseField = PvfField.Number("[magical defense]", optional: true);
        internal static readonly PvfField<double> FireResistanceField = PvfField.Number("[fire resistance]", optional: true);
        internal static readonly PvfField<double> WaterResistanceField = PvfField.Number("[water resistance]", optional: true);
        internal static readonly PvfField<double> DarkResistanceField = PvfField.Number("[dark resistance]", optional: true);
        internal static readonly PvfField<double> LightResistanceField = PvfField.Number("[light resistance]", optional: true);
        internal static readonly PvfField<double> InventoryLimitField = PvfField.Number("[inventory limit]", optional: true);
        internal static readonly PvfField<double> HPRegenSpeedField = PvfField.Number("[HP regen speed]", optional: true);
        internal static readonly PvfField<double> MPRegenSpeedField = PvfField.Number("[MP regen speed]", optional: true);
        internal static readonly PvfField<double> MoveSpeedField = PvfField.Number("[move speed]", optional: true);
        internal static readonly PvfField<double> AttackSpeedField = PvfField.Number("[attack speed]", optional: true);
        internal static readonly PvfField<double> CastSpeedField = PvfField.Number("[cast speed]", optional: true);
        internal static readonly PvfField<double> HitRecoveryField = PvfField.Number("[hit recovery]", optional: true);
        internal static readonly PvfField<double> JumpPowerField = PvfField.Number("[jump power]", optional: true);
        internal static readonly PvfField<double> WeightField = PvfField.Number("[weight]", optional: true);
    }

    private static readonly PvfField<double>[] StatFields =
    [
        Fields.HPMAXField, Fields.MPMAXField, Fields.PhysicalAttackField, Fields.PhysicalDefenseField,
        Fields.MagicalAttackField, Fields.MagicalDefenseField, Fields.FireResistanceField,
        Fields.WaterResistanceField, Fields.DarkResistanceField, Fields.LightResistanceField,
        Fields.InventoryLimitField, Fields.HPRegenSpeedField, Fields.MPRegenSpeedField,
        Fields.MoveSpeedField, Fields.AttackSpeedField, Fields.CastSpeedField, Fields.HitRecoveryField,
        Fields.JumpPowerField, Fields.WeightField,
    ];

    private const string CharacterList = "character/character.lst";
    private readonly object _gate = new();
    private IReadOnlyList<CharacterStatDefinition>? _definitions;

    public ValueTask<IReadOnlyList<CharacterStatDefinition>> GetAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_definitions is not null) return ValueTask.FromResult(_definitions);
            var archive = provider.Archive;
            var definitions = new List<CharacterStatDefinition>();
            foreach (var (id, relative) in PvfListFile.Read(archive, CharacterList))
            {
                cancellationToken.ThrowIfCancellationRequested();
                definitions.Add(Read(archive, checked((byte)id), "character/" + relative));
            }
            _definitions = definitions.AsReadOnly();
            return ValueTask.FromResult(_definitions);
        }
    }

    public async ValueTask<CharacterBaseStats> GetInitialAsync(byte job, CancellationToken cancellationToken)
    {
        var definitions = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        return (definitions.FirstOrDefault(value => value.Job == job)
            ?? throw new InvalidOperationException($"The archive declares no job {job}.")).Initial.ToFixedPoint();
    }

    private static CharacterStatDefinition Read(PvfArchive archive, byte job, string path)
    {
        var index = archive.FindFile(path);
        if (index < 0) throw new InvalidOperationException($"The archive does not contain {path}.");
        Dictionary<string, double>? values = null;
        Dictionary<string, double>? initial = null;
        var growth = new Dictionary<CharacterCareer, Dictionary<string, double>>();
        var growType = -1;
        foreach (var section in PvfDocument.Open(archive, path).Sections)
        {
            var tag = section.Name;
            if (tag.Equals(Fields.InitialValueField.Name, StringComparison.OrdinalIgnoreCase))
            {
                Fields.InitialValueField.Decode(archive, section.Values, path);
                if (initial is not null) throw new InvalidOperationException($"{path}: duplicate initial block.");
                values = initial = new(StringComparer.OrdinalIgnoreCase);
            }
            else if (GrowTypeFields.TryGetIndex(tag, out var grow))
            {
                growType = grow - 1;
                values = new(StringComparer.OrdinalIgnoreCase);
                growth.Add(new CharacterCareer(job, growType), values);
            }
            else if (AwakeningFields.TryGetIndex(tag, out var awakening))
            {
                if (growType < 0 || awakening == 0)
                    throw new InvalidOperationException($"{path}: awakening without a valid growth block.");
                values = new(StringComparer.OrdinalIgnoreCase);
                growth.Add(new CharacterCareer(job, growType, awakening), values);
            }
            else if (StatFields.FirstOrDefault(field => tag.Equals(field.Name, StringComparison.OrdinalIgnoreCase)) is { } field)
            {
                var value = field.Decode(archive, section.Values, path);
                if (values is null) continue;
                if (!values.TryAdd(field.Name, value))
                    throw new InvalidDataException($"PVF '{path}', field '{field.Name}': duplicate field in character stat block.");
            }
            else if (values is not null && !values.ContainsKey(tag))
            {
                // Unknown headings retain their previous first-numeric projection.
                foreach (var token in section.Values)
                {
                    if (token.Kind == PvfTokenKind.Single) { values[tag] = token.AsSingle(); break; }
                    if (token.Kind == PvfTokenKind.Int32) { values[tag] = token.Value; break; }
                }
            }
        }
        if (initial is null || !growth.ContainsKey(new CharacterCareer(job)))
            throw new InvalidOperationException($"{path} requires initial and unadvanced growth declarations.");
        return new(job, Vector(initial), growth.ToFrozenDictionary(pair => pair.Key, pair => Vector(pair.Value)));
    }



    private static CharacterStatValues Vector(Dictionary<string, double> values) => new(
        HpMax: values.GetValueOrDefault(Fields.HPMAXField.Name),
        MpMax: values.GetValueOrDefault(Fields.MPMAXField.Name),
        PhysicalAttack: values.GetValueOrDefault(Fields.PhysicalAttackField.Name),
        PhysicalDefense: values.GetValueOrDefault(Fields.PhysicalDefenseField.Name),
        MagicalAttack: values.GetValueOrDefault(Fields.MagicalAttackField.Name),
        MagicalDefense: values.GetValueOrDefault(Fields.MagicalDefenseField.Name),
        FireResistance: values.GetValueOrDefault(Fields.FireResistanceField.Name),
        WaterResistance: values.GetValueOrDefault(Fields.WaterResistanceField.Name),
        DarkResistance: values.GetValueOrDefault(Fields.DarkResistanceField.Name),
        LightResistance: values.GetValueOrDefault(Fields.LightResistanceField.Name),
        InventoryLimit: values.GetValueOrDefault(Fields.InventoryLimitField.Name),
        HpRegenSpeed: values.GetValueOrDefault(Fields.HPRegenSpeedField.Name),
        MpRegenSpeed: values.GetValueOrDefault(Fields.MPRegenSpeedField.Name),
        MoveSpeed: values.GetValueOrDefault(Fields.MoveSpeedField.Name),
        AttackSpeed: values.GetValueOrDefault(Fields.AttackSpeedField.Name),
        CastSpeed: values.GetValueOrDefault(Fields.CastSpeedField.Name),
        HitRecovery: values.GetValueOrDefault(Fields.HitRecoveryField.Name),
        JumpPower: values.GetValueOrDefault(Fields.JumpPowerField.Name),
        Weight: values.GetValueOrDefault(Fields.WeightField.Name));
}
