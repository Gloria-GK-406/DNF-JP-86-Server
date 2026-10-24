using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Execution.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Notifications;

/// <summary>
/// Composes a publish per recipient and registers each recipient's messages for his connection, in
/// order, through the sender, which only registers them in the running job's outbox (RQ-030). Nothing
/// is sent here and nothing is retried: a recipient whose composition or encoding fails is that
/// publish's <see cref="NotificationOutcome.Failed"/>, logged once, and never asked again (VC-5,
/// RQ-031(d)). The outbound sender delivers after the job ends.
/// </summary>
/// <remarks>
/// A recipient's connection is looked up once, and all his messages are registered as one prepared batch
/// for that exact connection: encoded whole on the job, so one message that cannot be encoded fails the
/// recipient with none of his messages registered (RQ-031(d), F-102). A connection that is no longer
/// registered drops the recipient's messages, as a send to it is dropped (F-83).
/// </remarks>
internal sealed class NotificationBus : INotificationBus
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly NotificationHandlerRegistry registry;
    private readonly INotificationAudienceResolver audiences;
    private readonly INetworkMessageSender sender;
    private readonly ISelectedSessionDomainService sessions;
    private readonly GameplayOutboxes outboxes;
    private readonly INetworkConnectionContexts? contexts;
    private readonly ILogger<NotificationBus> logger;

    /// <summary>Without networking every connection is unknown, so every recipient's messages are dropped.</summary>
    public NotificationBus(
        IServiceScopeFactory scopeFactory,
        NotificationHandlerRegistry registry,
        INotificationAudienceResolver audiences,
        INetworkMessageSender sender,
        ISelectedSessionDomainService sessions,
        GameplayOutboxes outboxes,
        ILogger<NotificationBus> logger)
    {
        this.scopeFactory = scopeFactory;
        this.registry = registry;
        this.audiences = audiences;
        this.sender = sender;
        this.sessions = sessions;
        this.outboxes = outboxes;
        this.logger = logger;
    }

    public NotificationBus(
        IServiceScopeFactory scopeFactory,
        NotificationHandlerRegistry registry,
        INotificationAudienceResolver audiences,
        INetworkMessageSender sender,
        ISelectedSessionDomainService sessions,
        GameplayOutboxes outboxes,
        INetworkConnectionContexts contexts,
        ILogger<NotificationBus> logger)
        : this(scopeFactory, registry, audiences, sender, sessions, outboxes, logger) => this.contexts = contexts;

    public async ValueTask<NotificationDelivery> PublishAsync<TNotification>(
        NotificationAudience audience,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : class, INotification<TNotification>
    {
        ArgumentNullException.ThrowIfNull(audience);
        ArgumentNullException.ThrowIfNull(notification);
        var registration = registry.GetRegistration(typeof(TNotification));
        var recipients = audiences.Resolve(audience);
        if (recipients.Count == 0)
            return new NotificationDelivery([]);

        // The registration point, asked once before anyone is composed: off the executor this is a
        // program error (RQ-061, the outbox half), not one recipient's failure.
        _ = outboxes.Current;
        var attempts = new List<NotificationAttempt>(recipients.Count);

        // One scope for the publish rather than one per recipient: the handler is asked repeatedly
        // and owns nothing that should differ between two people in the same audience.
        await using var scope = scopeFactory.CreateAsyncScope();
        var composer = (INotificationComposer)scope.ServiceProvider
            .GetRequiredService(registration.ComposerType);

        foreach (var recipient in recipients)
        {
            var outcome = await AttemptAsync(composer, recipient, notification, cancellationToken)
                .ConfigureAwait(false);
            attempts.Add(new NotificationAttempt(recipient.CharacterId, outcome));
        }

        return new NotificationDelivery(attempts);
    }

    /// <summary>One recipient, isolated from the rest.</summary>
    /// <remarks>
    /// Everything that can go wrong here is caught, because the alternative is that whoever is
    /// standing next to a player whose connection died does not hear about it either. The one
    /// exception is the caller's own cancellation: that is not this recipient failing, it is the
    /// publish being called off.
    /// </remarks>
    private async ValueTask<NotificationOutcome> AttemptAsync(
        INotificationComposer composer,
        NotificationRecipient recipient,
        object notification,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Asked before the handler is, so that somebody who has already left costs nothing and
            // is reported as having left rather than as a failure. Inside the guard with everything
            // else: a session lookup that threw would otherwise take down the whole publish.
            if (!IsStillHere(recipient))
                return NotificationOutcome.Gone;

            var messages = await composer
                .ComposeAsync(recipient, notification, cancellationToken).ConfigureAwait(false);
            if (messages is null || messages.Count == 0)
                return NotificationOutcome.Declined;

            cancellationToken.ThrowIfCancellationRequested();
            if (contexts is not null && contexts.TryGet(recipient.ConnectionId, out var connection) && connection is not null)
                await sender.SendBatchAsync(connection, messages, cancellationToken).ConfigureAwait(false);
            else if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Notification for character {CharacterId} was dropped: {ConnectionId} is no longer registered",
                    recipient.CharacterId.Value, recipient.ConnectionId);

            return NotificationOutcome.Registered;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            // Logged here because this is where the exception still exists: the outcome that
            // travels onward is a single word.
            logger.LogWarning(
                error,
                "Notification for character {CharacterId} failed.",
                recipient.CharacterId.Value);
            return NotificationOutcome.Failed;
        }
    }

    /// <summary>Does this recipient still own the resolved session or connection?</summary>
    /// <remarks>
    /// A character audience checks its resolved connection and keeps its replacement semantics.
    /// An exact audience checks the whole selected-session identity, including generation and
    /// listener. Asking before composing is why a replaced recipient costs no handler call.
    /// </remarks>
    private bool IsStillHere(NotificationRecipient recipient) =>
        recipient.ExpectedSession is { } exact
            ? sessions.IsInWorld(exact)
            : sessions.TryGetInWorld(recipient.CharacterId, out var session)
                && session.ConnectionId == recipient.ConnectionId;
}
