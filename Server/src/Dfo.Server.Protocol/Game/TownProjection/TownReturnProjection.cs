using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonSelection;

namespace Dfo.Server.Protocol.Game.TownProjection;

/// <summary>
/// The frames that put a character back in the town they are standing in.
/// </summary>
/// <remarks>
/// Two things arrive here from two directions and mean the same thing: cancelling out of the
/// dungeon-selection screen, and walking out of a finished dungeon. The reference server routes
/// both through one handler; this server keeps the two commands apart, because they are two
/// commands, but the projection behind them has to be one thing. Written twice it would drift, and
/// the failure would look like "returning to town works from the gate and not from the dungeon" --
/// which is the shape of the defect this was extracted while fixing.
/// <para>
/// The area comes from where the character is standing rather than from the channel's spawn point.
/// A player who walked to the dungeon gate and came back should be at the gate; projecting the
/// spawn would carry them across town for pressing a button.
/// </para>
/// <para>
/// Where they are standing is passed in rather than taken off the session's snapshot of the
/// character. This is a projection: it has no dependencies and looks nothing up, so the two
/// handlers that send it ask the service that owns the position and hand the answer over.
/// </para>
/// </remarks>
internal static class TownReturnProjection
{
    /// <summary>The user's state byte for standing in a town, which is where this puts them.</summary>
    private const byte StandingInTown = 0x00;

    public static IReadOnlyList<IGameServerMessage> Frames(
        SelectedSessionIdentity session,
        SelectCharacterResult result,
        CharacterPositionState position)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(position);

        var spawn = result.World.Channel.Spawn with
        {
            TownId = position.TownId,
            AreaId = position.AreaId,
            X = position.X,
            Y = position.Y,
        };

        return
        [
            new UserStateNotification(session.WireUserId, StandingInTown),
            new UserAreaNotification(session.WireUserId, spawn, position.AreaId),
            new AreaUsersNotification(
                position.TownId,
                position.AreaId,
                [
                    new AreaUserPresence(
                        session.WireUserId, position.X, position.Y,
                        position.Direction, spawn.AreaState),
                ]),
        ];
    }
}
