using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Effects;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Commands;

public enum CommandRegistrationExit { SettledRequiredExplicit, CommittedReplayCurrentStateBatch }

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
    internal static CommandRegistrationAttempt Enter(ICommandRegistrationAttempt token, CommandRegistrationExit expected, NetworkConnectionId id)
    {
        if (token is not CommandRegistrationAttempt attempt) throw new InvalidOperationException("Unknown command registration token.");
        try
        {
            attempt.request.DemandRegistrationCurrent();
            if (attempt.entered || attempt.Completed || attempt.exit != expected || attempt.connection.ConnectionId != id)
                throw new InvalidOperationException("A registration attempt is single-call and stage/target bound.");
            attempt.state.DemandRegistration(expected, attempt.connection);
            attempt.entered = true;
        }
        catch (Exception error) { attempt.request.Failed(error); throw; }
        return attempt;
    }
    internal void DemandTarget(NetworkConnectionContext actual)
    { if (!ReferenceEquals(connection, actual)) throw new InvalidOperationException("The registration target was replaced."); }
    internal void EncoderFailed(Exception error)
    { EncoderFailure = error; request.Failed(error); }
    internal void Failed(Exception error) => request.Failed(error);
    internal void Complete() => Completed = true;
}
