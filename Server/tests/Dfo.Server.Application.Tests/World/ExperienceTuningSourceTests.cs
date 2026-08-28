using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// What an operator may change about a kill's worth, and what happens when they get it wrong.
/// </summary>
/// <remarks>
/// The behaviour worth protecting is not that configuration is read -- it is that a deployment
/// without any plays exactly as before, and that a deployment with a broken one stops instead of
/// quietly paying the wrong amount. Both failures are invisible in play: awards merely come out a
/// different size, and nothing about a smaller award looks like an error.
/// </remarks>
public sealed class ExperienceTuningSourceTests
{
    /// <summary>No configuration at all is what this server was measured at.</summary>
    [Fact]
    public void AnAbsentSectionLeavesEveryDialWhereItWas()
    {
        var tuning = ExperienceTuningSource.From(Configuration([]));

        Assert.Equal(ExperienceTuning.Default, tuning);
    }

    /// <summary>A section that sets one dial leaves the rest alone.</summary>
    /// <remarks>
    /// The trap this guards is a reader that treats a present section as a complete one: an
    /// operator who changes the growth rate would silently lose the level-gap bands, and the first
    /// symptom would be a level difference that throws mid-dungeon.
    /// </remarks>
    [Fact]
    public void SettingOneDialDoesNotDisturbTheOthers()
    {
        var tuning = ExperienceTuningSource.From(Configuration(new()
        {
            ["Experience:Curve:GrowthPercentPerLevel"] = "5",
        }));

        Assert.Equal(5, tuning.Curve.GrowthPercentPerLevel);
        Assert.Equal(10, tuning.Curve.PerLevel);
        Assert.Equal(2.0, tuning.WorthDivisor);
        Assert.Equal(ExperienceTuning.DefaultLevelGapBands.Count, tuning.LevelGapBands.Count);
    }

    /// <summary>The curve an operator writes is the curve a kill is priced with.</summary>
    [Fact]
    public void TheConfiguredCurveIsWhatPricesAKill()
    {
        var tuning = ExperienceTuningSource.From(Configuration(new()
        {
            ["Experience:Curve:PerLevel"] = "20",
        }));

        Assert.Equal(
            MonsterExperienceWorth.For(5, ExperienceTuning.Default.Curve) * 2,
            MonsterExperienceWorth.For(5, tuning.Curve));
    }

