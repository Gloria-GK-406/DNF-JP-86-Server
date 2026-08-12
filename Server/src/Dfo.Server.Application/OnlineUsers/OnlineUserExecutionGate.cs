using System.Collections.Concurrent;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Application.OnlineUsers;

public class UserExecutionGate<TKey> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Entry> entries = new();

    internal int TrackedEntryCount => entries.Count;

    public ValueTask<IAsyncDisposable> AcquireAsync(
        TKey userId,
        UserAccessMode accessMode,
        CancellationToken cancellationToken)
    {
        if (accessMode == UserAccessMode.Unscoped)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accessMode),
                accessMode,
                "A user execution lease must be Shared or Exclusive.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            var entry = entries.GetOrAdd(
                userId,
                static (id, owner) => new Entry(owner, id),
                this);
            if (entry.TryAcquire(accessMode, cancellationToken, out var acquisition))
            {
                return acquisition;
            }

            entries.TryRemove(new KeyValuePair<TKey, Entry>(userId, entry));
        }
    }

    private void RemoveRetired(TKey userId, Entry entry) =>
        entries.TryRemove(new KeyValuePair<TKey, Entry>(userId, entry));

    private sealed class Entry(UserExecutionGate<TKey> owner, TKey userId)
    {
        private readonly object sync = new();
        private readonly LinkedList<Waiter> waiters = [];
        private int references;
        private int activeShared;
        private bool activeExclusive;
        private bool retired;

        public bool TryAcquire(
            UserAccessMode accessMode,
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

        private bool CanEnterImmediately(UserAccessMode accessMode) =>
            waiters.Count == 0
            && !activeExclusive
            && (accessMode == UserAccessMode.Shared || activeShared == 0);

        private void MarkEntered(UserAccessMode accessMode)
        {
            if (accessMode == UserAccessMode.Shared)
            {
                activeShared++;
            }
            else
            {
                activeExclusive = true;
            }
        }

        private void Release(UserAccessMode accessMode)
        {
            bool removeRetired;
            lock (sync)
            {
                if (accessMode == UserAccessMode.Shared)
                {
                    if (activeShared <= 0)
                    {
                        throw new InvalidOperationException("No shared user execution is active.");
                    }

                    activeShared--;
                }
                else
                {
                    if (!activeExclusive)
                    {
                        throw new InvalidOperationException("No exclusive user execution is active.");
                    }

                    activeExclusive = false;
                }

                GrantWaiters();
                removeRetired = ReleaseReference();
            }

            if (removeRetired)
            {
                owner.RemoveRetired(userId, this);
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
                owner.RemoveRetired(userId, this);
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

            if (waiters.First.Value.AccessMode == UserAccessMode.Exclusive)
            {
                if (activeShared != 0)
                {
                    return;
                }

                Grant(waiters.First.Value);
                activeExclusive = true;
                return;
            }

            while (waiters.First is { Value.AccessMode: UserAccessMode.Shared } node)
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
            UserAccessMode accessMode,
            CancellationToken cancellationToken)
        {
            public UserAccessMode AccessMode { get; } = accessMode;

            public CancellationToken CancellationToken { get; } = cancellationToken;

            public TaskCompletionSource<IAsyncDisposable> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public LinkedListNode<Waiter>? Node { get; set; }

            public CancellationTokenRegistration CancellationRegistration { get; set; }

            public WaiterState State { get; set; } = WaiterState.Waiting;

            public void Cancel() => owner.Cancel(this);
        }

        private sealed class Lease(Entry owner, UserAccessMode accessMode) : IAsyncDisposable
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

public sealed class OnlineUserExecutionGate : UserExecutionGate<OnlineUserId>;

public sealed class AccountExecutionGate : UserExecutionGate<AccountId>;

/// <summary>Serializes selected-session transition/teardown independently of command execution.</summary>
public sealed class SelectedSessionTransitionGate : UserExecutionGate<AccountId>;
