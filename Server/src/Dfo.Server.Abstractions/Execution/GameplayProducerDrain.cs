namespace Dfo.Server.Abstractions.Execution;

/// <summary>
/// A producer of gameplay work that must be quiet before the executor stops (RQ-009). The executor's
/// lifetime calls every drain, in registration order, after the host has stopped its services and
/// before it stops the executor.
/// </summary>
/// <remarks>Not a <c>*DomainService</c>: command handlers cannot inject it.</remarks>
public interface IGameplayProducerDrain
{
    /// <summary>
    /// Returns when this producer makes no new work and everything it started has been enqueued or
    /// has completed.
    /// </summary>
    ValueTask DrainAsync(CancellationToken cancellationToken);
}
