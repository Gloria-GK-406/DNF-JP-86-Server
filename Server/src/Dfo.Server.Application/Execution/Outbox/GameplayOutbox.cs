using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Execution.Outbox;

/// <summary>
/// Gives every gameplay job its own outbox (attached to the job token, not to a DI scope: each inner
/// command opens a scope of its own) and flushes it to the sender when the job ends, on whichever
/// executor runs the job (plan "发件箱的归属").
/// </summary>
public sealed class GameplayOutboxes
{
    internal const string RegisterEntry = "GameplayOutbox.Register";

    private readonly GameplayJobSlot<GameplayOutbox> slot = new();
    private readonly IOutboundSender? sender;
    private readonly GameplayCloseCauses? causes;
    private readonly ILogger<GameplayOutbox> logger;

    /// <summary>Without a sender (a container without networking) flushed entries are dropped as stale.</summary>
    public GameplayOutboxes(ILogger<GameplayOutbox> logger) => this.logger = logger;

    public GameplayOutboxes(IOutboundSender sender, ILogger<GameplayOutbox> logger)
    {
        this.sender = sender;
        this.logger = logger;
    }

    internal GameplayOutboxes(GameplayCloseCauses causes, ILogger<GameplayOutbox> logger)
        : this(logger) => this.causes = causes;

    internal GameplayOutboxes(IOutboundSender sender, GameplayCloseCauses causes, ILogger<GameplayOutbox> logger)
        : this(sender, logger) => this.causes = causes;

    /// <summary>
    /// The running job's outbox. Off the executor this is a program error, reported to the gameplay
    /// assertion source before it is thrown (RQ-061, the outbox half).
    /// </summary>
    public GameplayOutbox Current
    {
        get
        {
            GameplayWriteAssertion.Writing(RegisterEntry);
            return slot.GetOrCreate(RegisterEntry, Create);
        }
    }

    /// <summary>The running job's outbox, or null off the executor; for what is not a registration.</summary>
    internal GameplayOutbox? CurrentOrNull => GameplayJob.IsOnExecutor ? slot.GetOrCreate(RegisterEntry, Create) : null;

    private GameplayOutbox Create()
    {
        var outbox = new GameplayOutbox(logger);
        // Every outbox is flushed exactly once, when its job ends and before the job's caller sees the
        // result (RQ-005): "completed" for a dispatcher means handed to the sender.
        GameplayJob.OnEnd("Flushing the gameplay outbox", end => Flush(outbox, end));
        return outbox;
    }

    /// <summary>
    /// Hands the outbox to the sender (RQ-031 (a)-(c)). If the sender refuses it (a program error), the
    /// close requests still go out on their own, unordered, and the source is closed as for a failed CMD
    /// (DR-15); the refusal then fails a job that had succeeded.
    /// </summary>
    private Exception? Flush(GameplayOutbox outbox, GameplayJobEnd end)
    {
        var entries = outbox.Complete(end.Failed, end.Source);
        if (entries.Count == 0)
            return null;
        if (sender is null)
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("No outbound sender is registered; {Count} outbound entries are dropped", entries.Count);
            return null;
        }

        // Before the sender has them, so the close callback always finds its cause (RQ-050). The source close
        // added for a failed CMD, and the fallback one below, are this CMD's too (TASK-29 ruling 6(a)).
        var chain = GameplayJob.Current;
        foreach (var entry in entries)
        {
            if (outbox.IsCausedClose(entry)) RecordCause(entry.Connection, chain);
        }

