using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Sessions;

/// <summary>Keeps open expert stores in memory, keyed by their owner.</summary>
internal sealed class ExpertStoreRegistry : IExpertStoreRegistry
{
    private readonly ConcurrentDictionary<CharacterId, byte> _open = new();

    public void Open(CharacterId ownerCharacterId) => _open[ownerCharacterId] = 0;

    public bool Close(CharacterId ownerCharacterId) => _open.TryRemove(ownerCharacterId, out _);

    public bool HasOpenStore(CharacterId ownerCharacterId) => _open.ContainsKey(ownerCharacterId);
}
