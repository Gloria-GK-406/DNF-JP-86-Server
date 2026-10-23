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
/// Each message is registered as the sender registers any single send: encoded on the job, so a message
/// that cannot be encoded fails its recipient here, after the ones before it were registered, as a
/// send that threw part way through did before the outbox (TASK-23 ruling 1).
/// </remarks>
internal sealed class NotificationBus(
    IServiceScopeFactory scopeFactory,
    NotificationHandlerRegistry registry,
    INotificationAudienceResolver audiences,
    INetworkMessageSender sender,
    ISelectedSessionDomainService sessions,
    GameplayOutboxes outboxes,
    ILogger<NotificationBus> logger) : INotificationBus
{
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

            foreach (var message in messages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await sender.SendAsync(recipient.ConnectionId, message, cancellationToken).ConfigureAwait(false);
            }

            return NotificationOutcome.Delivered;
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
