using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Networking.Outbound;

/// <summary>
/// The sending side of the outbox (RQ-032 to RQ-037). <see cref="Submit"/> only appends to one
/// admission queue; an admission loop encodes each entry and appends it to its connection's queue,
/// and every connection drains its own queue in its own loop. None of these loops carries a job
/// token, so nothing here runs on, or is waited for by, the executor.
/// </summary>
/// <remarks>
/// Ordered groups cannot deadlock: an entry only waits for entries submitted before it, every queue
/// is FIFO in submission order, and <see cref="Submit"/> is serialised, so waits always point at
/// strictly earlier submissions. The wait limit cuts any wait that still runs long.
/// </remarks>
internal sealed class OutboundSender : IOutboundSender, IAsyncDisposable
{
    private readonly IOutboundTransport transport;
    private readonly IOutboundEncoder encoder;
    private readonly IOutboundDeliveryObserver[] observers;
    private readonly ILogger<OutboundSender> logger;
    private readonly OutboundSendOptions options;
    private readonly Channel<OutboundEntry[]> submissions = Channel.CreateUnbounded<OutboundEntry[]>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    private readonly ConditionalWeakTable<NetworkConnectionContext, ConnectionQueue> queues = new();
    private readonly ConditionalWeakTable<OutboundOrderGroup, GroupState> groups = new();
    private readonly Lock submitLock = new();
    private Task? admission;

    public OutboundSender(
        IOutboundTransport transport,
        IOutboundEncoder encoder,
        IEnumerable<IOutboundDeliveryObserver> observers,
        ILogger<OutboundSender> logger,
        OutboundSendOptions options)
    {
        this.transport = transport;
        this.encoder = encoder;
        this.observers = [.. observers];
        this.logger = logger;
        this.options = options;
    }

    public void Submit(IReadOnlyList<OutboundEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        OutboundEntry[] batch = [.. entries];
        lock (submitLock)
        {
            var sequences = new Dictionary<OutboundOrderGroup, int>(ReferenceEqualityComparer.Instance);
            foreach (var entry in batch)
            {
                ArgumentNullException.ThrowIfNull(entry, nameof(entries));
                if (entry.Order is not { } order)
                    continue;
                var last = sequences.TryGetValue(order.Group, out var pending)
                    ? pending
                    : groups.GetOrCreateValue(order.Group).LastSubmitted;
                if (order.Sequence <= last)
                    throw new ArgumentException(
                        $"Ordered group '{order.Group}' got sequence {order.Sequence} after {last}.", nameof(entries));
                sequences[order.Group] = order.Sequence;
            }

            if (!submissions.Writer.TryWrite(batch))
                throw new ObjectDisposedException(nameof(OutboundSender));
            foreach (var (group, sequence) in sequences)
                groups.GetOrCreateValue(group).LastSubmitted = sequence;
            admission ??= Detached(AdmitAsync);
        }
    }

    public async ValueTask DisposeAsync()
    {
        submissions.Writer.TryComplete();
        Task? loop;
        lock (submitLock)
            loop = admission;
        if (loop is not null)
            await loop.ConfigureAwait(false);
    }

    private static Task Detached(Func<Task> work)
    {
        using (ExecutionContext.SuppressFlow())
            return Task.Run(work);
    }

    private async Task AdmitAsync()
    {
        await foreach (var batch in submissions.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            foreach (var entry in batch)
            {
                try
                {
                    Admit(entry);
                }
                catch (Exception error)
                {
                    logger.LogCritical(error, "Admitting an outbound entry failed");
                }
            }
        }
    }

    private void Admit(OutboundEntry entry)
    {
        Item? predecessor = null;
        if (entry.Order is { } order)
        {
            var group = groups.GetOrCreateValue(order.Group);
            predecessor = group.LastAdmitted;
        }

        var queue = queues.GetValue(entry.Connection, context => new ConnectionQueue(this, context));
        byte[] frames = [];
        Exception? encodingError = null;
        if (!entry.IsClose)
        {
            try
            {
                frames = encoder.Encode(entry.Connection, entry.Messages);
            }
            catch (Exception error)
            {
                encodingError = error;
            }
        }

        var item = new Item(entry, queue, predecessor, frames, encodingError);
        if (entry.Order is { } admitted)
            groups.GetOrCreateValue(admitted.Group).LastAdmitted = item;
        queue.Enqueue(item);
    }

