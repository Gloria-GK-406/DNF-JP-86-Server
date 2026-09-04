using Dfo.Server.Data.Networking;

namespace Dfo.Server.Abstractions.Notifications;

/// <summary>Something that happened, worth telling a group of characters about.</summary>
/// <remarks>
/// A notification names itself as its own type argument -- <c>PvpParticipantPublished :
/// INotification&lt;PvpParticipantPublished&gt;</c> -- and that is the whole reason the parameter
/// exists. It shuts the one door a plain marker interface leaves open: under
/// <c>where T : class, INotification</c> the contract itself satisfies the constraint, so
/// <c>PublishAsync&lt;INotification&gt;(audience, thing, ct)</c> compiles, the handler lookup then
/// misses at run time, and a notification meant to go out goes nowhere -- quietly, because nothing
/// on this path is required to succeed. Self-referential, that call is CS0311 instead.
/// <para>
/// What it does not do is make the concrete type the only possible argument. A notification
/// declared on top of a non-sealed base could still be widened to that base, and this contract
/// cannot stop it -- sealing the notification is what leaves no base to widen to, and that is a
/// choice each one makes for itself. The constraint closes the other door, the one that stands
/// open however a notification is declared.
/// </para>
/// <para>
/// This is the same care <c>ICommand&lt;TResult&gt;</c> takes by staying invariant in its result,
/// for the same reason: the binding between a payload and the code that consumes it is decided
/// where it is written, not where it fails.
/// </para>
/// </remarks>
public interface INotification<TSelf>
    where TSelf : INotification<TSelf>;

/// <summary>Works out what one recipient should be told, for one kind of notification.</summary>
/// <remarks>
/// Per recipient, because two people are not owed the same frame: what a notification means to
/// somebody depends on what they can already see. Returning nothing is how a handler says "not
/// this one" -- it is a decision, not a failure, and delivery counts it as such.
/// <para>
/// A handler reads and does not write. Writing belongs to the command that caused the
/// notification, which has already committed by the time this runs. The rule keeps three things
/// true at once: a notification never takes the lock a player's own action is waiting on, never
/// drags a fan-out's worth of transactions behind it, and costs what it looks like it costs.
/// Where it shows is the injection surface: a handler is given domain services and nothing that
/// settles a write -- no store, no table, no command bus.
/// </para>
/// </remarks>
public interface INotificationHandler<TNotification>
    where TNotification : class, INotification<TNotification>
{
    /// <summary>The messages this recipient should receive, or nothing to leave them out.</summary>
    ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        TNotification notification,
        CancellationToken cancellationToken);
}
