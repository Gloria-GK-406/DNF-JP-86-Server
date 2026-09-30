using System.Runtime.CompilerServices;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.EnchantByBead;

/// <summary>Connection-local bounded replay cache; the session identity invalidates old ACKs.</summary>
public sealed class EnchantByBeadReplayGuard
{
    private readonly ConditionalWeakTable<NetworkConnectionContext, State> states = new();

    internal async ValueTask<Lease> EnterAsync(
        NetworkConnectionContext connection, ushort sequence, CancellationToken ct)
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

    internal sealed record Fingerprint(byte BeadList, short BeadSlot, byte TargetList, short TargetSlot)
    {
        internal static Fingerprint From(EnchantByBeadRequest request) => new(
            (byte)request.BeadList, request.BeadSlot, (byte)request.TargetList, request.TargetSlot);
    }

    internal sealed record Cached(Fingerprint Request, EnchantByBeadResponse Response,
        EquipmentEnchantReceipt Receipt, long Ordinal);

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

        public Cached? CachedFor(EnchantByBeadRequest request, out bool conflict)
        {
            conflict = false;
            if (!state.Responses.TryGetValue(sequence, out var prior)) return null;
            var ordinal = request.Metadata.InboundOrdinal;
            if (ordinal is null || ordinal <= prior.Ordinal || ordinal - prior.Ordinal > 128)
                return null;
            conflict = prior.Request != Fingerprint.From(request);
            return conflict ? null : prior;
        }

        /// <summary>Freshness is measured at the network boundary across every Game frame,
        /// not by subtracting two enchant-only sequence values.</summary>
        public bool Admit(EnchantByBeadRequest request)
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

        public void MarkCommitFailed(EnchantByBeadRequest request)
        {
            if (request.Metadata.InboundOrdinal is { } ordinal)
                state.FailedCommit = (Fingerprint.From(request), sequence, ordinal);
        }

        public void Remember(EnchantByBeadRequest request, EnchantByBeadResponse response,
            EquipmentEnchantReceipt receipt)
        {
            var ordinal = request.Metadata.InboundOrdinal
                ?? throw new InvalidOperationException("Committed enchant lacked a transport ordinal.");
            state.FailedCommit = null;
            state.Responses[sequence] = new(Fingerprint.From(request), response, receipt, ordinal);
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
