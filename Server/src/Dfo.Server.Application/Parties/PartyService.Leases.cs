using System.Collections.Immutable;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Application.Parties;

public sealed partial class PartyService
{
    private const int MaximumLeasedSquads = 10;
    private readonly Dictionary<Guid, PartyLease> leases = [];

    public PartyLease Rent(Guid owner, int count)
    {
        GameplayWriteAssertion.Writing(nameof(PartyService) + "." + nameof(Rent));
        if (owner == Guid.Empty)
            throw new ArgumentException("A party lease requires an owner.", nameof(owner));
        if (count is < 1 or > MaximumLeasedSquads)
            throw new ArgumentOutOfRangeException(nameof(count));

        lock (sync)
        {
            if (leases.TryGetValue(owner, out var current))
            {
                if (current.Keys.Length != count)
                    throw new InvalidOperationException("The owner already has a lease of a different size.");
                return current;
            }

            var finalGeneration = checked(nextGeneration + count);
            var keys = ImmutableArray.CreateBuilder<PartyKey>(count);
            for (var i = 0; i < count; i++)
                keys.Add(PartyKey.Real(nextGeneration + i + 1));

            var lease = new PartyLease(owner, keys.MoveToImmutable());
            foreach (var key in lease.Keys)
            {
                parties.Add(key, new PartyInstance(key.Value, modeRules.DefaultSettings,
                    PartyTitle.Empty, modeRules.MaximumCapacity, lease));
            }
            leases.Add(owner, lease);
            nextGeneration = finalGeneration;
            return lease;
        }
    }

    public PartyLeaseMutation? Replace(PartyLease lease, IReadOnlyList<PartyLeaseSquad> squads)
    {
        GameplayWriteAssertion.Writing(nameof(PartyService) + "." + nameof(Replace));
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(squads);

        lock (sync)
        {
            if (!OwnsLease(lease) || ReadLeasedRosters(lease, squads) is not { } rosters)
                return null;

            var changes = new List<LeaseRosterChange>();
            for (var i = 0; i < lease.Keys.Length; i++)
            {
                var party = parties[lease.Keys[i]];
                var previous = party.Members.OfType<PartyMemberState>()
                    .Select(member => member.Character).ToArray();
                var roster = rosters[i] ?? [];
                if (previous.SequenceEqual(roster))
                    continue;

                var previousLeader = previous.FirstOrDefault()?.CharacterId;
                var nextLeader = roster.FirstOrDefault()?.CharacterId;
                var clearAudience = previous.Where(member => previousLeader != nextLeader
                    || !roster.Any(next => member.CharacterId == next.CharacterId));
                changes.Add(new LeaseRosterChange(party, roster, previous,
                    [.. clearAudience.Select(member => member.CharacterId)], party.LeadershipVersion));
            }

            // Remove every old mapping before adding any new ones, so full squads can exchange members.
            foreach (var change in changes)
            {
                foreach (var member in change.Previous)
                    membership.Remove(member.CharacterId);
            }
            foreach (var change in changes)
            {
                change.Party.SetLeasedRoster(change.Roster);
                foreach (var member in change.Roster)
                {
                    membership.Add(member.CharacterId, change.Party);
                    ForgetRequestsInvolving(member.CharacterId);
                }
            }

            return new PartyLeaseMutation(changes.Select(change =>
            {
                var hasClear = !change.ClearAudience.IsEmpty;
                var party = change.Party;
                return new PartyNotificationPlan(change.ClearAudience,
                    party.MemberCount == 0 ? null : Snapshot(party),
                    SettingsChange: party.MemberCount != 0 && (change.Previous.Length == 0
                        || change.PreviousLeadershipVersion != party.LeadershipVersion)
                            ? PartySettingsChangeKind.Created : null,
                    ClearPartyKey: hasClear ? party.Key : null,
                    ClearLeadershipVersion: hasClear ? change.PreviousLeadershipVersion : null,
                    ClearPartyGeneration: hasClear ? party.Generation : null,
                    ClearPartyRevision: hasClear ? party.Revision : null);
            }).ToArray());
        }
    }

    public bool Return(PartyLease lease)
    {
        GameplayWriteAssertion.Writing(nameof(PartyService) + "." + nameof(Return));
        ArgumentNullException.ThrowIfNull(lease);

        lock (sync)
        {
            if (!OwnsLease(lease) || lease.Keys.Any(key => parties[key].MemberCount != 0))
                return false;

            foreach (var key in lease.Keys)
                parties.Remove(key);
            return leases.Remove(lease.Owner);
        }
    }

    private bool IsLeased(CharacterId characterId) =>
        membership.TryGetValue(characterId, out var party) && party.Lease is not null;

    private bool OwnsLease(PartyLease lease) =>
        leases.TryGetValue(lease.Owner, out var current) && ReferenceEquals(lease, current);

    private PartyCharacterSnapshot[]?[]? ReadLeasedRosters(PartyLease lease, IReadOnlyList<PartyLeaseSquad> squads)
    {
        var rosters = new PartyCharacterSnapshot[]?[lease.Keys.Length];
        var members = new HashSet<CharacterId>();
        foreach (var squad in squads)
        {
            if (squad is null || squad.Number < 1 || squad.Number > rosters.Length
                || rosters[squad.Number - 1] is not null || squad.Members is null
                || squad.Members.Count > modeRules.MaximumCapacity)
            {
                return null;
            }

            var roster = squad.Members.ToArray();
            foreach (var member in roster)
            {
                if (member is null || member.CharacterId.Value <= 0 || !members.Add(member.CharacterId)
                    || (membership.TryGetValue(member.CharacterId, out var party)
                        && !ReferenceEquals(party.Lease, lease)))
                {
                    return null;
                }
            }

            rosters[squad.Number - 1] = roster;
        }
        return rosters;
    }

    private sealed record LeaseRosterChange(
        PartyInstance Party,
        PartyCharacterSnapshot[] Roster,
        PartyCharacterSnapshot[] Previous,
        ImmutableArray<CharacterId> ClearAudience,
        long PreviousLeadershipVersion);
}
