using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Commands;

public enum InternalCommandCancellation { QueueAndBody, BodyOnly }

public sealed record InternalCommandOrigin(
    GameplayCallSource Kind,
    GameplayCallChain Chain,
    string CommandType,
    InternalCommandCancellation Cancellation = InternalCommandCancellation.QueueAndBody,
    NetworkConnectionContext? SourceConnection = null)
{
    internal void Validate()
    {
        if (!Chain.IsInitialized || !Enum.IsDefined(Kind) || Kind == GameplayCallSource.Connection
            || !Enum.IsDefined(Chain.Source) || (Chain.Source != Kind
                && !(Kind == GameplayCallSource.Lifecycle && Chain.Source == GameplayCallSource.Connection)))
            throw new InvalidOperationException("An internal command requires an explicit internal source chain.");
        ArgumentException.ThrowIfNullOrWhiteSpace(CommandType);
        if (!Enum.IsDefined(Cancellation)) throw new InvalidOperationException("Unknown internal cancellation policy.");
        if (SourceConnection is { } source && Chain.SourceConnection != source.ConnectionId)
            throw new InvalidOperationException("The physical source differs from the origin chain.");
    }
}