    private async Task ProcessAsync(ConnectionQueue queue, Item item, CancellationToken ending)
    {
        try
        {
            // A close request is carried out even when its group broke (RQ-031(c)).
            if (item.Predecessor is { } predecessor && !await PredecessorsDeliveredAsync(predecessor, ending).ConfigureAwait(false)
                && !item.Entry.IsClose)
            {
                Complete(item, OutboundOutcome.Skipped);
                return;
            }

            // In order with the rest of the queue: what was admitted before it still goes out first.
            if (item.EncodingError is { } encodingError)
            {
                queue.End(item, OutboundOutcome.EncodingFailed, encodingError, close: true);
                return;
            }

            var connection = transport.Resolve(item.Entry.Connection);
            if (connection is null)
            {
                queue.End(item, OutboundOutcome.Stale, null, close: false, remainder: OutboundOutcome.Stale);
                return;
            }

            if (item.Entry.IsClose)
            {
                await CloseAsync(connection).ConfigureAwait(false);
                queue.End(item, OutboundOutcome.Closed, null, close: false);
                return;
            }

            await DeliverAsync(queue, item, connection, ending).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ending.IsCancellationRequested)
        {
            // The queue was ended by somebody else, who completed this item.
        }
    }

    /// <summary>
    /// Waits for every unsettled entry up to <paramref name="predecessor"/>, earliest first, giving each
    /// <see cref="OutboundSendOptions.OrderedWaitLimit"/> from the moment the one before it settled;
    /// past that its connection is failed (RQ-033). The limit goes to the earliest unsettled entry, not
    /// only the adjacent one, because a middle entry can finish without ever waiting (discarded, over
    /// the limit, dropped with its queue). Returns whether no Required entry up to the predecessor failed.
    /// </summary>
    private async Task<bool> PredecessorsDeliveredAsync(Item predecessor, CancellationToken ending)
    {
        var unsettled = new Stack<Item>();
        for (var item = predecessor; item is not null && !item.Settled.IsCompleted; item = item.Predecessor)
            unsettled.Push(item);
        while (unsettled.TryPop(out var item))
        {
            if (item.Done.Task.IsCompleted)
                continue;
            try
            {
                await item.Done.Task.WaitAsync(options.OrderedWaitLimit, ending).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                item.Queue.End(item, OutboundOutcome.TimedOut,
                    new TimeoutException("An ordered successor waited longer than the limit for this entry."), close: true);
            }
        }

        return !predecessor.BreaksGroup();
    }

