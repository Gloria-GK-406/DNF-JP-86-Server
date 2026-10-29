using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Game;
using Dfo.Server.Protocol.Game.EplpRechallenge;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game;

/// <summary>Orders a current accepted snapshot before the client's native submission check.</summary>
internal static class CycleQuestRechallengeProjection
{
    internal static IGameNotiMessage Check() => new EplpRechallengeNotification();
    internal static AcceptedQuestProgressMessage Progress(IReadOnlyList<AcceptedQuestState> accepted) =>
        new([.. accepted.OrderBy(x => x.Quest.Slot).Select(x => new AcceptedQuestProgressState(
            checked((ushort)x.Quest.QuestId), unchecked((uint)x.Quest.TriggerValue)))]);

    internal static IReadOnlyList<IGameServerMessage> ReadyFrames(
        DungeonRunState? run, IReadOnlyList<AcceptedQuestState> accepted) =>
        CycleQuestSubmission.IsReady(run, accepted) ? [Progress(accepted), Check()] : [];
}
