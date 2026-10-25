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
internal partial class GameMonsterDeathCommandHandler
{
    protected virtual ValueTask<ObjectDestructionResult> DestroyObjectAsync(CommandExecutionContext execution,
        SelectedSessionIdentity session, DungeonRunId runId, long roomVersion, int objectCode,
        IReadOnlyList<SelectedSessionIdentity> recipients, CancellationToken ct) =>
        workUnits!.WriteAsync(execution, new("destroy-dungeon-object", []),
            (_, token) => DestroyObjectAsync(session, runId, roomVersion, objectCode, recipients, token), ct);
    protected async ValueTask<ObjectDestructionResult> DestroyObjectAsync(SelectedSessionIdentity session,
        DungeonRunId runId, long roomVersion, int objectCode, IReadOnlyList<SelectedSessionIdentity> recipients, CancellationToken ct)
    {
        if (!sessions.IsInWorld(session))
            return new(null);
        var run = runs.Find(session.CharacterId);
        if (run is null || run.RunId != runId || run.RoomVersion != roomVersion)
            return new(null);
        // A reporting client is not the loot owner. Freeze exact sessions at the protocol
        // boundary, then revalidate them here, inside the command's transaction.
        if (!recipients.Contains(session)
            || recipients.Count != run.Participants.Count
            || recipients.Select(value => value.CharacterId).Distinct().Count() != recipients.Count
            || recipients.Any(recipient => !sessions.IsInWorld(recipient)
                || runs.Find(recipient.CharacterId) is not { } memberRun
                || memberRun.RunId != runId || memberRun.RoomVersion != roomVersion
                || !run.Participants.Any(member => member.CharacterId == recipient.CharacterId
                    && member.AccountId == recipient.AccountId)))
            return new(null);
        var claim = runs.ClaimObjectDestruction(session.CharacterId, runId,
            roomVersion, objectCode);
        if (claim is null) return new(null);
        var rewards = new List<ObjectQuestReward>();
        foreach (var recipient in recipients)
        {
            var granted = await PendingQuestItemGrants.GrantAsync(recipient.AccountId, recipient.CharacterId,
                claim.PendingLoot, default, questLogs, containers, items, null, ct, failIfFull: true)
                .ConfigureAwait(false);
            var updated = granted.HasChanges
                ? await progress.ReportAsync(recipient.AccountId, recipient.CharacterId,
                    [new QuestHoldingsChanged()], ct).ConfigureAwait(false)
                : null;
            rewards.Add(new(recipient, new MonsterKillEffects(null, updated?.NotificationSnapshot, granted.CharacterItems, granted.AccountMaterials)));
        }
        return new(rewards);
    }
}
