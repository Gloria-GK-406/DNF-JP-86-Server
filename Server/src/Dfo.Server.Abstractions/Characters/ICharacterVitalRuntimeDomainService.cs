using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>Current in-world HP display value, owned by the character rather than its connection.</summary>
public interface ICharacterVitalRuntimeDomainService
{
    void Publish(SelectedSessionIdentity session, byte hpPercent);
    byte HpPercentOf(CharacterId characterId);
    bool RemoveExact(SelectedSessionIdentity session);
}
