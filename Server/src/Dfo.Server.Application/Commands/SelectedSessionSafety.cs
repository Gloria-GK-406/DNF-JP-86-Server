using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Commands;

public sealed record SelectedSessionRecipient(SelectedSessionIdentity Session, NetworkConnectionContext? Connection, GameMessageMetadata? Metadata = null);

/// <summary>Unknown settlement outcomes survive retries and session/cache rebinding.</summary>
public sealed class SelectedSessionSafety(IServiceProvider services, ILogger<SelectedSessionSafety> logger)
{
    // The connection fence outlives the online user: dispatch consults it before any session is
    // resolved, and the connection is only asked to close after it is raised. Whether a session
    // is fenced is part of the session's own record.
    private readonly object gate = new();
    private readonly ConditionalWeakTable<NetworkConnectionContext, Fence> connections = new();
    private sealed class Fence;
    public bool IsBlocked(NetworkConnectionContext connection)
    { lock (gate) return connections.TryGetValue(connection, out _); }

    public SelectedSessionRecipient Capture(SelectedSessionIdentity session) =>
        Capture(session, services.GetService<NetworkCommandAuthorization>()?.Metadata);

    public SelectedSessionRecipient Capture(SelectedSessionIdentity session, GameMessageMetadata? metadata)
    {
        DemandCurrent(session);
        var users = services.GetService<IOnlineUserQueryDomainService>();
        NetworkConnectionContext? connection = null;
        if (users?.TryGetByConnection(session.ConnectionId, out var user) == true && user is not null)
        {
            if (user.AccountId != session.AccountId || user.CurrentCharacterId != session.CharacterId)
                throw new InvalidOperationException("The recipient connection no longer owns the selected character.");
            connection = user.Connection;
            if (IsBlocked(connection)) throw new InvalidOperationException("The recipient connection has an unknown settlement outcome.");
        }
        return new(session, connection, metadata);
    }
    public void DemandCurrent(SelectedSessionIdentity session)
    {
        if (services.GetService<ISelectedSessionDomainService>()?.StandingOf(session) is not { Fenced: false })
            throw new InvalidOperationException("The selected session is stale or has an unknown settlement outcome.");
    }
    public void DemandCurrent(SelectedSessionRecipient recipient)
    {
        DemandCurrent(recipient.Session);
        if (recipient.Connection is not { } connection) return;
        var users = services.GetService<IOnlineUserQueryDomainService>();
        if (IsBlocked(connection) || users?.TryGetByConnection(recipient.Session.ConnectionId, out var user) != true
            || user is null || !ReferenceEquals(user.Connection, connection)
            || user.AccountId != recipient.Session.AccountId || user.CurrentCharacterId != recipient.Session.CharacterId)
            throw new InvalidOperationException("The captured recipient connection was replaced or blocked.");
    }
    public async ValueTask BlockAsync(SelectedSessionRecipient recipient, Exception error, string operation)
    {
        GameplayWriteAssertion.Writing(nameof(SelectedSessionSafety) + "." + nameof(BlockAsync));
        services.GetService<ISelectedSessionDomainService>()?.Fence(recipient.Session);
        if (recipient.Connection is { } connection)
            lock (gate) connections.GetValue(connection, _ => new());
        try { services.GetService<ICreatureRuntimeDomainService>()?.DiscardExact(recipient.Session); }
        catch (Exception cleanup) { TryLog(() => logger.LogError(cleanup, "Discard of uncertain creature runtime failed")); }
        TryLog(() => logger.LogError(error, "Unknown settlement blocked operation={Operation} reason={Reason} account={Account} character={Character} connection={Connection} generation={Generation} sequence={Sequence} ordinal={Ordinal}",
            operation, TransactionOutcomeUncertainException.Find(error)?.Reason, recipient.Session.AccountId,
            recipient.Session.CharacterId, recipient.Session.ConnectionId, recipient.Session.ConnectionGeneration, recipient.Metadata?.Sequence, recipient.Metadata?.InboundOrdinal));
        if (recipient.Connection is { } original && services.GetService<INetworkConnectionCloser>() is { } closer)
            await closer.RequestCloseExactAsync(original, CancellationToken.None).ConfigureAwait(false);
    }
    private static void TryLog(Action log) { try { log(); } catch { /* Preserve settlement cause and continue exact close. */ } }
    public async ValueTask<TransactionOutcomeUncertainException> PublicationFailedAsync(
        SelectedSessionRecipient recipient, Exception cause, string operation)
    {
        GameplayWriteAssertion.Writing(nameof(SelectedSessionSafety) + "." + nameof(PublicationFailedAsync));
        var error = new TransactionOutcomeUncertainException(TransactionUncertaintyReason.PostCommitPublicationFailure,
            "Durable effects committed, but their runtime claim could not be published.", cause);
        await BlockAsync(recipient, error, operation).ConfigureAwait(false);
        return error;
    }
}
