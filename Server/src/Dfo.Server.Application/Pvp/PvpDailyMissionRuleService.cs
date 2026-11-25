using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpDailyMissionRuleService(IPvpDailyMissionDefinitionSource definitions)
{
    public IEnumerable<PvpDailyMissionDefinition> Assigned(CharacterId character, DateOnly day, PvpDailyMissionPool pool,
        IReadOnlyDictionary<ushort, int> progress)
    {
        foreach (var group in definitions.Load().Where(mission => mission.Pool == pool && CanAssign(mission))
                     .GroupBy(mission => mission.Group).OrderBy(group => group.Key))
        {
            var pinned = group.FirstOrDefault(mission => progress.ContainsKey(mission.Id));
            if (pinned is not null) { yield return pinned; continue; }
            var seed = Encoding.UTF8.GetBytes(FormattableString.Invariant($"{character.Value}/{day.DayNumber}/{pool}/{group.Key}"));
            var selected = BinaryPrimitives.ReadUInt32LittleEndian(SHA256.HashData(seed)) % checked((uint)group.Sum(mission => mission.Weight));
            foreach (var mission in group.OrderBy(mission => mission.Id))
            {
                if (selected < mission.Weight) { yield return mission; break; }
                selected -= checked((uint)mission.Weight);
            }
        }
    }

    private static bool CanAssign(PvpDailyMissionDefinition mission) => mission.Condition switch
    {
        PvpDailyMissionCondition.ColosseumWin => true,
        PvpDailyMissionCondition.WinStreak or PvpDailyMissionCondition.WinPose => mission.RewardItemId == PvpVictoryEmblems.ItemId,
        PvpDailyMissionCondition.Participate or PvpDailyMissionCondition.Win or PvpDailyMissionCondition.Knockouts
            or PvpDailyMissionCondition.DefeatJob or PvpDailyMissionCondition.ColorlessSkills
            or PvpDailyMissionCondition.ReduceHealth or PvpDailyMissionCondition.RemainingHealth
            or PvpDailyMissionCondition.TotalMatchSurvivors or PvpDailyMissionCondition.TotalMatchSweep
            => mission.RewardItemId == PvpVictoryEmblems.ItemId,
        _ => false,
    };
}
