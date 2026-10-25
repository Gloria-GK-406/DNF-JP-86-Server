using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Protocol.Game.Altar;

public enum AltarOperation { Ready, Advance, SelectDifficulty, Fail, SettleBeforeExit, SettleParticipantBeforeExit }
public sealed record AltarActionCommand(
    SelectedSessionIdentity Session, IReadOnlyList<SelectedSessionIdentity> Sessions,
    DungeonRunId RunId, long RoomVersion, long Generation, DateTimeOffset At,
    AltarOperation Operation, byte Difficulty = 0)
    : ICommand<AltarCommandResult>, IMultiSelectedSessionCommand
{
    SelectedSessionIdentity? ISelectedSessionCommand.Session => Session;
}
public sealed record AltarCommandResult(AltarActionResult? Action);
public sealed class AltarActionCommandHandler(IAltarDungeonDomainService altars)
    : ICommandHandler<AltarActionCommand, AltarCommandResult>
{
    public async ValueTask<AltarCommandResult> HandleAsync(CommandExecutionContext context, AltarActionCommand command, CancellationToken ct) =>
        new(await AltarCommandPhases.ActionAsync(altars, command.Session, command.RunId, command.RoomVersion,
            command.Generation, command.At, command.Operation, command.Difficulty, ct).ConfigureAwait(false));
}

internal static class AltarCommandPhases
{
    internal static ValueTask<AltarActionResult?> ActionAsync(IAltarDungeonDomainService altars,
        SelectedSessionIdentity session, DungeonRunId runId, long roomVersion, long generation, DateTimeOffset at,
        AltarOperation operation, byte difficulty, CancellationToken ct) => operation switch
    {
        AltarOperation.Ready => altars.ReadyAsync(session, runId, roomVersion, ct),
        AltarOperation.Advance => altars.AdvanceAsync(runId, roomVersion, generation, at, ct),
        AltarOperation.SelectDifficulty => altars.SelectDifficultyAsync(session, runId, roomVersion, generation, difficulty, ct),
        AltarOperation.Fail => altars.FailAsync(runId, ct),
        AltarOperation.SettleBeforeExit => altars.SettleBeforeExitAsync(runId, ct),
        AltarOperation.SettleParticipantBeforeExit => altars.SettleBeforeExitAsync(runId, ct, session.CharacterId),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    internal static async ValueTask<IReadOnlyList<MonsterDeathSettlementResult>> DeathBatchAsync(
        IAltarDungeonDomainService altars, IMonsterDeathDomainService deaths, SelectedSessionIdentity reporter,
        IReadOnlyList<SelectedSessionIdentity> cohort, DungeonRunId runId, long roomVersion,
        IReadOnlyList<ushort> actors, Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        var state = altars.Find(runId);
        if (state is not { Status: AltarStatus.Fighting } || state.RoomVersion != roomVersion) return [];
        var result = new List<MonsterDeathSettlementResult>();
        foreach (var sequence in actors.Distinct())
            result.Add(await deaths.SettleAsync(new(reporter, cohort, runId, roomVersion, sequence), footprint, ct).ConfigureAwait(false));
        return result;
    }
}
