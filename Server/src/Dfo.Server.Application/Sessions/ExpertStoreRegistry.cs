using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Sessions;

/// <summary>Keeps open expert stores in memory, keyed by their owner.</summary>
internal sealed class ExpertStoreRegistry : IExpertStoreRegistry
{
    private readonly ConcurrentDictionary<CharacterId, StoreOwner> _open = new();

    public void Open(CharacterId ownerCharacterId) => _open[ownerCharacterId] = new(null);

    public void Open(SelectedSessionIdentity session) => _open[session.CharacterId] = new(session);

    public bool Close(CharacterId ownerCharacterId) => _open.TryRemove(ownerCharacterId, out _);

    public bool CloseExact(SelectedSessionIdentity session) =>
        _open.TryGetValue(session.CharacterId, out var current)
        && current.Session == session
        && _open.TryRemove(new KeyValuePair<CharacterId, StoreOwner>(session.CharacterId, current));

    public bool HasOpenStore(CharacterId ownerCharacterId) => _open.ContainsKey(ownerCharacterId);

    private sealed record StoreOwner(SelectedSessionIdentity? Session);
}
