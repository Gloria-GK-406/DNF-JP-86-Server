using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.CompoundItem;

public sealed class CompoundItemReplayGuard
{
    private readonly ConditionalWeakTable<NetworkConnectionContext, State> states = new();

    internal async ValueTask<Lease> EnterAsync(NetworkConnectionContext connection,
        ushort sequence, CancellationToken ct)
    {
        var state = states.GetValue(connection, _ => new State());
        await state.Gate.WaitAsync(ct).ConfigureAwait(false);
        return new(state, sequence);
    }

    internal sealed class State
    {
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal SelectedSessionIdentity? Session { get; set; }
        internal Dictionary<ushort, Cached> Responses { get; } = [];
        internal Queue<(ushort Sequence, long Ordinal)> Order { get; } = new();
        internal (Fingerprint Request, ushort Sequence, long Ordinal)? FailedCommit { get; set; }
    }

    internal sealed record Fingerprint(int Source, byte SourceMode, ushort Count, bool Valid)
    {
        internal static Fingerprint From(CompoundItemRequest request) => new(request.Source, request.SourceMode, request.Count, request.Valid);
    }

    internal sealed record Cached(Fingerprint Request, RecipeCraftReceipt? Receipt,
        byte ErrorCode, long Ordinal);

    internal sealed class Lease(State state, ushort sequence) : IDisposable
    {
        public void BindSession(SelectedSessionIdentity session)
        {
            if (state.Session == session) return;
            state.Responses.Clear();
            state.Order.Clear();
            state.FailedCommit = null;
            state.Session = session;
        }

        public Cached? CachedFor(CompoundItemRequest request, out bool conflict)
        {
            conflict = false;
            if (!state.Responses.TryGetValue(sequence, out var prior)) return null;
            var ordinal = request.Metadata.InboundOrdinal;
            if (ordinal is null || ordinal <= prior.Ordinal || ordinal - prior.Ordinal > 128)
                return null;
            conflict = prior.Request != Fingerprint.From(request);
            return conflict ? null : prior;
        }

        public bool Admit(CompoundItemRequest request)
        {
            var ordinal = request.Metadata.InboundOrdinal;
            if (ordinal is null || ordinal <= 0) return false;
            if (request.Metadata.SequenceIsFresh)
            {
                state.FailedCommit = null;
                return true;
            }
            return state.FailedCommit is { } failed
                && failed.Sequence == sequence
                && failed.Request == Fingerprint.From(request)
                && ordinal > failed.Ordinal && ordinal - failed.Ordinal <= 128;
        }

        public void MarkCommitFailed(CompoundItemRequest request)
        {
            if (request.Metadata.InboundOrdinal is { } ordinal)
                state.FailedCommit = (Fingerprint.From(request), sequence, ordinal);
        }

        public void Remember(CompoundItemRequest request, RecipeCraftReceipt receipt)
            => RememberCore(request, receipt, 0);

        public void RememberFailure(CompoundItemRequest request, byte errorCode)
            => RememberCore(request, null, errorCode);

        private void RememberCore(CompoundItemRequest request,
            RecipeCraftReceipt? receipt, byte errorCode)
        {
            var ordinal = request.Metadata.InboundOrdinal
                ?? throw new InvalidOperationException("Settled outworld action lacks an inbound ordinal.");
            state.FailedCommit = null;
            state.Responses[sequence] = new(Fingerprint.From(request), receipt, errorCode, ordinal);
            state.Order.Enqueue((sequence, ordinal));
            while (state.Order.Count > 128)
            {
                var old = state.Order.Dequeue();
                if (state.Responses.TryGetValue(old.Sequence, out var cached)
                    && cached.Ordinal == old.Ordinal)
                    state.Responses.Remove(old.Sequence);
            }
        }

        public void Dispose() => state.Gate.Release();
    }
}
