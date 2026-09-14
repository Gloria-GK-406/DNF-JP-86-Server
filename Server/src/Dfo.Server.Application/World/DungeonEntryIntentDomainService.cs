using System.Collections.Concurrent;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class DungeonEntryIntentDomainService : IDungeonEntryIntentDomainService
{
    private readonly ConcurrentDictionary<CharacterId, OwnedIntent> _intents = new();

    public void Record(SelectedSessionIdentity session, DungeonEntryIntent intent) =>
        _intents[session.CharacterId] = new OwnedIntent(session, intent);

    public DungeonEntryIntent? Consume(SelectedSessionIdentity session)
    {
        while (_intents.TryGetValue(session.CharacterId, out var owned))
        {
            if (owned.Session != session)
                return null;

            var pair = new KeyValuePair<CharacterId, OwnedIntent>(session.CharacterId, owned);
            if (((ICollection<KeyValuePair<CharacterId, OwnedIntent>>)_intents).Remove(pair))
                return owned.Intent;
        }

        return null;
    }

    public bool ClearExact(SelectedSessionIdentity session)
    {
        if (!_intents.TryGetValue(session.CharacterId, out var owned) || owned.Session != session)
            return false;

        var pair = new KeyValuePair<CharacterId, OwnedIntent>(session.CharacterId, owned);
        return ((ICollection<KeyValuePair<CharacterId, OwnedIntent>>)_intents).Remove(pair);
    }

    private sealed record OwnedIntent(
        SelectedSessionIdentity Session,
        DungeonEntryIntent Intent);
}
