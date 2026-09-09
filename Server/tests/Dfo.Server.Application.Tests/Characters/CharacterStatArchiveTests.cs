using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Characters;
using Dfo.Server.Infrastructure.Content;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class CharacterStatArchiveTests(ITestOutputHelper output)
{
    private static readonly (string Property, string Tag)[] StatTags =
    [
        ("HpMax", "HP MAX"),
        ("MpMax", "MP MAX"),
        ("PhysicalAttack", "physical attack"),
        ("PhysicalDefense", "physical defense"),
        ("MagicalAttack", "magical attack"),
        ("MagicalDefense", "magical defense"),
        ("FireResistance", "fire resistance"),
        ("WaterResistance", "water resistance"),
        ("DarkResistance", "dark resistance"),
        ("LightResistance", "light resistance"),
        ("InventoryLimit", "inventory limit"),
        ("HpRegenSpeed", "HP regen speed"),
        ("MpRegenSpeed", "MP regen speed"),
        ("MoveSpeed", "move speed"),
        ("AttackSpeed", "attack speed"),
        ("CastSpeed", "cast speed"),
        ("HitRecovery", "hit recovery"),
        ("JumpPower", "jump power"),
        ("Weight", "weight"),
    ];

    [SkippableFact]
    public async Task Every_declared_career_and_level_matches_independent_archive_arithmetic()
    {
        var options = new PvfContentOptions();
        Skip.If(options.ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var provider = new PvfArchiveProvider(options);
        var rules = new CharacterStatRuleService(new PvfCharacterStatDefinitionSource(provider));
        var watch = Stopwatch.StartNew();
        await rules.InitializeAsync();
        var initializationMs = watch.Elapsed.TotalMilliseconds;
        var expected = new Dictionary<CharacterCareer, (double[] Initial, double[] Basic, double[] Advanced, double[] Final)>();
        var declaredJobs = new List<int>();
        // Independent token-to-text projection, regex block extraction, and a closed-form oracle.
        // This never asks the production definition source for its parsed growth vectors.
        foreach (var (job, relative) in PvfListFile.Read(provider.Archive, "character/character.lst"))
        {
            declaredJobs.Add(job);
            var text = ScriptText(provider.Archive, "character/" + relative);
            var blocks = Regex.Matches(text, @"\[(initial value|growtype (\d+)|awakening (\d+))\]", RegexOptions.IgnoreCase);
            double[]? initial = null;
            var growth = new Dictionary<CharacterCareer, double[]>();
            var currentGrow = 0;
            for (var i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                var start = block.Index + block.Length;
                var end = i + 1 == blocks.Count ? text.Length : blocks[i + 1].Index;
                var values = StatTags.Select(pair =>
                {
                    var match = Regex.Match(text[start..end], @"\[" + Regex.Escape(pair.Tag) + @"\]\s*([-+\d.Ee]+)", RegexOptions.IgnoreCase);
                    return match.Success ? double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                }).ToArray();
                if (block.Groups[1].Value.Equals("initial value", StringComparison.OrdinalIgnoreCase))
                    initial = values;
                else if (block.Groups[2].Success)
                {
                    currentGrow = int.Parse(block.Groups[2].Value, CultureInfo.InvariantCulture) - 1;
                    growth.Add(new(job, currentGrow), values);
                }
                else growth.Add(new(job, currentGrow, int.Parse(block.Groups[3].Value, CultureInfo.InvariantCulture)), values);
            }
            foreach (var (career, final) in growth)
                expected.Add(career, (initial!, growth[new(job)], growth[new(job, career.GrowType)], final));
        }
        Assert.Equal(expected.Keys.OrderBy(c => c.Job).ThenBy(c => c.GrowType).ThenBy(c => c.Awakening), rules.SupportedCareers);
        foreach (var (career, vectors) in expected)
        {
            for (var level = 1; level <= 86; level++)
            {
                var stats = rules.GetStats(career, level);
                for (var i = 0; i < StatTags.Length; i++)
                {
                    var number = vectors.Initial[i]
                        + Math.Min(level - 1, 14) * vectors.Basic[i]
                        + Math.Clamp(level - 15, 0, 35) * vectors.Advanced[i]
                        + Math.Max(level - 50, 0) * vectors.Final[i];
                    var expectedValue = checked((int)Math.Round(number * 10, MidpointRounding.AwayFromZero));
                    Assert.True(expectedValue == (int)typeof(CharacterBaseStats).GetProperty(StatTags[i].Property)!.GetValue(stats)!,
                        $"{career}, level {level}, {StatTags[i].Property}: expected {expectedValue}, actual {stats}");
                }
                Assert.Same(stats, rules.GetStats(career, level));
            }
        }
        var careers = rules.SupportedCareers.ToArray();
        long checksum = 0;
        watch.Restart();
        for (var i = 0; i < 1_000_000; i++)
            checksum += rules.GetStats(careers[i % careers.Length], i % 86 + 1).HpMax;
        watch.Stop();
        output.WriteLine($"Jobs={string.Join(',', declaredJobs)}; Careers={careers.Length}; Rows={careers.Length * 86}; InitialMs={initializationMs:F3}; Query1000000Ms={watch.Elapsed.TotalMilliseconds:F3}; Checksum={checksum}");
        output.WriteLine(string.Join("; ", careers.GroupBy(c => c.Job).Select(g => $"job{g.Key}={g.Count()}")));
        Assert.Contains(careers, career => career.Job == 13);
        Assert.Throws<InvalidOperationException>(() => rules.GetStats(new(255), 1));
        Assert.Throws<InvalidOperationException>(() => rules.GetStats(new(0, 15, 15), 1));
    }

    private static string ScriptText(PvfArchive archive, string path)
    {
        var text = new StringBuilder();
        var reader = archive.OpenScript(path);
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier) text.AppendLine(archive.ResolveString(token.Value));
            else if (token.Kind == PvfTokenKind.Single) text.AppendLine(((double)token.AsSingle()).ToString("R", CultureInfo.InvariantCulture));
            else if (token.Kind == PvfTokenKind.Int32) text.AppendLine(token.Value.ToString(CultureInfo.InvariantCulture));
            else text.AppendLine("string");
        }
        return text.ToString();
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(15, 102)]
    [InlineData(16, 122)]
    [InlineData(50, 802)]
    [InlineData(51, 1102)]
    [InlineData(85, 11302)]
    [InlineData(86, 11602)]
    public async Task Distinct_awakening_and_fractional_growth_keep_precision_to_the_final_projection(int level, int hp)
    {
        var source = new SyntheticSource();
        var rules = new CharacterStatRuleService(source);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => rules.InitializeAsync().AsTask()));
        Assert.Equal(hp, rules.GetStats(new(0, 1, 2), level).HpMax);
        Assert.Equal(902, rules.GetStats(new(0, 1, 1), 51).HpMax);
        Assert.Equal(822, rules.GetStats(new(0, 1), 51).HpMax);
        Assert.Equal(1, source.Reads);
        Assert.Throws<ArgumentOutOfRangeException>(() => rules.GetStats(default, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rules.GetStats(default, 87));
        Assert.Throws<InvalidOperationException>(() => rules.GetStats(new(0, 2), 1));
    }

    [Fact]
    public async Task Cancellation_does_not_publish_a_partial_table_and_initialization_can_be_retried()
    {
        var rules = new CharacterStatRuleService(new SyntheticSource());
        Assert.Throws<InvalidOperationException>(() => rules.GetStats(default, 1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rules.InitializeAsync(cancellation.Token).AsTask());
        Assert.Throws<InvalidOperationException>(() => rules.GetStats(default, 1));
        await rules.InitializeAsync();
        Assert.Equal(100, rules.GetStats(default, 1).HpMax);
    }

    [Fact]
    public void Career_round_trips_all_packed_values_and_rejects_out_of_range_components()
    {
        for (var packed = 0; packed <= 255; packed++)
        {
            var career = CharacterCareer.FromPacked(13, packed);
            Assert.Equal(packed, career.PackedGrowType);
            Assert.Equal(packed & 15, career.GrowType);
            Assert.Equal(packed >> 4, career.Awakening);
        }
        Assert.Equal(new CharacterCareer(0), default);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterCareer(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterCareer(256));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterCareer(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterCareer(0, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterCareer(0, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterCareer(0, 0, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => CharacterCareer.FromPacked(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CharacterCareer.FromPacked(0, 256));
    }

    private sealed class SyntheticSource : ICharacterStatDefinitionSource
    {
        public int Reads { get; private set; }
        public async ValueTask<IReadOnlyList<CharacterStatDefinition>> GetAllAsync(CancellationToken cancellationToken)
        {
            Reads++;
            await Task.Yield();
            return [new(0, new(HpMax: 10), new Dictionary<CharacterCareer, CharacterStatValues>
            {
                [new(0)] = new(HpMax: 0.0125),
                [new(0, 1)] = new(HpMax: 2),
                [new(0, 1, 1)] = new(HpMax: 10),
                [new(0, 1, 2)] = new(HpMax: 30),
            })];
        }
        public ValueTask<CharacterBaseStats> GetInitialAsync(byte job, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

