using Dfo.Server.Data.Networking;

namespace Dfo.Server.Abstractions.Execution;

/// <summary>
/// RQ-034. Only matters inside an ordered group: once a Required entry of the group does not end
/// delivered, every later entry of that group is skipped (today's "required failure stops the plan").
/// A BestEffort failure lets the group go on. Outside a group both classes behave the same.
/// </summary>
public enum OutboundDelivery
{
    Required,
    BestEffort,
}

/// <summary>
/// Identity of one ordered group (RQ-033); compared by reference. Create one per CMD and never reuse
/// or keep it: the sender remembers a group's last sequence and its chain of entries for as long as
/// the group object lives.
/// </summary>
public sealed class OutboundOrderGroup(string name)
{
    public string Name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("An ordered group needs a name.", nameof(name))
        : name;

    public override string ToString() => Name;
}

/// <summary>Sequences of one group must be submitted strictly increasing.</summary>
public readonly record struct OutboundOrder(OutboundOrderGroup Group, int Sequence);

/// <summary>
/// One immutable unit handed from the outbox to the sender: a typed batch for one exact connection,
/// or a request to close it. The batch keeps <c>SendBatchAsync</c> semantics: it is encoded whole
/// before any of it is delivered, and it goes only to the connection context it was registered for.
/// </summary>
public sealed class OutboundEntry
{
    private OutboundEntry(
        NetworkConnectionContext connection,
        IReadOnlyList<IServerNetworkMessage> messages,
        OutboundDelivery delivery,
        OutboundOrder? order,
        bool isClose)
    {
        Connection = connection;
        Messages = messages;
        Delivery = delivery;
        Order = order;
        IsClose = isClose;
    }

    public NetworkConnectionContext Connection { get; }

    public IReadOnlyList<IServerNetworkMessage> Messages { get; }

    public OutboundDelivery Delivery { get; }

    public OutboundOrder? Order { get; }

    public bool IsClose { get; }

    public static OutboundEntry Batch(
        NetworkConnectionContext connection,
        IReadOnlyList<IServerNetworkMessage> messages,
        OutboundDelivery delivery = OutboundDelivery.Required,
        OutboundOrder? order = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(messages);
        if (!Enum.IsDefined(delivery))
            throw new ArgumentOutOfRangeException(nameof(delivery));
        var copy = messages.ToArray();
        if (copy.Length == 0)
            throw new ArgumentException("An outbound batch needs at least one message.", nameof(messages));
        if (Array.IndexOf(copy, null) >= 0)
            throw new ArgumentException("An outbound batch cannot contain a missing message.", nameof(messages));
        ValidateOrder(order);
        return new OutboundEntry(connection, copy, delivery, order, isClose: false);
    }

    /// <summary>Closes the connection after everything registered for it before this request.</summary>
    public static OutboundEntry Close(NetworkConnectionContext connection, OutboundOrder? order = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ValidateOrder(order);
        return new OutboundEntry(connection, [], OutboundDelivery.Required, order, isClose: true);
    }

    private static void ValidateOrder(OutboundOrder? order)
    {
        if (order is { } value && value.Group is null)
            throw new ArgumentException("An ordered entry needs its group.", nameof(order));
    }
}

/// <summary>
/// The hand-off from the outbox to the transport (RQ-037): <see cref="Submit"/> enqueues the entries
/// in order and returns at once; it never waits for delivery and never runs delivery on the caller.
/// </summary>
public interface IOutboundSender
{
    void Submit(IReadOnlyList<OutboundEntry> entries);
}

public enum OutboundOutcome
{
    /// <summary>Handed to the transport and out of its send pipe (spec "交付").</summary>
    Delivered,

    /// <summary>A close request was carried out after everything before it.</summary>
    Closed,

    /// <summary>The registered connection was replaced or already closed; nothing was sent (RQ-035).</summary>
    Stale,

    /// <summary>Dropped because its connection had already been failed or closed by an earlier entry.</summary>
    Discarded,

    /// <summary>Not delivered because an earlier Required entry of its ordered group did not deliver.</summary>
    Skipped,

    /// <summary>The batch could not be encoded; none of it was sent and the connection was closed.</summary>
    EncodingFailed,

    /// <summary>The transport failed; the connection was closed (RQ-036).</summary>
    Failed,

    /// <summary>Delivery, or a successor's wait for it, exceeded its limit; the connection was closed.</summary>
    TimedOut,

    /// <summary>Admitting it would exceed the connection's queue byte limit; the connection was closed.</summary>
    OverLimit,
}

public sealed record OutboundDeliveryReport(OutboundEntry Entry, OutboundOutcome Outcome, Exception? Error = null);

/// <summary>
/// Observes delivery from the sender's own loops, off the executor. Exceptions are logged and ignored.
/// </summary>
public interface IOutboundDeliveryObserver
{
    void OnEntryCompleted(OutboundDeliveryReport report);

    /// <summary>The sender closed <paramref name="connection"/> because of a failure (not a close request).</summary>
    void OnConnectionFailed(NetworkConnectionContext connection, OutboundOutcome reason);
}
