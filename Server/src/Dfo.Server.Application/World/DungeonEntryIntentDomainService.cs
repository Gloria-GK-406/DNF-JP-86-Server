using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class DungeonEntryIntentDomainService(ISaoTowerRuleService sao) : IDungeonEntryIntentDomainService
{
    private readonly ConcurrentDictionary<CharacterId, OwnedIntent> _intents = new();

    public void Record(SelectedSessionIdentity session, DungeonEntryIntent intent)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonEntryIntentDomainService) + "." + nameof(Record));
        _intents[session.CharacterId] = new OwnedIntent(session, intent);
    }

    public DungeonEntryIntent? Find(SelectedSessionIdentity session) =>
        _intents.TryGetValue(session.CharacterId, out var owned) && owned.Session == session ? owned.Intent : null;

    public DungeonEntryIntent? Authorize(SelectedSessionIdentity session, int dungeonId, bool mirror)
    {
        if (Find(session) is not { } intent) return null;
        var bound = intent.Kind is DungeonEntryIntentKind.Directed or DungeonEntryIntentKind.Cycle or DungeonEntryIntentKind.OrdinaryRechallenge;
        return bound && intent.DirectedDungeonId != dungeonId && !mirror
            && !(intent.Kind == DungeonEntryIntentKind.Directed
                && intent.DirectedDungeonId is { } prior && sao.Supports(prior) && sao.Supports(dungeonId))
            ? null : intent;
    }

    public bool TryReplaceExact(SelectedSessionIdentity session, DungeonEntryIntent expected, DungeonEntryIntent replacement)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonEntryIntentDomainService) + "." + nameof(TryReplaceExact));
        return _intents.TryGetValue(session.CharacterId, out var owned) && owned.Session == session && owned.Intent == expected
            && _intents.TryUpdate(session.CharacterId, new(session, replacement), owned);
    }

    public DungeonEntryIntent? Consume(SelectedSessionIdentity session)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonEntryIntentDomainService) + "." + nameof(Consume));
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

    public void RestoreIfAbsent(SelectedSessionIdentity session, DungeonEntryIntent intent)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonEntryIntentDomainService) + "." + nameof(RestoreIfAbsent));
        _intents.TryAdd(session.CharacterId, new OwnedIntent(session, intent));
    }

    public bool ClearExact(SelectedSessionIdentity session)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonEntryIntentDomainService) + "." + nameof(ClearExact));
        if (!_intents.TryGetValue(session.CharacterId, out var owned) || owned.Session != session)
            return false;

        var pair = new KeyValuePair<CharacterId, OwnedIntent>(session.CharacterId, owned);
        return ((ICollection<KeyValuePair<CharacterId, OwnedIntent>>)_intents).Remove(pair);
    }

    private sealed record OwnedIntent(
        SelectedSessionIdentity Session,
        DungeonEntryIntent Intent);
}
