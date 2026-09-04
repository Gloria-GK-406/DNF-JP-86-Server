using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Notifications;

/// <summary>How long one recipient gets before the rest of the audience moves on.</summary>
/// <remarks>
/// One value for every notification, on purpose. Delivery is sequential, so a recipient who is
/// slow holds up everyone behind them, and a single number is the thing worth being able to say
/// about that. A per-notification override would make the worst case depend on which notification
/// happened to be in flight.
/// <para>
/// What it bounds is what the send path observes of it. It cancels the token handed to the
/// transport; how promptly a stalled socket answers that token is the transport's business, and
/// nothing here has measured it. Read this as the deadline this code enforces, not as a proven
/// ceiling on wall-clock time per recipient.
/// </para>
/// </remarks>
public sealed record NotificationDeliveryOptions(
    TimeSpan PerRecipientTimeout,
    TimeSpan RetryInterval)
{
    public static NotificationDeliveryOptions Default { get; } =
        new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
}

/// <summary>Gives the recipients who failed another go.</summary>
/// <remarks>
/// Separate from <see cref="INotificationBus"/> because it is not something a caller does. It is
/// driven by the one loop this service is allowed to have, and by the cases that need to watch a
/// retry happen without waiting for a clock.
/// </remarks>
internal interface INotificationRetryDrain
{
    ValueTask DrainAsync(CancellationToken cancellationToken);
}

