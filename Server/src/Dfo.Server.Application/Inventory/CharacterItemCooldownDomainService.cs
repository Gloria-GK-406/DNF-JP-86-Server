using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Inventory;

/// <summary>
/// Remembers when each of a character's cooldowns started.
/// </summary>
/// <remarks>
/// A dictionary and a clock. Expiry is not an event anyone is waiting for -- nothing happens when
/// a cooldown ends except that the next question is answered differently -- so a timer per running
/// cooldown would buy nothing and would have to be cancelled on every disconnect.
///
/// Entries for an expired cooldown are left in place until the same key is used again or the
/// character leaves. They are two words each, bounded by the number of cooldown groups a character
/// can touch in one session, and sweeping them would need a second pass over a structure whose
/// only reader already checks the timestamp.
/// </remarks>
internal sealed class CharacterItemCooldownDomainService(TimeProvider timeProvider)
    : ICharacterItemCooldownDomainService
{
    private readonly ConcurrentDictionary<(int CharacterId, ItemCooldownKey Key), long> expiries = new();

    public bool IsCoolingDown(CharacterId characterId, ItemCooldownKey key) =>
        expiries.TryGetValue((characterId.Value, key), out var expiresAt)
        && timeProvider.GetUtcNow().UtcTicks < expiresAt;

    public void Start(CharacterId characterId, ItemCooldownKey key, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return;

        var expiresAt = timeProvider.GetUtcNow().UtcTicks + duration.Ticks;
        expiries[(characterId.Value, key)] = expiresAt;
    }

    public void Clear(CharacterId characterId)
    {
        foreach (var entry in expiries.Keys)
        {
            if (entry.CharacterId == characterId.Value)
                expiries.TryRemove(entry, out _);
        }
    }
}
