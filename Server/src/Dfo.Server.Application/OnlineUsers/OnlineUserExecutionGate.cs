using System.Collections.Concurrent;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Application.OnlineUsers;

public sealed class OnlineUserExecutionGate
{
    private readonly ConcurrentDictionary<OnlineUserId, Entry> entries = new();

    internal int TrackedEntryCount => entries.Count;

    public ValueTask<IAsyncDisposable> AcquireAsync(
        OnlineUserId onlineUserId,
        OnlineUserAccessMode accessMode,
        CancellationToken cancellationToken)
    {
        if (accessMode == OnlineUserAccessMode.Unscoped)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accessMode),
                accessMode,
                "An online-user execution lease must be Shared or Exclusive.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            var entry = entries.GetOrAdd(
                onlineUserId,
                static (id, owner) => new Entry(owner, id),
                this);
            if (entry.TryAcquire(accessMode, cancellationToken, out var acquisition))
            {
                return acquisition;
            }

            entries.TryRemove(new KeyValuePair<OnlineUserId, Entry>(onlineUserId, entry));
        }
    }

    private void RemoveRetired(OnlineUserId onlineUserId, Entry entry) =>
        entries.TryRemove(new KeyValuePair<OnlineUserId, Entry>(onlineUserId, entry));

    private sealed class Entry(OnlineUserExecutionGate owner, OnlineUserId onlineUserId)
    {
        private readonly object sync = new();
        private readonly LinkedList<Waiter> waiters = [];
        private int references;
        private int activeShared;
        private bool activeExclusive;
        private bool retired;

        public bool TryAcquire(
            OnlineUserAccessMode accessMode,
            CancellationToken cancellationToken,
            out ValueTask<IAsyncDisposable> acquisition)
        {
            lock (sync)
            {
                if (retired)
                {
                    acquisition = default;
                    return false;
                }

                references++;
                if (CanEnterImmediately(accessMode))
                {
                    MarkEntered(accessMode);
                    acquisition = ValueTask.FromResult<IAsyncDisposable>(
                        new Lease(this, accessMode));
                    return true;
                }

                var waiter = new Waiter(this, accessMode, cancellationToken);
                waiter.Node = waiters.AddLast(waiter);
                if (cancellationToken.CanBeCanceled)
                {
                    var registration = cancellationToken.Register(
                        static state => ((Waiter)state!).Cancel(),
                        waiter);
                    waiter.CancellationRegistration = registration;
                    if (waiter.State != WaiterState.Waiting)
                    {
                        registration.Unregister();
                    }
                }

                acquisition = new ValueTask<IAsyncDisposable>(waiter.Completion.Task);
                return true;
            }
        }

        private bool CanEnterImmediately(OnlineUserAccessMode accessMode) =>
            waiters.Count == 0
            && !activeExclusive
            && (accessMode == OnlineUserAccessMode.Shared || activeShared == 0);

        private void MarkEntered(OnlineUserAccessMode accessMode)
        {
            if (accessMode == OnlineUserAccessMode.Shared)
            {
                activeShared++;
            }
            else
            {
                activeExclusive = true;
            }
        }

        private void Release(OnlineUserAccessMode accessMode)
        {
            bool removeRetired;
            lock (sync)
            {
                if (accessMode == OnlineUserAccessMode.Shared)
                {
                    if (activeShared <= 0)
                    {
                        throw new InvalidOperationException("No shared online-user execution is active.");
                    }

                    activeShared--;
                }
                else
                {
                    if (!activeExclusive)
                    {
                        throw new InvalidOperationException("No exclusive online-user execution is active.");
                    }

                    activeExclusive = false;
                }

                GrantWaiters();
                removeRetired = ReleaseReference();
            }

            if (removeRetired)
            {
                owner.RemoveRetired(onlineUserId, this);
            }
        }

        private void Cancel(Waiter waiter)
        {
            bool removeRetired;
            lock (sync)
            {
                if (waiter.State != WaiterState.Waiting || waiter.Node is null)
                {
                    return;
                }

                waiters.Remove(waiter.Node);
                waiter.Node = null;
                waiter.State = WaiterState.Cancelled;
                waiter.Completion.TrySetCanceled(waiter.CancellationToken);
                GrantWaiters();
                removeRetired = ReleaseReference();
            }

            if (removeRetired)
            {
                owner.RemoveRetired(onlineUserId, this);
            }
        }

        private bool ReleaseReference()
        {
            if (--references != 0)
            {
                return false;
            }

            if (activeShared != 0 || activeExclusive || waiters.Count != 0)
            {
                throw new InvalidOperationException("An idle gate entry still owns execution state.");
            }

            retired = true;
            return true;
        }

        private void GrantWaiters()
        {
            if (activeExclusive || waiters.First is null)
            {
                return;
            }

            if (waiters.First.Value.AccessMode == OnlineUserAccessMode.Exclusive)
            {
                if (activeShared != 0)
                {
                    return;
                }

                Grant(waiters.First.Value);
                activeExclusive = true;
                return;
            }

            while (waiters.First is { Value.AccessMode: OnlineUserAccessMode.Shared } node)
            {
                Grant(node.Value);
                activeShared++;
            }
        }

        private void Grant(Waiter waiter)
        {
            waiters.Remove(waiter.Node!);
            waiter.Node = null;
            waiter.State = WaiterState.Granted;
            waiter.CancellationRegistration.Unregister();
            waiter.Completion.TrySetResult(new Lease(this, waiter.AccessMode));
        }

        private sealed class Waiter(
            Entry owner,
            OnlineUserAccessMode accessMode,
            CancellationToken cancellationToken)
        {
            public OnlineUserAccessMode AccessMode { get; } = accessMode;

            public CancellationToken CancellationToken { get; } = cancellationToken;

            public TaskCompletionSource<IAsyncDisposable> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public LinkedListNode<Waiter>? Node { get; set; }

            public CancellationTokenRegistration CancellationRegistration { get; set; }

            public WaiterState State { get; set; } = WaiterState.Waiting;

            public void Cancel() => owner.Cancel(this);
        }

        private sealed class Lease(Entry owner, OnlineUserAccessMode accessMode) : IAsyncDisposable
        {
            private Entry? owner = owner;

            public ValueTask DisposeAsync()
            {
                Interlocked.Exchange(ref owner, null)?.Release(accessMode);
                return ValueTask.CompletedTask;
            }
        }

        private enum WaiterState
        {
            Waiting,
            Granted,
            Cancelled,
        }
    }
}