        try
        {
            sender.Submit(entries);
            return null;
        }
        catch (Exception refused)
        {
            logger.LogError(refused, "The sender refused a gameplay job's outbox; only its close requests are sent");
            var closes = entries.Where(entry => entry.IsClose).Select(entry => OutboundEntry.Close(entry.Connection)).ToList();
            if (!end.Failed && end.Source is not null)
            {
                closes.Add(OutboundEntry.Close(end.Source));
                RecordCause(end.Source, chain);
            }
            sender.Submit(closes);
            return refused;
        }
    }

    private void RecordCause(NetworkConnectionContext connection, GameplayCallChain? chain)
    {
        if (causes is not null && chain is { } cause) causes.Record(connection, cause);
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
    private OrderedScope? ordered;
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
        // Close requests never belong to a unit (plan, R-1) nor to an ordered group: they are always carried out.
        slots.Add(new Slot(entry.IsClose ? null : open, entry, null, entry.IsClose ? null : Mark(entry)));
    }

    public void RequestClose(NetworkConnectionContext connection) => Register(OutboundEntry.Close(connection));

    /// <summary>
    /// A close that is not this CMD's request, so the teardown it leads to is a new root: the depth guard's
    /// (RQ-053(a)). Carried out like any other close.
    /// </summary>
    internal void RequestCloseWithoutCause(NetworkConnectionContext connection)
    {
        EnsureWritable();
        slots.Add(new Slot(null, OutboundEntry.Close(connection), null, null, WithoutCause: true));
    }

    /// <summary>Whether <paramref name="entry"/> is a close this CMD asked for, which makes it the teardown's cause.</summary>
    internal bool IsCausedClose(OutboundEntry entry) =>
        entry.IsClose && !slots.Exists(slot => slot.WithoutCause && ReferenceEquals(slot.Entry, entry));

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
        slots.Add(new Slot(open, null, composition, ordered?.Mark()));
        if (open is null)
            await composition.ComposeAsync(cancellationToken).ConfigureAwait(false);
        return composition;
    }

    /// <summary>
    /// Opens an ordered group (<see cref="IOutboundOrdering"/>); a scope opened inside another joins it.
    /// </summary>
    internal IOutboundOrderingScope OpenGroup(string name, OutboundDelivery delivery)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(delivery))
            throw new ArgumentOutOfRangeException(nameof(delivery));
        EnsureWritable();
        if (ordered is { } outer)
            return new NestedScope(outer, delivery);
        ordered = new OrderedScope(this, new OutboundOrderGroup(name), delivery);
        return ordered;
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

    /// <summary>
    /// Ends the outbox: what survives, in registration order, then the source close on failure. Entries
    /// registered in an ordered scope get their group sequence here, in that same order.
    /// </summary>
    internal IReadOnlyList<OutboundEntry> Complete(bool failed, NetworkConnectionContext? source)
    {
        if (completed)
            throw new InvalidOperationException("The outbox has already been flushed.");
        completed = true;
        ordered = null;
        if (open is { } unfinished)
        {
            if (!failed)
                logger.LogWarning("A work unit was still open when its gameplay job ended; its entries are dropped");
            unfinished.Abandon();
        }

        var flushed = new List<OutboundEntry>(slots.Count + 1);
        var sequences = new Dictionary<OutboundOrderGroup, int>(ReferenceEqualityComparer.Instance);
        foreach (var slot in slots)
        {
            if (slot.Unit is { State: not GameplayOutboxUnitState.Committed })
                continue;
            if (slot.Entry is { } entry)
                flushed.Add(Ordered(entry, slot.Group, sequences));
            else
                flushed.AddRange(slot.Composition!.ComposedEntries.Select(composed => Ordered(composed, slot.Group, sequences)));
        }

        if (failed && source is not null)
            flushed.Add(OutboundEntry.Close(source));
        return flushed;
    }

    private static OutboundEntry Ordered(
        OutboundEntry entry, GroupMark? mark, Dictionary<OutboundOrderGroup, int> sequences)
    {
        if (mark is not { } group || entry.IsClose)
            return entry;
        if (entry.Order is not null)
            throw new InvalidOperationException("An entry that carries its own order cannot join an ordered scope.");
        var sequence = sequences.TryGetValue(group.Group, out var last) ? last + 1 : 1;
        sequences[group.Group] = sequence;
        // A BestEffort entry stays BestEffort: an explicit delivery's secondary targets never break the group.
        var delivery = entry.Delivery == OutboundDelivery.Required ? group.Delivery : OutboundDelivery.BestEffort;
        return entry.InGroup(new OutboundOrder(group.Group, sequence), delivery);
    }

    private GroupMark? Mark(OutboundEntry entry)
    {
        if (ordered is not { } scope)
            return null;
        if (entry.Order is not null)
            throw new InvalidOperationException("An entry that carries its own order cannot join an ordered scope.");
        return scope.Mark();
    }

    private void EnsureWritable()
    {
        GameplayWriteAssertion.Writing(GameplayOutboxes.RegisterEntry);
        if (completed)
            throw new InvalidOperationException("The outbox of a finished gameplay job takes no more work.");
    }

    private void EndGroup(OrderedScope scope)
    {
        if (ReferenceEquals(ordered, scope))
            ordered = null;
    }

    private sealed record Slot(GameplayOutboxUnit? Unit, OutboundEntry? Entry, IOutboxComposition? Composition, GroupMark? Group,
        bool WithoutCause = false);

    private readonly record struct GroupMark(OutboundOrderGroup Group, OutboundDelivery Delivery);

    private sealed class OrderedScope(GameplayOutbox owner, OutboundOrderGroup group, OutboundDelivery delivery)
        : IOutboundOrderingScope
    {
        public OutboundDelivery Delivery
        {
            get => delivery;
            set => delivery = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }

        public GroupMark Mark() => new(group, delivery);

        public void Dispose() => owner.EndGroup(this);
    }

    private sealed class NestedScope : IOutboundOrderingScope
    {
        private readonly OrderedScope outer;
        private readonly OutboundDelivery restored;
        private bool disposed;

        public NestedScope(OrderedScope outer, OutboundDelivery delivery)
        {
            this.outer = outer;
            restored = outer.Delivery;
            outer.Delivery = delivery;
        }

        public OutboundDelivery Delivery
        {
            get => outer.Delivery;
            set => outer.Delivery = value;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            outer.Delivery = restored;
        }
    }
}

public enum GameplayOutboxUnitState
{
    Open,
    Committed,
    RolledBack,
    Uncertain,
}

/// <summary>
/// One work unit's share of the outbox. Its owner, the writable domain transaction, reports how the
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
