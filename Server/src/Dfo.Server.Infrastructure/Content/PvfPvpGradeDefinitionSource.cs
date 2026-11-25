using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Pvp;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfPvpGradeDefinitionSource(PvfArchiveProvider provider) : IPvpGradeDefinitionSource, IContentWarmup
{
    private const string Path = "etc/pvp_ref.etc";
    private readonly Lazy<PvpGradeCatalog> catalog = new(() => Read(provider));
    public PvpGradeCatalog Load() => catalog.Value;
    public string Name => "pvp-grade";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Load().Grades.Count);

    private static PvpGradeCatalog Read(PvfArchiveProvider provider)
    {
        // The season reward block repeats [pvp grade] and [item] inside itself; only root fields are read.
        var root = PvfDocument.Open(provider.Archive, Path).AtRoot();
        // [pvp experience max grade] counts the threshold rows, whose last one is an unreachable
        // sentinel; the highest grade a record can hold is the first value of [pvp experience loss].
        var rows = root.Read(PvfField.Int32("[pvp experience max grade]", minimum: 2, maximum: byte.MaxValue));
        var pairs = root.Read(PvfField.Int32List("[pvp experience]"));
        var loss = root.Read(PvfField.Int32List("[pvp experience loss]"));
        var points = root.Read(PvfField.Int32List("[pvp grade point]"));
        PvfField.Require(pairs.Count == rows * 2, Path, "[pvp experience]", "expected one threshold per grade");
        PvfField.Require(loss.Count == 2 && loss[0] == rows - 1, Path, "[pvp experience loss]",
            "expected the top grade to be the last grade below the sentinel row");
        var maximum = (byte)loss[0];
        PvfField.Require(points.Count == (maximum + 1) * 3, Path, "[pvp grade point]", "expected one row per grade");

        var thresholds = new long[rows + 1];
        for (var i = 0; i < pairs.Count; i += 2)
        {
            var grade = i / 2 + 1;
            PvfField.Require(pairs[i] == grade && pairs[i + 1] > thresholds[grade - 1], Path, "[pvp experience]",
                "thresholds must name each grade in order and increase");
            thresholds[grade] = pairs[i + 1];
        }

        var grades = new PvpGradeStep[maximum + 1];
        for (var grade = 0; grade <= maximum; grade++)
        {
            var row = grade * 3;
            PvfField.Require(points[row] == grade && points[row + 1] >= 0 && points[row + 2] <= 0, Path,
                "[pvp grade point]", "expected (grade, win >= 0, loss <= 0) rows in grade order");
            grades[grade] = new((byte)grade, thresholds[grade], thresholds[grade + 1], points[row + 1], points[row + 2]);
        }

        var tournamentRelay = root.TryRead(PvfField.Int32("[pvp relay battle time for tournament]", optional: true, minimum: 1),
            out var milliseconds) ? TimeSpan.FromMilliseconds(milliseconds) : (TimeSpan?)null;
        return new(Array.AsReadOnly(grades), maximum, BattleTime(root, "[pvp normal battle time]"),
            BattleTime(root, "[pvp relay battle time]"), tournamentRelay);
    }

    private static TimeSpan BattleTime(PvfDocument root, string name) =>
        TimeSpan.FromMilliseconds(root.Read(PvfField.Int32(name, minimum: 1)));
}