    /// <summary>A dial that is not a number stops the server and names the key.</summary>
    /// <remarks>
    /// Loudly, unlike the bonus rates beside it. Those are an operator's choice and a mistyped one
    /// is skipped; this one has a right answer, and defaulting past it would leave every award on
    /// the server wrong with nothing to read about it.
    /// </remarks>
    [Fact]
    public void ADialThatIsNotANumberIsRefusedByName()
    {
        var thrown = Assert.Throws<InvalidDataException>(() =>
            ExperienceTuningSource.From(Configuration(new()
            {
                ["Experience:WorthDivisor"] = "half",
            })));

        Assert.Contains("WorthDivisor", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>A divisor of zero is refused rather than dividing by it.</summary>
    [Fact]
    public void ADivisorOfZeroIsRefused() =>
        Assert.Throws<InvalidDataException>(() =>
            ExperienceTuningSource.From(Configuration(new()
            {
                ["Experience:WorthDivisor"] = "0",
            })));

    /// <summary>Bands that leave a gap are refused, naming where the run breaks.</summary>
    /// <remarks>
    /// This is the failure the validation exists for: a set that answers every difference its
    /// author tried and none of the one a player reaches later. Refusing at startup turns a
    /// mid-dungeon exception into a line in the log before anybody logs in.
    /// </remarks>
    [Fact]
    public void BandsThatDoNotCoverEveryDifferenceAreRefused()
    {
        var thrown = Assert.Throws<InvalidDataException>(() =>
            ExperienceTuningSource.From(Configuration(new()
            {
                ["Experience:LevelGapBands:0:From"] = "-2147483648",
                ["Experience:LevelGapBands:0:To"] = "0",
                ["Experience:LevelGapBands:0:Rate"] = "1.0",
                // Starts at 2, so a difference of exactly 1 belongs to no band.
                ["Experience:LevelGapBands:1:From"] = "2",
                ["Experience:LevelGapBands:1:To"] = "2147483647",
                ["Experience:LevelGapBands:1:Rate"] = "1.0",
            })));

        Assert.Contains("gaps or overlaps", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>Bands that stop short of the top are refused.</summary>
    [Fact]
    public void BandsThatStopShortAreRefused()
    {
        var thrown = Assert.Throws<InvalidDataException>(() =>
            ExperienceTuningSource.From(Configuration(new()
            {
                ["Experience:LevelGapBands:0:From"] = "-2147483648",
                ["Experience:LevelGapBands:0:To"] = "99",
                ["Experience:LevelGapBands:0:Rate"] = "1.0",
            })));

        Assert.Contains("must reach", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>A complete set of bands is used as written.</summary>
    [Fact]
    public void ACompleteSetOfBandsIsUsedAsWritten()
    {
        var tuning = ExperienceTuningSource.From(Configuration(new()
        {
            ["Experience:LevelGapBands:0:From"] = "-2147483648",
            ["Experience:LevelGapBands:0:To"] = "0",
            ["Experience:LevelGapBands:0:Rate"] = "0.25",
            ["Experience:LevelGapBands:1:From"] = "1",
            ["Experience:LevelGapBands:1:To"] = "2147483647",
            ["Experience:LevelGapBands:1:Rate"] = "2.5",
        }));

        Assert.Equal(0.25, tuning.LevelGapRate(characterLevel: 10, monsterLevel: 10));
        Assert.Equal(2.5, tuning.LevelGapRate(characterLevel: 10, monsterLevel: 11));
    }

    /// <summary>The file this server ships documents the numbers the code defaults to.</summary>
    /// <remarks>
    /// The two are written in different places by different hands, and a file that drifted from
    /// the code would hand an operator a document describing a server they are not running -- the
    /// worst kind of documentation, because it is specific and wrong. Read directly rather than
    /// through the configuration stack, so this needs no JSON provider in a test project that
    /// otherwise has no use for one.
    /// </remarks>
    [SkippableFact]
    public void TheShippedFileDocumentsTheDefaultsInCode()
    {
        var path = System.IO.Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "Dfo.Server.Host", "experience.json");
        Skip.IfNot(File.Exists(path), "The host's experience.json is not beside this test run.");

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var experience = document.RootElement.GetProperty("Experience");
        var curve = experience.GetProperty("Curve");
        var expected = ExperienceTuning.Default;

        Assert.Equal(expected.Curve.PerLevel, curve.GetProperty("PerLevel").GetInt32());
        Assert.Equal(expected.Curve.LevelHeadStart, curve.GetProperty("LevelHeadStart").GetInt32());
        Assert.Equal(
            expected.Curve.GrowthBeginsAtLevel, curve.GetProperty("GrowthBeginsAtLevel").GetInt32());
        Assert.Equal(
            expected.Curve.GrowthPercentPerLevel,
            curve.GetProperty("GrowthPercentPerLevel").GetInt32());
        Assert.Equal(expected.WorthDivisor, experience.GetProperty("WorthDivisor").GetDouble());
        Assert.Equal(
            expected.RankRates,
            experience.GetProperty("RankRates").EnumerateArray()
                .Select(rate => rate.GetDouble()).ToArray());
        Assert.Equal(
            expected.LevelGapBands.Count,
            experience.GetProperty("LevelGapBands").GetArrayLength());

        // The channel's reward is declared in the archive, not here. Asserted as an absence
        // because the key was here once: an operator who still had the old file would be reading
        // a dial that the server no longer consults.
        Assert.False(experience.TryGetProperty("ChannelRate", out _));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
