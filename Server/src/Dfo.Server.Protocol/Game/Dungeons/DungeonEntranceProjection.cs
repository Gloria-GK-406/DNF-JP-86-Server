using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;

namespace Dfo.Server.Protocol.Game.Dungeons;

/// <summary>
/// NOTI 0x022F: where a boss room entrance condition placed its targets, for the minimap.
/// </summary>
/// <remarks>
/// A21 handler 0x0114C580 reads <c>u16 n</c> and <c>n x (u8 x, u8 y, u32 code)</c>, matches each
/// code against the dungeon's <c>[with monster]</c> icon entries and gives the matching icon this
/// room. Sent after DUNGEON_INFO and before START_MAP, as the reference server sends it. A dungeon
/// whose icons are fixed <c>[grid]</c> cells (2016) matches nothing and keeps its fixed icons.
/// </remarks>
internal sealed record DungeonEntranceTargetIconsNotification(IReadOnlyList<DungeonEntranceTarget> Targets)
    : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameEntranceTargetIcons;
}

/// <summary>
/// NOTI 0x0138: every target of the boss room entrance condition is hunted.
/// </summary>
/// <remarks>
/// A21 handler 0x01193290 reads <c>u32, u8</c> and ignores both; the party leader's client then
/// requests the summon declared by its current map (CMD 0x0211). It acts only on what the current
/// room declares and keeps nothing for later, so it is sent in the room where the last target died.
/// </remarks>
internal sealed record DungeonEntranceConditionPassedNotification : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameEntranceConditionPassed;
}

internal static class DungeonEntranceProjection
{
    /// <summary>The icon frame for a run that placed entrance targets; null otherwise.</summary>
    public static DungeonEntranceTargetIconsNotification? IconsFor(DungeonLayout layout) =>
        layout.EntrancePlan is { Targets.Count: > 0 } plan ? new(plan.Targets) : null;
}

internal sealed class DungeonEntranceTargetIconsEncoder : GameNotiMessageEncoder<DungeonEntranceTargetIconsNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(DungeonEntranceTargetIconsNotification message)
    {
        using var stream = new MemoryStream(2 + message.Targets.Count * 6);
        using var writer = new BinaryWriter(stream);
        writer.Write(checked((ushort)message.Targets.Count));
        foreach (var target in message.Targets)
        {
            writer.Write(target.Cell.X);
            writer.Write(target.Cell.Y);
            writer.Write(target.Code);
        }
        return OnePayload(message, stream.ToArray());
    }
}

internal sealed class DungeonEntranceConditionPassedEncoder : GameNotiMessageEncoder<DungeonEntranceConditionPassedNotification>
{
    /// <summary><c>u32 0, u8 0</c>, as the reference builds it; the A21 handler reads and discards both.</summary>
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(DungeonEntranceConditionPassedNotification message) =>
        OnePayload(message, new byte[5]);
}
