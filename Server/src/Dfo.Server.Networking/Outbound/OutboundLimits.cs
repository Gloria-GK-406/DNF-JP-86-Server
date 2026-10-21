namespace Dfo.Server.Networking.Outbound;

/// <summary>The sender's limits (RQ-033, RQ-036).</summary>
public static class OutboundLimits
{
    /// <summary>
    /// Largest outbox one CMD can register for one connection: a SELECT_CHARACTER with every growing
    /// list at its capacity bound, 450,680 B of bodies plus 237 frames × 15 B. The per-frame-type
    /// inputs are pinned by <c>OutboundQueueLimitTests</c>; the derivation and its sources are in
    /// <c>.rks-agent/tasks/serial-gameplay-executor/verification/measurement/selection-outbound-bound.md</c>.
    /// Measured on a new character: 39,285 B.
    /// </summary>
    public const long SelectionOutboundBound = 454_235;

    /// <summary>Per-connection queue limit in encoded bytes: at least twice <see cref="SelectionOutboundBound"/>.</summary>
    public const long QueueByteLimit = 1024 * 1024;

    /// <summary>Today's per-recipient deadline (<c>NotificationDeliveryOptions.Default</c>).</summary>
    public static TimeSpan DeliveryTimeout { get; } = TimeSpan.FromSeconds(2);

    /// <summary>How long an ordered entry waits for its predecessor once everything before that has settled.</summary>
    public static TimeSpan OrderedWaitLimit { get; } = TimeSpan.FromSeconds(2);
}

public sealed record OutboundSendOptions(long QueueByteLimit, TimeSpan DeliveryTimeout, TimeSpan OrderedWaitLimit)
{
    public static OutboundSendOptions Default { get; } =
        new(OutboundLimits.QueueByteLimit, OutboundLimits.DeliveryTimeout, OutboundLimits.OrderedWaitLimit);
}
