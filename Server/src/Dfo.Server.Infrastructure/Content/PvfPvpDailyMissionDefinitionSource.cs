using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Pvp;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfPvpDailyMissionDefinitionSource(PvfArchiveProvider provider) : IPvpDailyMissionDefinitionSource, IContentWarmup
{
    private const string CatalogPath = "etc/missionsystem.etc";
    private const string ListPath = "pvp_mission/mission.lst";
    private readonly Lazy<IReadOnlyList<PvpDailyMissionDefinition>> definitions = new(() => Read(provider.Archive));

    public IReadOnlyList<PvpDailyMissionDefinition> Load() => definitions.Value;
    public string Name => "pvp-daily-missions";
    public ValueTask<int> WarmAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Load().Count);

    private static IReadOnlyList<PvpDailyMissionDefinition> Read(PvfArchive archive)
    {
        var list = archive.ReadList(ListPath).Paths(PvfListDuplicates.First, "pvp_mission/");
        var table = PvfDocument.Open(archive, CatalogPath).AtRoot();
        List<PvpDailyMissionDefinition> result = [];
        ReadPool("[casual daily mission]", PvpDailyMissionPool.Casual);
        ReadPool("[total match daily mission]", PvpDailyMissionPool.TotalMatch);
        return result.AsReadOnly();

        void ReadPool(string field, PvpDailyMissionPool pool)
        {
            var groups = table.ReadAll(PvfField.Int32List(field));
            foreach (var row in groups)
            {
                PvfField.Require(row.Count >= 3 && row.Count % 2 == 1, CatalogPath, field, "expected group and ID/weight pairs");
                for (var index = 1; index < row.Count; index += 2)
                {
                    var id = row[index];
                    PvfField.Require(row[index + 1] > 0, CatalogPath, field, "invalid mission weight");
                    // The A21 casual pool still names 128 and 129, which are absent from mission.lst.
                    if (!list.TryGetValue(id, out var path)) continue;
                    result.Add(ReadMission(archive, path, checked((ushort)id), pool, checked((byte)row[0]), row[index + 1]));
                }
            }
        }
    }

    private static PvpDailyMissionDefinition ReadMission(PvfArchive archive, string path, ushort id,
        PvpDailyMissionPool pool, byte group, int weight)
    {
        var script = PvfDocument.Open(archive, path);
        var condition = script.Read(PvfField.Tokens("[condition]"));
        var mode = pool == PvpDailyMissionPool.TotalMatch ? "[total match mode]" : "[all]";
        PvfField.Require(condition.Count == 4
            && condition[0].IsString && condition[1].IsString && archive.ResolveString(condition[1].Value) == mode
            && condition[2] is { Kind: PvfTokenKind.Int32, Value: > 0 } && condition[3].Kind == PvfTokenKind.Int32,
            path, "[condition]", "expected condition, mode, goal and parameter");
        var kind = archive.ResolveString(condition[0].Value) switch
        {
            "[hereafter join]" => PvpDailyMissionCondition.Participate,
            "[hereafter winning count]" => PvpDailyMissionCondition.Win,
            "[use skill]" => PvpDailyMissionCondition.ColorlessSkills,
            "[reduce HP]" => PvpDailyMissionCondition.ReduceHealth,
            "[remainS HP]" => PvpDailyMissionCondition.RemainingHealth,
            "[winning job]" => PvpDailyMissionCondition.DefeatJob,
            "[kill count]" => PvpDailyMissionCondition.Knockouts,
            "[winning streak]" => PvpDailyMissionCondition.WinStreak,
            "[win pose]" => PvpDailyMissionCondition.WinPose,
            "[winning count]" => PvpDailyMissionCondition.ColosseumWin,
            "[total match win left character]" => PvpDailyMissionCondition.TotalMatchSurvivors,
            "[total match win all kill]" => PvpDailyMissionCondition.TotalMatchSweep,
            var unknown => throw new InvalidDataException($"{path}: unknown PvP daily mission condition {unknown}."),
        };
        var root = script.AtRoot();
        int itemId, count;
        if (root.TryRead(PvfField.Int32("[reward victory emblem]", optional: true, minimum: 1), out count))
            itemId = 2;
        else
        {
            var reward = root.Read(PvfField.Int32List("[reward item]"));
            PvfField.Require(reward.Count == 2 && reward[0] > 0 && reward[1] > 0, path, "[reward item]", "expected item and count");
            (itemId, count) = (reward[0], reward[1]);
        }
        return new(id, pool, group, weight, kind, condition[2].Value, condition[3].Value, itemId, count);
    }
}