internal sealed class NotificationBus(
    IServiceScopeFactory scopeFactory,
    NotificationHandlerRegistry registry,
    INotificationAudienceResolver audiences,
    INetworkMessageSender sender,
    ISelectedSessionDomainService sessions,
    NotificationRetryQueue retries,
    NotificationDeliveryOptions options,
    ILogger<NotificationBus> logger) : INotificationBus, INotificationRetryDrain
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
            if (outcome == NotificationOutcome.Failed)
                Requeue(registration, notification, recipient.CharacterId, used: 0);

            attempts.Add(new NotificationAttempt(recipient.CharacterId, outcome));
        }

        return new NotificationDelivery(attempts);
    }

    /// <summary>Works through what was waiting when the pass began.</summary>
    /// <remarks>
    /// What was waiting, not what is waiting: a recipient who fails again goes to the back, and a
    /// pass that kept draining would meet them again in the same breath and spend a notification's
    /// whole retry allowance in one instant.
    /// </remarks>
    public async ValueTask DrainAsync(CancellationToken cancellationToken)
    {
        // Asked before taking rather than after: a pass that stopped with an entry already in hand
        // would drop that recipient's remaining goes without anything having failed.
        for (var pending = retries.Count; pending > 0; pending--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!retries.TryTake(out var entry))
                break;

            await RetryAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Puts a failed recipient back, if they have a go left.</summary>
    /// <remarks>
    /// Decided here rather than when the entry comes back out, so nothing sits in the queue that
    /// nobody will try. Otherwise a recipient whose allowance had just run out would wait a whole
    /// interval to be discarded, and the queue's depth would be counting them the entire time.
    /// </remarks>
    private void Requeue(
        NotificationHandlerRegistration registration,
        object notification,
        CharacterId characterId,
        int used)
    {
        if (used < registration.RetryLimit)
        {
            retries.Add(new NotificationRetry(registration, notification, characterId, used));
            return;
        }

        // The last thing anybody will ever hear about this recipient. Best effort means a
        // notification may be missed; it does not mean nobody may find out that it was.
        //
        // Only when there were second chances to spend. A notification that declared none was
        // never going to try again, and the failure itself has already been logged -- saying
        // "gave up after 0 of 0" on top of it is two lines about one event.
        if (registration.RetryLimit > 0)
        {
            logger.LogWarning(
                "Notification {Notification} gave up on character {CharacterId} after {Used} "
                    + "of {Limit} further attempts.",
                registration.NotificationType.Name,
                characterId.Value,
                used,
                registration.RetryLimit);
        }
    }

    /// <summary>One waiting recipient, asked about from scratch.</summary>
    /// <remarks>
    /// A retry is a fresh evaluation for one person rather than the tail of the publish that
    /// queued it. Where they are is asked again, and so is what to tell them -- which is why the
    /// entry carries neither.
    /// <para>
    /// That is also why somebody who reconnected between the failure and now is delivered to here,
    /// while the same person mid-publish is <see cref="NotificationOutcome.Gone"/>. The two are
    /// not the same moment. Mid-publish, their session was replaced while this very publish was
    /// running, and what is in hand was worked out against the session that just went away. Here,
    /// nothing is in hand: the question starts at "who is this character and where are they", the
    /// answer is their current session, and the handler composes against it. The queue names a
    /// person, not a session, and this is what that means.
    /// </para>
    /// </remarks>
    private async ValueTask RetryAsync(NotificationRetry entry, CancellationToken cancellationToken)
    {
        // Through the audience, so that "which connection is this character on" keeps having one
        // answer. Nobody home is not a failure and not another retry: they have stopped being
        // somebody this notification can reach.
        var recipient = audiences
            .Resolve(new NotificationAudience.TheseCharacters([entry.CharacterId]))
            .FirstOrDefault();
        if (recipient is null)
            return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var composer = (INotificationComposer)scope.ServiceProvider
            .GetRequiredService(entry.Registration.ComposerType);
        var outcome = await AttemptAsync(composer, recipient, entry.Notification, cancellationToken)
            .ConfigureAwait(false);
        if (outcome == NotificationOutcome.Failed)
            Requeue(entry.Registration, entry.Notification, entry.CharacterId, entry.Attempts + 1);
    }

    /// <summary>One recipient, isolated from the rest.</summary>
    /// <remarks>
    /// Everything that can go wrong here is caught, because the alternative is that whoever is
    /// standing next to a player whose connection died does not hear about it either. The one
    /// exception is the caller's own cancellation: that is not this recipient failing, it is the
    /// publish being called off, and swallowing it would keep sending after the answer stopped
    /// mattering.
    /// </remarks>
    private async ValueTask<NotificationOutcome> AttemptAsync(
        INotificationComposer composer,
        NotificationRecipient recipient,
        object notification,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.PerRecipientTimeout);
        try
        {
            // Asked before the handler is, so that somebody who has already left costs nothing and
            // is reported as having left rather than as a delivery that failed. Inside the guard
            // with everything else: a session lookup that threw would otherwise take down the
            // whole publish, and this method's promise is that one recipient's trouble stays
            // theirs. It is still a race -- they can go in the next instant -- which is why a send
            // that throws is its own outcome.
            if (!IsStillHere(recipient))
                return NotificationOutcome.Gone;

            var messages = await composer
                .ComposeAsync(recipient, notification, deadline.Token).ConfigureAwait(false);
            if (messages is null || messages.Count == 0)
                return NotificationOutcome.Declined;

            foreach (var message in messages)
            {
                await sender.SendAsync(recipient.ConnectionId, message, deadline.Token)
                    .ConfigureAwait(false);
            }

            return NotificationOutcome.Delivered;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            // Including the timeout, which reaches here as a cancellation of the linked token
            // while the caller's own token is still fine -- so the two are told apart in what is
            // logged rather than merged into one word.
            //
            // Logged here because this is where the exception still exists: the outcome that
            // travels onward is a single word, and a summary nobody reads is not somewhere a
            // reason can survive.
            //
            // Both halves of the condition matter. A deadline that has passed is not on its own
            // proof that the deadline is what happened: a handler that ignores the token can
            // throw something real a moment later, and matching on the clock alone would file
            // that as a timeout and drop the exception with it.
            if (error is OperationCanceledException && deadline.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Notification for character {CharacterId} ran out of its {Timeout} before it "
                        + "was delivered.",
                    recipient.CharacterId.Value,
                    options.PerRecipientTimeout);
            }
            else
            {
                logger.LogWarning(
                    error,
                    "Notification for character {CharacterId} failed.",
                    recipient.CharacterId.Value);
            }

            return NotificationOutcome.Failed;
        }
    }

    /// <summary>Is this still their connection?</summary>
    /// <remarks>
    /// Both halves are the question. A character with no session has logged out; a character whose
    /// session is now on a different connection has come back on a new one. Neither of them is the
    /// recipient this publish resolved to, which is what makes them the same answer -- and asking
    /// before composing rather than after is why neither costs a handler call.
    /// </remarks>
    private bool IsStillHere(NotificationRecipient recipient) =>
        sessions.FindByCharacter(recipient.CharacterId) is { } session
        && session.ConnectionId == recipient.ConnectionId;
}
