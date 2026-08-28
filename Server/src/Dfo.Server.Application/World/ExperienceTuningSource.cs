using Dfo.Server.Abstractions.World;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Application.World;

/// <summary>
/// Reads the dials this server decides for itself out of configuration.
/// </summary>
/// <remarks>
/// Only what the archive does not declare. The difficulty and party rates, the monster-wide rate,
/// each dungeon's weight and the level thresholds are content and are read from the archive; a
/// server that let an operator override those would be answering with something the client's own
/// files contradict.
/// <para>
/// <b>An absent section changes nothing.</b> Every value defaults to what was measured or
/// recovered, so a deployment with no configuration plays exactly as one built before this existed.
/// That matters more than it looks: these numbers carry evidence, and a default of zero or one
/// would quietly replace evidence with a placeholder.
/// </para>
/// <para>
/// <b>Failing loudly is the point here.</b> Unlike the bonus rates beside it, these have a right
/// answer, and getting one wrong shifts every award on the server at once without producing
/// anything that looks like an error -- awards merely come out smaller. So a malformed value stops
/// the server and names the key, rather than being skipped.
/// </para>
/// </remarks>
internal static class ExperienceTuningSource
{
    public static ExperienceTuning From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("Experience");
        if (!section.Exists())
            return ExperienceTuning.Default;

        var tuning = new ExperienceTuning(
            ReadCurve(section.GetSection("Curve")),
            Read(section, "WorthDivisor", ExperienceTuning.Default.WorthDivisor),
            ReadRankRates(section.GetSection("RankRates")),
            ReadBands(section.GetSection("LevelGapBands")));

        tuning.Validate();
        return tuning;
    }

    private static MonsterWorthCurve ReadCurve(IConfigurationSection section)
    {
        var fallback = MonsterWorthCurve.Recovered;
        if (!section.Exists())
            return fallback;

        return new MonsterWorthCurve(
            Read(section, "PerLevel", fallback.PerLevel),
            Read(section, "LevelHeadStart", fallback.LevelHeadStart),
            Read(section, "GrowthBeginsAtLevel", fallback.GrowthBeginsAtLevel),
            Read(section, "GrowthPercentPerLevel", fallback.GrowthPercentPerLevel));
    }

    private static IReadOnlyList<double> ReadRankRates(IConfigurationSection section)
    {
        if (!section.Exists())
            return ExperienceTuning.Default.RankRates;

        var rates = new List<double>();
        foreach (var entry in section.GetChildren())
            rates.Add(Read(entry, value: entry.Value, name: entry.Path));
        return rates;
    }

    /// <remarks>
    /// A declared but empty list is refused rather than defaulted. Writing the key at all is a
    /// statement of intent, and answering it with the built-in bands would leave an operator
    /// looking at a file that says one thing while the server does another.
    /// </remarks>
    private static IReadOnlyList<LevelGapBand> ReadBands(IConfigurationSection section)
    {
        if (!section.Exists())
            return ExperienceTuning.Default.LevelGapBands;

        var bands = new List<LevelGapBand>();
        foreach (var entry in section.GetChildren())
        {
            bands.Add(new LevelGapBand(
                Read(entry, "From", int.MinValue, required: true),
                Read(entry, "To", int.MaxValue, required: true),
                Read(entry, "Rate", 0d, required: true)));
        }

        return bands;
    }

    private static int Read(IConfiguration section, string key, int fallback, bool required = false)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return required
                ? throw new InvalidDataException($"'{Path(section, key)}' is required and was not set.")
                : fallback;
        }

        return int.TryParse(raw, out var value)
            ? value
            : throw new InvalidDataException($"'{Path(section, key)}' is not a whole number: '{raw}'.");
    }

    private static double Read(
        IConfiguration section, string key, double fallback, bool required = false)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return required
                ? throw new InvalidDataException($"'{Path(section, key)}' is required and was not set.")
                : fallback;
        }

        return Read(section, raw, Path(section, key));
    }

    private static double Read(IConfiguration section, string? value, string name)
    {
        _ = section;
        return double.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidDataException($"'{name}' is not a number: '{value}'.");
    }

    private static string Path(IConfiguration section, string key) =>
        section is IConfigurationSection named ? $"{named.Path}:{key}" : key;
}
