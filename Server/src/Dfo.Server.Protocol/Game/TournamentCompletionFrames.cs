using Dfo.Server.Protocol.Game.Tournament;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game;
internal static class TournamentCompletionFrames
{
    internal static IReadOnlyList<IGameServerMessage> CompletionFrames(TournamentActionResult action, IInventorySlotProjectionDomainService slots, TimeProvider clock)
    {
        var frames = new List<IGameServerMessage>();
        if (action.Fatigue is { } fatigue) frames.Add(new Fatigue.FatigueNotification(fatigue));
        if (action.Experience is { } exp && action.SkillPoints is { } points)
            frames.Add(new ExperienceNotification(exp.Level, exp.NotificationTotal, points, AccountHonorLevel.Initial, ExperienceBonusLines.Of(exp)));
        AddInventory(frames, action, slots, clock);
        if (action.Quests is { } quests)
            frames.Add(new AcceptedQuestProgressMessage([.. quests.Quests.OrderBy(x => x.Slot)
                .Select(x => new AcceptedQuestProgressState(checked((ushort)x.QuestId), unchecked((uint)x.TriggerValue)))]));
        if (action.State.IsFinished && action.Experience is not null) frames.Add(new TournamentRewardMessage(action.State));
        return frames;
    }
    private static void AddInventory(List<IGameServerMessage> frames, TournamentActionResult action, IInventorySlotProjectionDomainService slots, TimeProvider clock)
    {
        if (action.GoldBalance is { } gold) frames.Add(GameInventoryDelivery.CarriedGoldNotification(gold, clock.GetUtcNow()));
        foreach (var group in (action.Delivery?.Inventory ?? []).GroupBy(x => slots.Project(x.Slot).Space))
            frames.Add(new InventoryDeltaMessage(group.Key, group.GroupBy(x => x.Slot).Select(x =>
            {
                var last = x.Last(); return new Dfo.Server.Data.Inventory.InventoryItemState(null, slots.Project(last.Slot).Index, last.Snapshot.ToCore());
            }).ToArray(), clock.GetUtcNow()));
    }
}
