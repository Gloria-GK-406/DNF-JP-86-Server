using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CharacterBuffs;

namespace Dfo.Server.Protocol.Game.GuildDungeon;

internal static class GuildDungeonPartyBuffProjection
{
    internal static ValueTask ActivateAsync(IGameCommandOutbox outbox, NetworkConnectionId connection, int buffId,
        CancellationToken token) =>
        outbox.RegisterExplicitAsync(connection, new(
            [new CharacterBuffAdded(buffId, State1: 0), new CharacterDungeonBuffs([buffId])], []), token);

    internal static ValueTask ClearAsync(IGameCommandOutbox outbox, ISelectedSessionDomainService sessions,
        SelectedSessionIdentity session, DungeonRunState? run, CancellationToken token) =>
        run?.Layout.Mode is DungeonRunMode.GuildDungeon { PartyBuffId: { } buffId }
            && sessions.IsInWorld(session)
            ? outbox.RegisterExplicitAsync(session.ConnectionId, new(
                [new CharacterBuffRemoved(buffId), new CharacterDungeonBuffs([])], []), token)
            : ValueTask.CompletedTask;
}
