using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Data.Raids;

namespace Dfo.Server.Application.Raids;

internal sealed class AntonRaidRuleService(IAntonRaidContentSource source, IPartyModeRuleService partyRules)
{
    private const int AdmissionQuestId = 12829;
    private const int DailyPhaseClearQuestId = 12830;

    internal const byte SquadCount = 10;
    internal static readonly TimeSpan ApprovalLifetime = TimeSpan.FromSeconds(60);

    internal AntonRaidRules Current => source.Load();

    internal static bool CanEnterChannel(IReadOnlyList<ClearedQuest> clearedQuests) =>
        clearedQuests.Any(quest => quest.QuestId == AdmissionQuestId);

    internal static bool LimitsPhaseClearQuestProgressToOncePerDay(int questId) =>
        questId == DailyPhaseClearQuestId;

    internal bool HasRecruitmentSpace(int members) => Fits(Current, members + 1);

    internal bool HasSquadSpace(int members) => members < partyRules.MaximumCapacity;

    internal static bool CanStartWith(AntonRaidRules rules, int members) =>
        members >= rules.MinimumMembers && Fits(rules, members);

    private static bool Fits(AntonRaidRules rules, int members) => members <= rules.MaximumMembers;

    internal static int HatcheryAt(int room) => room switch
    {
        19100 => 224,
        19103 => 221,
        19116 => 223,
        19119 => 222,
        _ => 0,
    };
}
