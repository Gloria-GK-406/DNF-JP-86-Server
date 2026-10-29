using Dfo.Server.Abstractions.Quests;

namespace Dfo.Server.Abstractions.World;

/// <summary>When a cycle run's quest can be handed in from inside the run.</summary>
public static class CycleQuestSubmission
{
    /// <summary>A cycle run, cleared, for a quest the client can name.</summary>
    public static bool IsCycleClear(DungeonRunState? run) =>
        run is { ClearOpen: true, Layout.Purpose.Kind: DungeonRunPurposeKind.Cycle,
            Layout.Purpose.QuestId: > 0 and <= ushort.MaxValue };

    /// <summary>A cycle clear whose quest the character carries and can complete.</summary>
    public static bool IsReady(DungeonRunState? run, IReadOnlyList<AcceptedQuestState> accepted) =>
        IsCycleClear(run) && accepted.Any(x => x.Quest.QuestId == run!.Layout.Purpose.QuestId && x.CanComplete);
}
