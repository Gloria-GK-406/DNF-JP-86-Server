using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class DungeonRoomMechanismRuleService : IDungeonRoomMechanismRuleService
{
    public DungeonRoomMechanismDefinition Define(DungeonRoomMechanismKind kind) => kind switch
    {
        DungeonRoomMechanismKind.Elevator => new(kind, TimeSpan.FromSeconds(15), 4),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public DungeonRoomMechanismState Start(DungeonRoomMechanismState state, DateTimeOffset now) =>
        state.StartedAt is null ? state with { StartedAt = now, Revision = state.Revision + 1 } : state;

    public DungeonRoomMechanismState Advance(DungeonRoomMechanismState state, DateTimeOffset now)
    {
        if (state.StartedAt is null || state.Outcome != DungeonRoomMechanismOutcome.Running) return state;
        var stage = StageAt(state, now);
        return stage <= state.Stage ? state : state with { Stage = stage, Revision = state.Revision + 1 };
    }

    public DungeonRoomMechanismState Complete(DungeonRoomMechanismState state, DateTimeOffset occurredAt)
    {
        if (state.StartedAt is null || state.Outcome != DungeonRoomMechanismOutcome.Running) return state;
        var stage = StageAt(state, occurredAt);
        return state with { Stage = stage, Outcome = stage < state.Definition.LastStage
            ? DungeonRoomMechanismOutcome.Normal : DungeonRoomMechanismOutcome.Crash, Revision = state.Revision + 1 };
    }

    public DateTimeOffset? NextDeadline(DungeonRoomMechanismState state) =>
        state.StartedAt is { } start && state.Outcome == DungeonRoomMechanismOutcome.Running
            && state.Stage < state.Definition.LastStage
            ? start.AddTicks(state.Definition.StageDuration.Ticks * (state.Stage + 1)) : null;

    public bool AllowsExit(DungeonRoomMechanismState state, MazeCell from, MazeCell to) =>
        state.Outcome != DungeonRoomMechanismOutcome.Running && to.Y == from.Y
            && to.X == from.X + (state.Outcome == DungeonRoomMechanismOutcome.Normal ? 1 : -1);

    private static byte StageAt(DungeonRoomMechanismState state, DateTimeOffset now) =>
        checked((byte)Math.Clamp((now - state.StartedAt!.Value).Ticks / state.Definition.StageDuration.Ticks,
            0, state.Definition.LastStage));
}
