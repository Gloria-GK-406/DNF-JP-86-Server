using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.OnlineUsers;

/// <summary>Serializes selected-session transition/teardown independently of command execution.</summary>
public sealed class SelectedSessionTransitionGate : UserExecutionGate<AccountId>
{
    private readonly AsyncLocal<Frame?> current = new();

    /// <summary>Installs all borrowable frames on the caller's flow before sequential acquisition.</summary>
    public ValueTask<IAsyncDisposable> AcquireManyExclusiveAsync(
        IEnumerable<AccountId> accounts, CancellationToken cancellationToken)
    {
        var frames = new List<Frame>();
        foreach (var account in accounts.Distinct().OrderBy(x => x.Value))
        {
            var frame = new Frame(account, this.LiveParent(), accessMode: UserAccessMode.Exclusive);
            this.current.Value = frame;
            frames.Add(frame);
        }
        return AcquireGroupAsync(frames, cancellationToken);
    }

    private async ValueTask<IAsyncDisposable> AcquireGroupAsync(List<Frame> frames, CancellationToken ct)
    {
        var group = new FrameGroup(frames);
        try
        {
            foreach (var frame in frames)
                frame.Acquired(await base.AcquireAsync(frame.AccountId, UserAccessMode.Exclusive, ct).ConfigureAwait(false));
            return group;
        }
        catch
        {
            await group.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class FrameGroup(List<Frame> frames) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            for (var i = frames.Count - 1; i >= 0; i--)
                await frames[i].DisposeAsync().ConfigureAwait(false);
        }
    }

    // This entry point must stay synchronous: installing the frame after an await would only
    // update the callee's ExecutionContext, and the caller would not be able to borrow it.
    public override ValueTask<IAsyncDisposable> AcquireAsync(
        AccountId accountId, UserAccessMode accessMode, CancellationToken cancellationToken)
    {
        var acquisition = base.AcquireAsync(accountId, accessMode, cancellationToken);
        var frame = new Frame(accountId, this.LiveParent(), accessMode: accessMode);
        this.current.Value = frame;
        return CompleteAcquisitionAsync(frame, acquisition);
    }

    /// <summary>Borrow a live lease on this flow; otherwise acquire an ordinary Shared lease.</summary>
    public ValueTask<IAsyncDisposable> BorrowOrAcquireSharedAsync(
        AccountId accountId, CancellationToken cancellationToken) =>
        this.BorrowOrAcquireAsync(accountId, UserAccessMode.Shared, cancellationToken);

    /// <summary>Protect a read and its delivery from other settlements; a held Shared lease cannot be upgraded.</summary>
    public ValueTask<IAsyncDisposable> BorrowOrAcquireExclusiveAsync(
        AccountId accountId, CancellationToken cancellationToken) =>
        this.BorrowOrAcquireAsync(accountId, UserAccessMode.Exclusive, cancellationToken);

    private ValueTask<IAsyncDisposable> BorrowOrAcquireAsync(
        AccountId accountId, UserAccessMode accessMode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A borrowable frame seen off the executor has flowed out of the job that holds it.
        GameplayGateAssertion.Entering($"{nameof(SelectedSessionTransitionGate)} borrow");
        for (var candidate = this.current.Value; candidate is not null; candidate = candidate.Parent)
        {
            if (candidate.AccountId != accountId || !candidate.TryBorrow(accessMode, out var owner)) continue;
            var frame = new Frame(accountId, this.LiveParent(), owner);
            this.current.Value = frame;
            return ValueTask.FromResult<IAsyncDisposable>(frame);
        }
        return this.AcquireAsync(accountId, accessMode, cancellationToken);
    }

    // Closed frames remain invalid for inherited execution contexts, but need not accumulate
    // when the current flow repeatedly opens new leases after completing earlier ones.
    private Frame? LiveParent()
    {
        var parent = this.current.Value;
        while (parent is not null && !parent.IsActive) parent = parent.Parent;
        return parent;
    }

    private static async ValueTask<IAsyncDisposable> CompleteAcquisitionAsync(
        Frame frame, ValueTask<IAsyncDisposable> acquisition)
    {
        try
        {
            frame.Acquired(await acquisition.ConfigureAwait(false));
            return frame;
        }
        catch
        {
            await frame.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class Frame(AccountId accountId, Frame? parent, LeaseOwner? owner = null,
        UserAccessMode accessMode = UserAccessMode.Shared)
        : IAsyncDisposable
    {
        private readonly object sync = new();
        private LeaseOwner? owner = owner;
        private bool active = true;
        public AccountId AccountId { get; } = accountId;
        public Frame? Parent { get; } = parent;
        public bool IsActive { get { lock (this.sync) return this.active; } }

        public void Acquired(IAsyncDisposable lease)
        {
            lock (this.sync) this.owner = new LeaseOwner(lease, accessMode);
        }

        public bool TryBorrow(UserAccessMode requiredMode, out LeaseOwner? borrowed)
        {
            lock (this.sync)
            {
                borrowed = this.active ? this.owner : null;
                if (borrowed is null) return false;
                if (requiredMode == UserAccessMode.Exclusive && borrowed.AccessMode != UserAccessMode.Exclusive)
                    throw new InvalidOperationException("A held selected-session Shared lease cannot be upgraded to Exclusive.");
                borrowed.AddReference();
                return true;
            }
        }

        public ValueTask DisposeAsync()
        {
            LeaseOwner? release;
            lock (this.sync)
            {
                if (!this.active) return ValueTask.CompletedTask;
                this.active = false;
                release = this.owner;
            }
            return release?.ReleaseAsync() ?? ValueTask.CompletedTask;
        }
    }

    private sealed class LeaseOwner(IAsyncDisposable lease, UserAccessMode accessMode)
    {
        private int references = 1;
        public UserAccessMode AccessMode { get; } = accessMode;
        public void AddReference() => Interlocked.Increment(ref this.references);
        public ValueTask ReleaseAsync() => Interlocked.Decrement(ref this.references) == 0
            ? lease.DisposeAsync() : ValueTask.CompletedTask;
    }
}
