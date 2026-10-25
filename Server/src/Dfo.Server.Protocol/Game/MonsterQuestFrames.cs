using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game;
internal static class MonsterQuestFrames
{
    internal static IReadOnlyList<IGameServerMessage> Of(MonsterKillEffects? applied)
    {
        var frames = new List<IGameServerMessage>();
        if (applied?.QuestItems is { } items) frames.Add(GameInventoryDelivery.Notification(items));
        if (applied?.AccountMaterials is { Count: > 0 } shared)
            frames.Add(GameInventoryDelivery.VirtualResourceNotification(shared, DateTimeOffset.UtcNow));
        if (applied?.QuestProgress?.Quests is { } quests)
            frames.Add(new AcceptedQuestProgressMessage([.. quests.OrderBy(x => x.Slot)
                .Select(x => new AcceptedQuestProgressState(checked((ushort)x.QuestId), unchecked((uint)x.TriggerValue)))]));
        return frames;
    }
}
