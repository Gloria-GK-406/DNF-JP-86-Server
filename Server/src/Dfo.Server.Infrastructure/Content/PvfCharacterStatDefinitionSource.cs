using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfCharacterStatDefinitionSource(PvfArchiveProvider provider)
    : ICharacterStatDefinitionSource
{
    private const string CharacterList = "character/character.lst";
    private const string InitialTag = "[initial value]";

    /// <summary>Opens the first growth block, which is where the initial block ends.</summary>
    private const string GrowthTagPrefix = "[growtype ";

    /// <summary>
    /// Archive numbers are tenths of the value the client is sent: <c>[HP MAX] 180</c> is 1800.
    /// </summary>
    private const float WireScale = 10f;

    public ValueTask<CharacterBaseStats> GetInitialAsync(byte job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var archive = provider.Archive;
        var relative = PvfListFile.Read(archive, CharacterList)
            .FirstOrDefault(value => value.Id == job).RelativePath;
        if (string.IsNullOrEmpty(relative))
            throw new InvalidOperationException($"{CharacterList} declares no job {job}.");

        var path = "character/" + relative;
        var values = ReadInitialSection(archive, path);
        return ValueTask.FromResult(new CharacterBaseStats(
            HpMax: Stat(values, "[HP MAX]"),
            MpMax: Stat(values, "[MP MAX]"),
            PhysicalAttack: Stat(values, "[physical attack]"),
            PhysicalDefense: Stat(values, "[physical defense]"),
            MagicalAttack: Stat(values, "[magical attack]"),
            MagicalDefense: Stat(values, "[magical defense]"),
            FireResistance: Stat(values, "[fire resistance]"),
            WaterResistance: Stat(values, "[water resistance]"),
            DarkResistance: Stat(values, "[dark resistance]"),
            LightResistance: Stat(values, "[light resistance]"),
            InventoryLimit: Stat(values, "[inventory limit]"),
            HpRegenSpeed: Stat(values, "[HP regen speed]"),
            MpRegenSpeed: Stat(values, "[MP regen speed]"),
            MoveSpeed: Stat(values, "[move speed]"),
            AttackSpeed: Stat(values, "[attack speed]"),
            CastSpeed: Stat(values, "[cast speed]"),
            HitRecovery: Stat(values, "[hit recovery]"),
            JumpPower: Stat(values, "[jump power]"),
            Weight: Stat(values, "[weight]")));
    }

    /// <summary>
    /// Collects the first value of every tag between <c>[initial value]</c> and the first growth
    /// block.
    /// </summary>
    /// <remarks>
    /// The scan has to stop at <c>[growtype 1]</c> rather than run to the end of the file. The
    /// growth blocks repeat the same stat tags with the per-level increments, so a scan that kept
    /// going would report a swordman's starting HP as the 45 he gains per level instead of the
    /// 180 he is born with.
    /// </remarks>
    private static Dictionary<string, float> ReadInitialSection(PvfArchive archive, string path)
    {
        var index = archive.FindFile(path);
        if (index < 0)
            throw new InvalidOperationException($"The archive does not contain {path}.");

        var values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var reader = archive.OpenScript(index);
        var started = false;
        string? tag = null;

        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                var name = archive.ResolveString(token.Value);
                if (!started)
                {
                    started = name.Equals(InitialTag, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (name.StartsWith(GrowthTagPrefix, StringComparison.OrdinalIgnoreCase))
                    break;

                tag = name;
                continue;
            }

            // Only the first value of a tag. Some blocks in this range carry lists -- the initial
            // [skill] grants are pairs of integers -- and none of the stats does.
            if (started && tag is not null && !values.ContainsKey(tag))
            {
                if (token.Kind == PvfTokenKind.Single)
                    values[tag] = token.AsSingle();
                else if (token.Kind == PvfTokenKind.Int32)
                    values[tag] = token.Value;
            }
        }

        if (!started)
            throw new InvalidOperationException($"{path} declares no {InitialTag} section.");

        return values;
    }

    /// <summary>
    /// Scales one archive number, or zero where the job does not declare it.
    /// </summary>
    /// <remarks>
    /// Rounded rather than truncated. These arrive as IEEE singles, so a tenth that is exact in
    /// the source text need not be exact in the token: truncating <c>0.7f * 10</c> yields 6.
    /// No stat observed so far lands on such a value, and rounding costs nothing to be sure.
    /// </remarks>
    private static int Stat(Dictionary<string, float> values, string tag) =>
        values.TryGetValue(tag, out var value)
            ? (int)MathF.Round(value * WireScale, MidpointRounding.AwayFromZero)
            : 0;
}
