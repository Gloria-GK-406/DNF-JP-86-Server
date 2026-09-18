using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterVitalRuntimeDomainService : ICharacterVitalRuntimeDomainService
{
    private readonly object sync = new();
    private readonly Dictionary<CharacterId, (SelectedSessionIdentity Session, byte Percent)> current = [];

    public void Publish(SelectedSessionIdentity session, byte hpPercent)
    {
        lock (sync) current[session.CharacterId] = (session, Math.Min(hpPercent, (byte)100));
    }

    public byte HpPercentOf(CharacterId characterId)
    {
        lock (sync) return current.TryGetValue(characterId, out var value) ? value.Percent : (byte)0;
    }

    public bool RemoveExact(SelectedSessionIdentity session)
    {
        lock (sync) return current.TryGetValue(session.CharacterId, out var value)
            && value.Session == session && current.Remove(session.CharacterId);
    }
}
