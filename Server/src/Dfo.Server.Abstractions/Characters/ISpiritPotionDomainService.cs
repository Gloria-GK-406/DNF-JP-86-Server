using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>Session-scoped experience potion bonus for dungeon clear rewards.</summary>
public interface ISpiritPotionDomainService
{
    void Apply(CharacterId characterId, double bonusRate, TimeSpan duration);
    void Clear(CharacterId characterId);
    double GetBonusRate(CharacterId characterId);
}
