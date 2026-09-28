using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Characters;

internal sealed class SpiritPotionDomainService(TimeProvider clock) : ISpiritPotionDomainService
{
    private readonly Lock gate = new();
    private readonly Dictionary<CharacterId, (double Rate, DateTimeOffset ExpiresAt)> effects = [];

    public void Apply(CharacterId characterId, double bonusRate, TimeSpan duration)
    {
        var expiresAt = Expiry(bonusRate, duration);
        lock (gate) effects[characterId] = (bonusRate, expiresAt);
    }

    internal void ApplyForSession(SelectedSessionIdentity session,
        ISelectedSessionDomainService sessions, double bonusRate, TimeSpan duration)
    {
        var expiresAt = Expiry(bonusRate, duration);
        lock (gate)
        {
            if (sessions.FindByCharacter(session.CharacterId) == session)
                effects[session.CharacterId] = (bonusRate, expiresAt);
        }
    }

    internal void ClearIfUnselected(CharacterId characterId, ISelectedSessionDomainService sessions)
    {
        lock (gate)
        {
            if (sessions.FindByCharacter(characterId) is null)
                effects.Remove(characterId);
        }
    }

    public void Clear(CharacterId characterId)
    {
        lock (gate) effects.Remove(characterId);
    }

    public double GetBonusRate(CharacterId characterId)
    {
        lock (gate)
        {
            if (!effects.TryGetValue(characterId, out var effect)) return 0;
            if (clock.GetUtcNow() < effect.ExpiresAt) return effect.Rate;
            effects.Remove(characterId);
            return 0;
        }
    }

    private DateTimeOffset Expiry(double bonusRate, TimeSpan duration)
    {
        if (!double.IsFinite(bonusRate) || bonusRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(bonusRate));
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));
        return clock.GetUtcNow().Add(duration);
    }
}
