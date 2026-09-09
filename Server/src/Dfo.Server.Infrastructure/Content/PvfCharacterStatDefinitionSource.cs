using System.Collections.Frozen;
using System.Globalization;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfCharacterStatDefinitionSource(PvfArchiveProvider provider) : ICharacterStatDefinitionSource
{
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
        var reader = archive.OpenScript(index);
        Dictionary<string, double>? values = null;
        Dictionary<string, double>? initial = null;
        var growth = new Dictionary<CharacterCareer, Dictionary<string, double>>();
        var growType = -1;
        string? tag = null;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                tag = archive.ResolveString(token.Value);
                if (tag.Equals("[initial value]", StringComparison.OrdinalIgnoreCase))
                {
                    if (initial is not null) throw new InvalidOperationException($"{path}: duplicate initial block.");
                    values = initial = new(StringComparer.OrdinalIgnoreCase);
                }
                else if (TryNumber(tag, "[growtype ", out var grow))
                {
                    growType = grow - 1;
                    values = new(StringComparer.OrdinalIgnoreCase);
                    growth.Add(new CharacterCareer(job, growType), values);
                }
                else if (TryNumber(tag, "[awakening ", out var awakening))
                {
                    if (growType < 0 || awakening == 0)
                        throw new InvalidOperationException($"{path}: awakening without a valid growth block.");
                    values = new(StringComparer.OrdinalIgnoreCase);
                    growth.Add(new CharacterCareer(job, growType, awakening), values);
                }
                continue;
            }
            if (values is null || tag is null || values.ContainsKey(tag)) continue;
            if (token.Kind == PvfTokenKind.Single) values[tag] = token.AsSingle();
            else if (token.Kind == PvfTokenKind.Int32) values[tag] = token.Value;
        }
        if (initial is null || !growth.ContainsKey(new CharacterCareer(job)))
            throw new InvalidOperationException($"{path} requires initial and unadvanced growth declarations.");
        return new(job, Vector(initial), growth.ToFrozenDictionary(pair => pair.Key, pair => Vector(pair.Value)));
    }

    private static bool TryNumber(string tag, string prefix, out int value)
    {
        value = 0;
        return tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && tag.EndsWith(']')
            && int.TryParse(tag.AsSpan(prefix.Length, tag.Length - prefix.Length - 1),
                NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static CharacterStatValues Vector(Dictionary<string, double> values) => new(
        HpMax: values.GetValueOrDefault("[HP MAX]"),
        MpMax: values.GetValueOrDefault("[MP MAX]"),
        PhysicalAttack: values.GetValueOrDefault("[physical attack]"),
        PhysicalDefense: values.GetValueOrDefault("[physical defense]"),
        MagicalAttack: values.GetValueOrDefault("[magical attack]"),
        MagicalDefense: values.GetValueOrDefault("[magical defense]"),
        FireResistance: values.GetValueOrDefault("[fire resistance]"),
        WaterResistance: values.GetValueOrDefault("[water resistance]"),
        DarkResistance: values.GetValueOrDefault("[dark resistance]"),
        LightResistance: values.GetValueOrDefault("[light resistance]"),
        InventoryLimit: values.GetValueOrDefault("[inventory limit]"),
        HpRegenSpeed: values.GetValueOrDefault("[HP regen speed]"),
        MpRegenSpeed: values.GetValueOrDefault("[MP regen speed]"),
        MoveSpeed: values.GetValueOrDefault("[move speed]"),
        AttackSpeed: values.GetValueOrDefault("[attack speed]"),
        CastSpeed: values.GetValueOrDefault("[cast speed]"),
        HitRecovery: values.GetValueOrDefault("[hit recovery]"),
        JumpPower: values.GetValueOrDefault("[jump power]"),
        Weight: values.GetValueOrDefault("[weight]"));
}
