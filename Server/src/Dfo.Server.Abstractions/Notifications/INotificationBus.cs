using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Notifications;

/// <summary>What became of one recipient in one publish.</summary>
/// <remarks>
/// Four, and the two that are not failures are the reason there are four. A recipient who was told
/// nothing because the handler had nothing for them, and a recipient who had already left, both
/// look exactly like a silent failure from outside -- and a summary that could not tell them apart
/// would be a summary nobody could act on.
/// </remarks>
public enum NotificationOutcome
{
    /// <summary>
    /// The messages were composed, encoded and registered for their connection: submitted for sending,
    /// or dropped because the connection was no longer registered (F-83).
    /// </summary>
    /// <remarks>
    /// Registration does not promise transport or client receipt. The job's outbox hands them to
    /// the sender when the job ends, and nothing here waits for the transport: a connection that then
    /// fails to take them is closed by the sender, and its teardown is the lifecycle CMD that follows.
    /// </remarks>
    Registered,

    /// <summary>The handler had nothing for this recipient. A decision, not a fault.</summary>
    Declined,

    /// <summary>
    /// By the time it was their turn, the connection the audience resolved to was no longer
    /// theirs. Not retried.
    /// </summary>
    /// <remarks>
    /// Two ways to arrive here, and the second is the one worth stating: they logged out, or they
    /// came back on a different connection. Both are this, and neither is retried.
    /// <para>
    /// The reconnected case looks unfair -- somebody is online and hears nothing -- and it is
    /// still the right answer. This publish resolved its recipients at one instant, and that
    /// resolution is what it is about; the session on the other end of it is gone. Delivering to
    /// the replacement instead would race a frame from a publish that began before that session
    /// existed against its entry sequence, which builds its state from the ground up and is the
    /// authority on it.
    /// </para>
    /// <para>
    /// What they miss depends on what the notification was. One that describes a state has already
    /// been superseded -- their entry hands them the current version of the same thing. One that
    /// announces an event is simply not heard, and that is the cost of this choice rather than an
    /// argument against it: this whole path is best effort, and racing a fresh entry sequence with
    /// an older frame is the worse of the two failures.
    /// </para>
    /// </remarks>
    Gone,

    /// <summary>Composing or encoding threw. Logged as a warning; not retried (VC-5).</summary>
    Failed,
}

public sealed record NotificationAttempt(CharacterId CharacterId, NotificationOutcome Outcome)
{
    public bool RegistrationAccepted { get; init; }
}

/// <summary>What one publish reached, and what became of each recipient.</summary>
/// <remarks>
/// Said in characters, like the audience it came from, so a caller never has to learn what a
/// connection is to read its own result.
/// <para>
/// The attempts are the recipients the audience resolved to -- the people who were here. Somebody
/// the audience meant but who was not online is not in this list, and cannot be: an audience is
/// not always a list of names, so there is no general answer to "who did you mean but miss".
/// </para>
/// </remarks>
public sealed record NotificationDelivery(IReadOnlyList<NotificationAttempt> Attempts)
{
    public IEnumerable<CharacterId> With(NotificationOutcome outcome) => Attempts
        .Where(attempt => attempt.Outcome == outcome)
        .Select(attempt => attempt.CharacterId);
}

/// <summary>Tells a group of characters that something happened.</summary>
/// <remarks>
/// The notification's type comes from the argument, so a publish cannot name a type its payload is
/// not. See <see cref="INotification{TSelf}"/> for what that costs and what it buys.
/// <para>
/// Best effort, and deliberately so. A publish that reached nobody is not an error: this runs
/// after the command that changed something has already committed, so what a recipient would have
/// been told is true whether or not they hear it, and they will see it when they next look. What
/// the caller gets back is a description of what happened, not a promise about it.
/// </para>
/// </remarks>
public interface INotificationBus
{
    ValueTask<NotificationDelivery> PublishAsync<TNotification>(
        NotificationAudience audience,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : class, INotification<TNotification>;
}
