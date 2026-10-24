using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Effects;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game;

/// <summary>Temporary adapter for the ledger's legacy route and Joust; remove after the last legacy caller.</summary>
internal sealed class LegacyReplayStateProjectionDelivery(ICommandBus commands, SelectedSessionSafety safety,
    IReplayStateProjectionSink sink) : IReplayStateProjectionDelivery
{
    public async ValueTask RefreshAsync(NetworkConnectionContext originalConnection, SelectedSessionIdentity session,
        ReplayProjectionRequest request, GameMessageMetadata? metadata, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(originalConnection);
        request.Validate();
        var recipient = safety.Capture(session, metadata);
        if (session.ConnectionId != originalConnection.ConnectionId || !ReferenceEquals(recipient.Connection, originalConnection)
            || safety.IsBlocked(originalConnection))
            throw new InvalidOperationException("Replay projection requires its exact original connection.");
        safety.DemandCurrent(recipient);
        var snapshot = await commands.SendAsync(new ReadReplayStateCommand(session, request, metadata), ct).ConfigureAwait(false);
        await sink.ProjectAsync(originalConnection, session, metadata, snapshot, null, ct).ConfigureAwait(false);
    }
}
