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
/// <param name="Rewards">Null when the destruction was refused or already claimed.</param>
internal sealed record ObjectDestructionEffects(IReadOnlyList<ObjectRewardEffects>? Rewards);

internal sealed record ObjectRewardEffects(SelectedSessionIdentity Session, MonsterKillEffects Effects);

internal sealed partial class GameMonsterDeathCommandHandler
{
    private ValueTask<ObjectDestructionEffects> DestroyObjectAsync(CommandExecutionContext execution,
        SelectedSessionIdentity session, DungeonRunId runId, long roomVersion, int objectCode,
        IReadOnlyList<SelectedSessionIdentity> recipients, CancellationToken ct) =>
        workUnits.WriteAsync(execution, new("destroy-dungeon-object", []),
            (_, token) => DestroyObjectAsync(session, runId, roomVersion, objectCode, recipients, token), ct);
    // Projects inside the write unit: the touched slots are read as they stand before commit.
    internal async ValueTask<ObjectDestructionEffects> DestroyObjectAsync(SelectedSessionIdentity session,
        DungeonRunId runId, long roomVersion, int objectCode, IReadOnlyList<SelectedSessionIdentity> recipients, CancellationToken ct)
    {
        var destroyed = await deaths.DestroyObjectAsync(session, runId, roomVersion, objectCode, recipients, ct)
            .ConfigureAwait(false);
        if (destroyed.Rewards is null) return new(null);
        var rewards = new List<ObjectRewardEffects>(destroyed.Rewards.Count);
        foreach (var reward in destroyed.Rewards)
        {
            var (items, shared) = await ItemGrantWireProjection.QuestItemsAsync(reward.Items, clock.GetUtcNow(), ct)
                .ConfigureAwait(false);
            rewards.Add(new(reward.Session, new MonsterKillEffects(null, reward.QuestProgress, items, shared)));
        }
        return new(rewards);
    }
}
