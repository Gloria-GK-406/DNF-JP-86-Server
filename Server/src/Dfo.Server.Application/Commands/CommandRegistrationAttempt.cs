using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Effects;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Commands;

public enum CommandRegistrationExit { SettledRequiredExplicit, CommittedReplayCurrentStateBatch, SettledInitialBatch }

internal sealed class CommandRegistrationAttempt(CommandRequestContext request, ExplicitEffectRequest state,
    CommandRegistrationExit exit, NetworkConnectionContext connection) : ICommandRegistrationAttempt
{
    private readonly CommandRequestContext request = request;
    private readonly ExplicitEffectRequest state = state;
    private readonly CommandRegistrationExit exit = exit;
    private readonly NetworkConnectionContext connection = connection;
    private bool entered;
    internal bool Completed { get; private set; }
    internal Exception? EncoderFailure { get; private set; }
    internal static CommandRegistrationAttempt EnterBatch(ICommandRegistrationAttempt token, NetworkConnectionId id) => Enter(token, true, id);
    internal static CommandRegistrationAttempt EnterRequiredExplicit(ICommandRegistrationAttempt token, NetworkConnectionId id) => Enter(token, false, id);
    private static CommandRegistrationAttempt Enter(ICommandRegistrationAttempt token, bool batch, NetworkConnectionId id)
    {
        var attempt = token as CommandRegistrationAttempt;
        try
        {
            if (attempt is null) throw new InvalidOperationException("Unknown command registration token.");
            attempt.request.DemandRegistrationCurrent();
            if (attempt.entered || attempt.Completed || !(batch ? attempt.exit is CommandRegistrationExit.CommittedReplayCurrentStateBatch or CommandRegistrationExit.SettledInitialBatch
                : attempt.exit == CommandRegistrationExit.SettledRequiredExplicit) || attempt.connection.ConnectionId != id)
                throw new InvalidOperationException("A registration attempt is single-call and stage/target bound.");
            attempt.state.DemandRegistration(attempt.exit, attempt.connection);
            attempt.entered = true;
        }
        catch (Exception error) { attempt?.request.Failed(error); GameplayJob.RecordProgramError(error); throw; }
        return attempt;
    }
    internal void DemandTarget(NetworkConnectionContext actual)
    {
        if (!ReferenceEquals(connection, actual))
        {
            var error = new InvalidOperationException("The registration target was replaced.");
            request.Failed(error); GameplayJob.RecordProgramError(error); throw error;
        }
    }
    internal void EncoderFailed(Exception error)
    { EncoderFailure = error; request.Failed(error); }
    internal void Failed(Exception error) => request.Failed(error);
    internal void Complete() => Completed = true;
}
