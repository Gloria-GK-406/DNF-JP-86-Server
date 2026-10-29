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
internal sealed partial class GameMonsterDeathCommandHandler
{
    private ValueTask<ObjectDestructionResult> DestroyObjectAsync(CommandExecutionContext execution,
        SelectedSessionIdentity session, DungeonRunId runId, long roomVersion, int objectCode,
        IReadOnlyList<SelectedSessionIdentity> recipients, CancellationToken ct) =>
        workUnits.WriteAsync(execution, new("destroy-dungeon-object", []),
            (_, token) => DestroyObjectAsync(session, runId, roomVersion, objectCode, recipients, token), ct);
    internal ValueTask<ObjectDestructionResult> DestroyObjectAsync(SelectedSessionIdentity session,
        DungeonRunId runId, long roomVersion, int objectCode, IReadOnlyList<SelectedSessionIdentity> recipients, CancellationToken ct) =>
        deaths.DestroyObjectAsync(session, runId, roomVersion, objectCode, recipients, ct);
}
