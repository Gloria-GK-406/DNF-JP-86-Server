using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>
/// Stands in for session-adjacent projection in tests whose subject is something else.
/// </summary>
internal sealed class StubSessionAdjacentEntryDomainService(SessionAdjacentEntryState? state = null)
    : ISessionAdjacentEntryDomainService
{
    public SessionAdjacentSubject? LastSubject { get; private set; }

    public ValueTask<SessionAdjacentEntryState> LoadAsync(
        SessionAdjacentSubject subject,
        CancellationToken cancellationToken)
    {
        LastSubject = subject;
        return ValueTask.FromResult(state ?? SessionAdjacentEntryState.Empty);
    }
}

/// <summary>Session-adjacent projection that fails, for proving entry publishes nothing.</summary>
internal sealed class FailingSessionAdjacentEntryDomainService : ISessionAdjacentEntryDomainService
{
    public ValueTask<SessionAdjacentEntryState> LoadAsync(
        SessionAdjacentSubject subject,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<SessionAdjacentEntryState>(
            new InvalidDataException("session state is unavailable"));
}
