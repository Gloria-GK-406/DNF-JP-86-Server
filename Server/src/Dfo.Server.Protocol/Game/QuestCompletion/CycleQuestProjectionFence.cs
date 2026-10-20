using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Protocol.Game.QuestCompletion;

/// <summary>Committed quest results follow the originating actor-death frames, even when the outbox wins the race.</summary>
public sealed class CycleQuestProjectionFence
{
    private readonly object gate = new();
    private readonly Dictionary<DungeonRunId, Pending> pending = [];
    public IDisposable Begin(DungeonRunId run)
    {
        lock (gate)
        {
            if (!pending.TryGetValue(run, out var state)) pending.Add(run, state = new());
            state.Count++;
            return new Lease(this, run, state);
        }
    }
    public Task WaitAsync(DungeonRunId run, CancellationToken ct)
    {
        lock (gate) return pending.TryGetValue(run, out var state) ? state.Done.Task.WaitAsync(ct) : Task.CompletedTask;
    }
    private sealed class Pending
    {
        public int Count;
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class Lease(CycleQuestProjectionFence owner, DungeonRunId run, Pending state) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lock (owner.gate)
                if (--state.Count == 0) { owner.pending.Remove(run); state.Done.TrySetResult(); }
        }
    }
}
