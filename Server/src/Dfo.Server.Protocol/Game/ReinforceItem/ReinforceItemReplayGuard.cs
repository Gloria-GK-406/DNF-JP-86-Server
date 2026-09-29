using System.Runtime.CompilerServices;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.ReinforceItem;

/// <summary>Serialises one connection's reinforcement requests and keeps a bounded sequence cache.</summary>
public sealed class ReinforceItemReplayGuard
{
    private readonly ConditionalWeakTable<NetworkConnectionContext, ConnectionState> states = new();

    internal async ValueTask<Lease> EnterAsync(
        NetworkConnectionContext connection, ushort sequence, CancellationToken ct)
    {
        var state = states.GetValue(connection, _ => new ConnectionState());
        await state.Gate.WaitAsync(ct).ConfigureAwait(false);
        return new(state, sequence);
    }

    internal sealed class ConnectionState
    {
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal SelectedSessionIdentity? Session { get; set; }
        internal Dictionary<ushort, CachedResponse> Responses { get; } = [];
        internal Queue<ushort> Order { get; } = new();
    }

    internal sealed record CachedResponse(RequestFingerprint Request, ReinforceItemResponse Response);

    internal sealed record RequestFingerprint(ushort Method, short TargetSlot, int ExpectedItemId,
        short MaterialSlot, short OptionalTicketSlot, string NameHex)
    {
        internal static RequestFingerprint From(ReinforceItemRequest request) => new(
            request.Method, request.TargetSlot, request.ExpectedItemId, request.MaterialSlot,
            request.OptionalTicketSlot, Convert.ToHexString(request.ReportedItemNameBytes));
    }

    internal sealed class Lease(ConnectionState state, ushort sequence) : IDisposable
    {
        public void BindSession(SelectedSessionIdentity session)
        {
            if (state.Session == session) return;
            state.Responses.Clear();
            state.Order.Clear();
            state.Session = session;
        }

        public ReinforceItemResponse? CachedFor(ReinforceItemRequest request, out bool conflict)
        {
            conflict = false;
            if (!state.Responses.TryGetValue(sequence, out var cached)) return null;
            conflict = cached.Request != RequestFingerprint.From(request);
            return conflict ? null : cached.Response;
        }

        public void Remember(ReinforceItemRequest request, ReinforceItemResponse response)
        {
            if (state.Responses.TryAdd(sequence,
                    new(RequestFingerprint.From(request), response))) state.Order.Enqueue(sequence);
            while (state.Order.Count > 128)
                state.Responses.Remove(state.Order.Dequeue());
        }

        public void Dispose() => state.Gate.Release();
    }
}