    private async Task DeliverAsync(ConnectionQueue queue, Item item, IOutboundConnection connection, CancellationToken ending)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ending);
        deadline.CancelAfter(options.DeliveryTimeout);
        Task send;
        try
        {
            // End cancels before it completes (and releases) the item, so released frames are never sent.
            var frames = item.Frames;
            if (ending.IsCancellationRequested)
                return;
            send = connection.SendAsync(frames, deadline.Token).AsTask();
        }
        catch (Exception error)
        {
            queue.End(item, OutboundOutcome.Failed, error, close: true);
            return;
        }

        try
        {
            // Also bounded here in case the transport ignores its token.
            await send.WaitAsync(options.DeliveryTimeout, ending).ConfigureAwait(false);
            Complete(item, OutboundOutcome.Delivered);
        }
        catch (OperationCanceledException) when (ending.IsCancellationRequested)
        {
            Observe(send);
        }
        catch (Exception error) when (error is TimeoutException
            || (error is OperationCanceledException && deadline.IsCancellationRequested))
        {
            Observe(send);
            queue.End(item, OutboundOutcome.TimedOut, error, close: true);
        }
        catch (Exception error)
        {
            queue.End(item, OutboundOutcome.Failed, error, close: true);
        }
    }

    private static void Observe(Task task) =>
        _ = task.ContinueWith(static done => _ = done.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private async Task CloseAsync(IOutboundConnection connection)
    {
        try
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Closing an outbound connection failed");
        }
    }

    private void FailConnection(NetworkConnectionContext context, OutboundOutcome reason)
    {
        logger.LogWarning("Outbound connection {ConnectionId} closed: {Reason}", context.ConnectionId, reason);
        foreach (var observer in observers)
        {
            try
            {
                observer.OnConnectionFailed(context, reason);
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "An outbound delivery observer failed");
            }
        }

        if (transport.Resolve(context) is { } connection)
            _ = Detached(() => CloseAsync(connection));
    }

    private void Complete(Item item, OutboundOutcome outcome, Exception? error = null)
    {
        if (!item.Done.TrySetResult(outcome))
            return;
        item.ReleaseFrames();
        var level = outcome switch
        {
            OutboundOutcome.Delivered or OutboundOutcome.Closed => LogLevel.Trace,
            OutboundOutcome.Stale or OutboundOutcome.Discarded or OutboundOutcome.Skipped => LogLevel.Debug,
            OutboundOutcome.EncodingFailed => LogLevel.Error,
            _ => LogLevel.Warning,
        };
        if (logger.IsEnabled(level))
            logger.Log(level, error, "Outbound entry for {ConnectionId}: {Outcome}", item.Entry.Connection.ConnectionId, outcome);
        var report = new OutboundDeliveryReport(item.Entry, outcome, error);
        foreach (var observer in observers)
        {
            try
            {
                observer.OnEntryCompleted(report);
            }
            catch (Exception failure)
            {
                logger.LogWarning(failure, "An outbound delivery observer failed");
            }
        }
    }

    private sealed class GroupState
    {
        public int LastSubmitted { get; set; } = int.MinValue;

        public Item? LastAdmitted { get; set; }
    }

    private sealed class Item
    {
        private int broken;

        public Item(OutboundEntry entry, ConnectionQueue queue, Item? predecessor, byte[] frames, Exception? encodingError)
        {
            Entry = entry;
            Queue = queue;
            Predecessor = predecessor;
            Frames = frames;
            Length = frames.Length;
            EncodingError = encodingError;
            Settled = predecessor is null ? Done.Task : Task.WhenAll(predecessor.Settled, Done.Task);
        }

        public OutboundEntry Entry { get; }

        public ConnectionQueue Queue { get; }

        public Item? Predecessor { get; }

        /// <summary>Released once the entry is done, so a group's chain of items does not keep its bytes.</summary>
        public byte[] Frames { get; private set; }

        public int Length { get; }

        public void ReleaseFrames() => Frames = [];

        public Exception? EncodingError { get; }

        public TaskCompletionSource<OutboundOutcome> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>This entry and everything before it in its group are done.</summary>
        public Task Settled { get; }

        /// <summary>Whether a Required entry up to and including this one did not deliver; only asked once settled.</summary>
        public bool BreaksGroup()
        {
            // Memoised (0 unknown, 1 intact, 2 broken) so a long group is walked once.
            var unknown = new Stack<Item>();
            var item = this;
            while (item is not null && Volatile.Read(ref item.broken) == 0)
            {
                unknown.Push(item);
                item = item.Predecessor;
            }

            var result = item is not null && Volatile.Read(ref item.broken) == 2;
            while (unknown.TryPop(out var next))
            {
                result |= next.Entry.Delivery == OutboundDelivery.Required
                    && next.Done.Task.Result is not (OutboundOutcome.Delivered or OutboundOutcome.Closed);
                Volatile.Write(ref next.broken, result ? 2 : 1);
            }

            return result;
        }
    }

    private sealed class ConnectionQueue(OutboundSender owner, NetworkConnectionContext context)
    {
        private readonly Lock gate = new();
        private readonly Queue<Item> items = new();
        private readonly CancellationTokenSource ending = new();
        private long bytes;
        private bool running;
        private bool ended;

        public void Enqueue(Item item)
        {
            bool discard = false, overflow = false, start = false;
            lock (gate)
            {
                if (ended)
                    discard = true;
                else if (bytes + item.Length > owner.options.QueueByteLimit)
                    overflow = true;
                else
                {
                    items.Enqueue(item);
                    bytes += item.Length;
                    start = !running;
                    running = true;
                }
            }

            if (discard)
                owner.Complete(item, OutboundOutcome.Discarded);
            else if (overflow)
                End(item, OutboundOutcome.OverLimit, null, close: true);
            else if (start)
                _ = Detached(RunAsync);
        }

        /// <summary>
        /// Ends the queue for good: <paramref name="cause"/> gets <paramref name="outcome"/>, everything
        /// still queued gets <paramref name="remainder"/>, and a failure closes the connection.
        /// </summary>
        public void End(
            Item cause,
            OutboundOutcome outcome,
            Exception? error,
            bool close,
            OutboundOutcome remainder = OutboundOutcome.Discarded)
        {
            Item[] dropped;
            lock (gate)
            {
                if (ended)
                {
                    dropped = [];
                    close = false;
                    outcome = OutboundOutcome.Discarded;
                }
                else
                {
                    ended = true;
                    dropped = [.. items];
                    items.Clear();
                    bytes = 0;
                }
            }

            ending.Cancel();
            owner.Complete(cause, outcome, error);
            foreach (var item in dropped)
                owner.Complete(item, remainder);
            if (close)
                owner.FailConnection(context, outcome);
        }

        private async Task RunAsync()
        {
            while (true)
            {
                Item head;
                lock (gate)
                {
                    if (ended || items.Count == 0)
                    {
                        running = false;
                        return;
                    }

                    head = items.Peek();
                }

                await owner.ProcessAsync(this, head, ending.Token).ConfigureAwait(false);
                lock (gate)
                {
                    if (!ended && items.TryPeek(out var first) && ReferenceEquals(first, head))
                    {
                        items.Dequeue();
                        bytes -= head.Length;
                    }
                }
            }
        }
    }
}
