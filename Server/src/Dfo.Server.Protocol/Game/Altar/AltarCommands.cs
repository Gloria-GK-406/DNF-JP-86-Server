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
        new(await (command.Operation switch
        {
            AltarOperation.Ready => altars.ReadyAsync(command.Session, command.RunId, command.RoomVersion, ct),
            AltarOperation.Advance => altars.AdvanceAsync(command.RunId, command.RoomVersion, command.Generation, command.At, ct),
            AltarOperation.SelectDifficulty => altars.SelectDifficultyAsync(command.Session, command.RunId, command.RoomVersion, command.Generation, command.Difficulty, ct),
            AltarOperation.Fail => altars.FailAsync(command.RunId, ct),
            AltarOperation.SettleBeforeExit => altars.SettleBeforeExitAsync(command.RunId, ct),
            AltarOperation.SettleParticipantBeforeExit => altars.SettleBeforeExitAsync(command.RunId, ct, command.Session.CharacterId),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        }).ConfigureAwait(false));
}
public sealed record AltarDeathBatchCommand(
    SelectedSessionIdentity Session, IReadOnlyList<SelectedSessionIdentity> Sessions,
    DungeonRunId RunId, long RoomVersion, IReadOnlyList<ushort> Actors)
    : ICommand<IReadOnlyList<MonsterDeathSettlementResult>>, IMultiSelectedSessionCommand
{
    SelectedSessionIdentity? ISelectedSessionCommand.Session => Session;
}
public sealed class AltarDeathBatchCommandHandler(IMonsterDeathDomainService deaths, IAltarDungeonDomainService altars)
    : ICommandHandler<AltarDeathBatchCommand, IReadOnlyList<MonsterDeathSettlementResult>>
{
    public async ValueTask<IReadOnlyList<MonsterDeathSettlementResult>> HandleAsync(CommandExecutionContext context, AltarDeathBatchCommand command, CancellationToken ct)
    {
        var state = altars.Find(command.RunId);
        if (state is not { Status: AltarStatus.Fighting } || state.RoomVersion != command.RoomVersion) return [];
        var result = new List<MonsterDeathSettlementResult>();
        foreach (var sequence in command.Actors.Distinct())
            result.Add(await deaths.SettleAsync(new(command.Session, command.Sessions, command.RunId, command.RoomVersion, sequence), ct).ConfigureAwait(false));
        return result;
    }
}
