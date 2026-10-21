using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Execution.Outbox;

/// <summary>
/// Gives every gameplay job its own outbox (attached to the job token, not to a DI scope: during the
/// transition each inner command opens a scope of its own) and is the job's end point that flushes it.
/// </summary>
public sealed class GameplayOutboxes(IOutboundSender sender, ILogger<GameplayOutbox> logger)
{
    private readonly GameplayJobSlot<GameplayOutbox> slot = new();

    /// <summary>The running job's outbox; off the executor this is a program error.</summary>
    public GameplayOutbox Current => slot.GetOrCreate("Registering outbound work", () => new GameplayOutbox(logger));

    /// <summary>
    /// Runs <paramref name="body"/> as the rest of the current job and then flushes the job's outbox
    /// to the sender (RQ-031 (a)-(c)). When the body throws, <paramref name="source"/> is closed after
    /// everything that survives the flush and the exception is rethrown.
    /// </summary>
    public async ValueTask<T> RunAsync<T>(
        NetworkConnectionContext? source,
        Func<CancellationToken, ValueTask<T>> body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var outbox = Current;
        T result;
        try
        {
            result = await body(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The job's own failure is the one the caller sees; a flush failure is only logged.
            Flush(outbox, failed: true, source);
            throw;
        }

        if (Flush(outbox, failed: false, source) is { } refused)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(refused);
        return result;
    }

    /// <summary>
    /// Hands the outbox to the sender. If the sender refuses it (a program error), the close requests
    /// still go out on their own, unordered, and the source is closed as for a failed CMD (DR-15).
    /// </summary>
    private Exception? Flush(GameplayOutbox outbox, bool failed, NetworkConnectionContext? source)
    {
        var entries = outbox.Complete(failed, source);
        try
        {
            sender.Submit(entries);
            return null;
        }
        catch (Exception refused)
        {
            logger.LogError(refused, "The sender refused a gameplay job's outbox; only its close requests are sent");
            var closes = entries.Where(entry => entry.IsClose).Select(entry => OutboundEntry.Close(entry.Connection)).ToList();
            if (!failed && source is not null)
                closes.Add(OutboundEntry.Close(source));
            sender.Submit(closes);
            return refused;
        }
    }

    public async ValueTask RunAsync(
        NetworkConnectionContext? source,
        Func<CancellationToken, ValueTask> body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await RunAsync<bool>(source, async ct =>
        {
            await body(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Where one CMD registers its outbound entries and close requests (RQ-030). Entries registered in a
/// work unit live or die with it; entries outside a unit and close requests always survive. Pending
/// compositions in a unit are composed when the unit commits, before control returns to the CMD.
/// </summary>
/// <remarks>Used only from its own job, so it takes no locks.</remarks>
public sealed class GameplayOutbox
{
    private readonly ILogger logger;
    private readonly List<Slot> slots = [];
    private GameplayOutboxUnit? open;
    private bool completed;

    internal GameplayOutbox(ILogger logger) => this.logger = logger;

    public GameplayOutboxUnit BeginUnit()
    {
        EnsureWritable();
        if (open is not null)
            throw new InvalidOperationException("A work unit is already open; units run one after another.");
        open = new GameplayOutboxUnit(this);
        return open;
    }

    public void Register(OutboundEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        EnsureWritable();
        // Close requests never belong to a unit (plan, R-1): they are always carried out.
        slots.Add(new Slot(entry.IsClose ? null : open, entry, null));
    }

    public void RequestClose(NetworkConnectionContext connection) => Register(OutboundEntry.Close(connection));

    /// <summary>
    /// Registers entries composed per recipient. Inside a unit they are composed when it commits;
    /// outside one, now. A recipient whose composition fails loses only its own entries (RQ-031(d)).
    /// </summary>
    public async ValueTask<OutboxComposition<TRecipient>> ComposeAsync<TRecipient>(
        IReadOnlyList<TRecipient> recipients,
        Func<TRecipient, CancellationToken, ValueTask<IReadOnlyList<OutboundEntry>>> compose,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        ArgumentNullException.ThrowIfNull(compose);
        EnsureWritable();
        var composition = new OutboxComposition<TRecipient>([.. recipients], compose, logger);
        slots.Add(new Slot(open, null, composition));
        if (open is null)
            await composition.ComposeAsync(cancellationToken).ConfigureAwait(false);
        return composition;
    }

    internal async ValueTask CommitAsync(GameplayOutboxUnit unit, CancellationToken cancellationToken)
    {
        foreach (var slot in slots)
        {
            if (ReferenceEquals(slot.Unit, unit) && slot.Composition is { } composition)
                await composition.ComposeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal void Close(GameplayOutboxUnit unit)
    {
        if (ReferenceEquals(open, unit))
            open = null;
    }

    /// <summary>Ends the outbox: what survives, in registration order, then the source close on failure.</summary>
    internal IReadOnlyList<OutboundEntry> Complete(bool failed, NetworkConnectionContext? source)
    {
        if (completed)
            throw new InvalidOperationException("The outbox has already been flushed.");
        completed = true;
        if (open is { } unfinished)
        {
            if (!failed)
                logger.LogWarning("A work unit was still open when its gameplay job ended; its entries are dropped");
            unfinished.Abandon();
        }

        var flushed = new List<OutboundEntry>(slots.Count + 1);
        foreach (var slot in slots)
        {
            if (slot.Unit is { State: not GameplayOutboxUnitState.Committed })
                continue;
            if (slot.Entry is { } entry)
                flushed.Add(entry);
            else
                flushed.AddRange(slot.Composition!.ComposedEntries);
        }

        if (failed && source is not null)
            flushed.Add(OutboundEntry.Close(source));
        return flushed;
    }

    private void EnsureWritable()
    {
        if (!GameplayJob.IsOnExecutor)
            throw new GameplayOffExecutorException("Registering outbound work");
        if (completed)
            throw new InvalidOperationException("The outbox of a finished gameplay job takes no more work.");
    }

    private sealed record Slot(GameplayOutboxUnit? Unit, OutboundEntry? Entry, IOutboxComposition? Composition);
}

public enum GameplayOutboxUnitState
{
    Open,
    Committed,
    RolledBack,
    Uncertain,
}

/// <summary>
/// One work unit's share of the outbox. Its owner (T-006: the domain transaction) reports how the
/// unit ended; disposing an open unit counts as a rollback.
/// </summary>
public sealed class GameplayOutboxUnit : IDisposable
{
    private readonly GameplayOutbox outbox;

    internal GameplayOutboxUnit(GameplayOutbox outbox) => this.outbox = outbox;

    public GameplayOutboxUnitState State { get; private set; }

    /// <summary>The unit's transaction has committed: compose its pending entries now (RQ-031(0)).</summary>
    public async ValueTask CommitAsync(CancellationToken cancellationToken)
    {
        EnsureOpen();
        try
        {
            await outbox.CommitAsync(this, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            End(GameplayOutboxUnitState.Uncertain);
            throw;
        }

        End(GameplayOutboxUnitState.Committed);
    }

    public void Rollback()
    {
        EnsureOpen();
        End(GameplayOutboxUnitState.RolledBack);
    }

    /// <summary>The commit itself did not report an outcome; the unit's entries are dropped.</summary>
    public void MarkUncertain()
    {
        EnsureOpen();
        End(GameplayOutboxUnitState.Uncertain);
    }

    public void Dispose()
    {
        if (State == GameplayOutboxUnitState.Open)
            End(GameplayOutboxUnitState.RolledBack);
    }

    internal void Abandon() => End(GameplayOutboxUnitState.Uncertain);

    private void EnsureOpen()
    {
        if (State != GameplayOutboxUnitState.Open)
            throw new InvalidOperationException($"The work unit has already ended ({State}).");
    }

    private void End(GameplayOutboxUnitState state)
    {
        State = state;
        outbox.Close(this);
    }
}

internal interface IOutboxComposition
{
    IEnumerable<OutboundEntry> ComposedEntries { get; }

    ValueTask ComposeAsync(CancellationToken cancellationToken);
}

/// <summary>One registered per-recipient composition; <see cref="Results"/> is filled once it is composed.</summary>
public sealed class OutboxComposition<TRecipient> : IOutboxComposition
{
    private readonly TRecipient[] recipients;
    private readonly Func<TRecipient, CancellationToken, ValueTask<IReadOnlyList<OutboundEntry>>> compose;
    private readonly ILogger logger;
    private OutboxRecipientResult<TRecipient>[] results = [];

    internal OutboxComposition(
        TRecipient[] recipients,
        Func<TRecipient, CancellationToken, ValueTask<IReadOnlyList<OutboundEntry>>> compose,
        ILogger logger)
    {
        this.recipients = recipients;
        this.compose = compose;
        this.logger = logger;
    }

    public bool IsComposed { get; private set; }

    public IReadOnlyList<OutboxRecipientResult<TRecipient>> Results => results;

    IEnumerable<OutboundEntry> IOutboxComposition.ComposedEntries => results.SelectMany(result => result.Entries);

    async ValueTask IOutboxComposition.ComposeAsync(CancellationToken cancellationToken) =>
        await ComposeAsync(cancellationToken).ConfigureAwait(false);

    internal async ValueTask ComposeAsync(CancellationToken cancellationToken)
    {
        var composed = new OutboxRecipientResult<TRecipient>[recipients.Length];
        for (var i = 0; i < recipients.Length; i++)
            composed[i] = await ComposeOneAsync(recipients[i], cancellationToken).ConfigureAwait(false);
        results = composed;
        IsComposed = true;
    }

    private async ValueTask<OutboxRecipientResult<TRecipient>> ComposeOneAsync(
        TRecipient recipient, CancellationToken cancellationToken)
    {
        try
        {
            var entries = await compose(recipient, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The composition produced no entry list.");
            OutboundEntry[] owned = [.. entries];
            if (owned.Any(entry => entry is null || entry.IsClose))
                throw new InvalidDataException("A composition may produce only message batches.");
            return new OutboxRecipientResult<TRecipient>(recipient, owned, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            logger.LogWarning(failure,
                "Outbound composition for recipient {Recipient} failed; only its entries are dropped", recipient);
            return new OutboxRecipientResult<TRecipient>(recipient, [], failure);
        }
    }
}

public sealed record OutboxRecipientResult<TRecipient>(
    TRecipient Recipient,
    IReadOnlyList<OutboundEntry> Entries,
    Exception? Failure)
{
    public bool Composed => Failure is null;
}
