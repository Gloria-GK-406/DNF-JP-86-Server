using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Application.Parties;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Protocol.Game.Party;

/// <summary>Allocates A21's short-lived wire numbers independently of the party aggregate.</summary>
/// <remarks>
/// A number is retired when its party is disbanded or rekeyed by a departing leader, as 70 returns a
/// disbanded party to its pool (CParty::destroy → PutParty). The cursor only moves forward, so a
/// retired number comes back only after every other free number has been handed out: a client still
/// holding the old number cannot meet it on a new party soon after.
/// </remarks>
public sealed class PartyClientIdRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<(PartyKey Key, long Version), ushort> numbers = [];
    private readonly HashSet<ushort> inUse = [];
    private ushort next = 1;

    public ushort For(PartyKey key, long version)
    {
        GameplayWriteAssertion.Writing(nameof(PartyClientIdRegistry) + "." + nameof(For));
        if (key.Kind != PartyKeyKind.Real || version <= 0)
            throw new ArgumentOutOfRangeException(nameof(key));
        lock (gate)
        {
            if (numbers.TryGetValue((key, version), out var existing))
                return existing;
            for (var attempts = 0; attempts < ushort.MaxValue - 1; attempts++)
            {
                var candidate = next++;
                if (next is 0 or ushort.MaxValue)
                    next = 1;
                if (candidate is not 0 and not ushort.MaxValue
                    && inUse.Add(candidate))
                {
                    numbers.Add((key, version), candidate);
                    return candidate;
                }
            }
            throw new InvalidOperationException("No A21 party number is available.");
        }
    }

    public PartySnapshot Project(PartySnapshot snapshot)
    {
        GameplayWriteAssertion.Writing(nameof(PartyClientIdRegistry) + "." + nameof(Project));
        return snapshot.IsVirtual || snapshot.Key == default
            ? snapshot
            : snapshot with { PartyId = For(snapshot.Key, snapshot.LeadershipVersion) };
    }

    public PartyNotificationPlan Project(PartyNotificationPlan plan)
    {
        GameplayWriteAssertion.Writing(nameof(PartyClientIdRegistry) + "." + nameof(Project));
        var projected = plan with
        {
            Snapshot = plan.Snapshot is { } snapshot ? Project(snapshot) : null,
            LeadershipTransfer = plan.LeadershipTransfer is { } transfer ? Project(transfer) : null,
            ClearPartyId = plan.ClearPartyKey is { } key
                && plan.ClearLeadershipVersion is { } version
                    ? For(key, version)
                    : plan.ClearPartyId,
        };
        // The cleared number is final once no surviving snapshot carries it: the party is gone, or
        // a departing leader's successor took the party under a new version.
        if (plan.ClearPartyKey is { } clearedKey
            && plan.ClearLeadershipVersion is { } clearedVersion
            && (plan.Snapshot is null || plan.Snapshot.LeadershipVersion != clearedVersion))
        {
            Retire(clearedKey, clearedVersion);
        }
        return projected;
    }

    private void Retire(PartyKey key, long version)
    {
        lock (gate)
        {
            if (numbers.Remove((key, version), out var number))
                inUse.Remove(number);
        }
    }
}

/// <summary>Protocol-facing projection over the pure PartyService; it owns no membership.</summary>
public sealed class PartyClientProjectionService(
    PartyService inner,
    PartyClientIdRegistry ids) : IPartyService
{
    public IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query) =>
        inner.Query(query).Select(ids.Project).ToArray();

    public PartySnapshot FindParty(PartyCharacterSnapshot character) =>
        ids.Project(inner.FindParty(character));

    public PartyMembershipSnapshot? FindByKey(PartyKey key) => inner.FindByKey(key);

    public PartyKey KeyOf(CharacterId characterId) => inner.KeyOf(characterId);

    public PartyOperationResult CreateOrUpdateSettings(
        PartyCharacterSnapshot character, PartySettings settings, PartyTitle title) =>
        Project(inner.CreateOrUpdateSettings(character, settings, title));

    public PartyOperationResult RequestInvite(
        PartyCharacterSnapshot inviter, PartyCharacterSnapshot target,
        PartyInviteDisplayState display) =>
        Project(inner.RequestInvite(inviter, target, display));

    public PartyOperationResult AcceptInvite(
        PartyCharacterSnapshot invitee, PartyCharacterSnapshot inviter) =>
        Project(inner.AcceptInvite(invitee, inviter));

    public PartyOperationResult TransferLeadership(
        CharacterId currentLeaderCharacterId, CharacterId nextLeaderCharacterId) =>
        Project(inner.TransferLeadership(currentLeaderCharacterId, nextLeaderCharacterId));

    public PartyOperationResult RemoveMember(
        CharacterId currentLeaderCharacterId, CharacterId targetCharacterId) =>
        Project(inner.RemoveMember(currentLeaderCharacterId, targetCharacterId));

    public PartyOperationResult LeaveParty(CharacterId characterId) =>
        Project(inner.LeaveParty(characterId));

    public PartyOperationResult CleanupCharacter(CharacterId characterId) =>
        Project(inner.CleanupCharacter(characterId));

    private PartyOperationResult Project(PartyOperationResult result) =>
        result.Succeeded
            ? result with { Notifications = ids.Project(result.Notifications) }
            : result;
}
