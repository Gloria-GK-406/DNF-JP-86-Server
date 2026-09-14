using System.Collections.Immutable;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Application.Parties;

/// <summary>
/// Owns all process-local party behavior and directory indexes. Transport/session ownership is
/// validated before callers enter this boundary.
/// </summary>
internal sealed class PartyService(
    IPartyMemberStatusDomainService memberStatus,
    IPartyModeRuleService modeRules,
    TimeProvider clock) : IPartyService
{
    internal static readonly TimeSpan InvitationLifetime = TimeSpan.FromSeconds(30);

    private readonly object sync = new();
    private readonly Dictionary<ushort, PartyState> parties = [];
    private readonly Dictionary<CharacterId, PartyState> membership = [];
    private readonly Dictionary<CharacterId, PartyEndpoint> endpoints = [];
    private readonly Dictionary<CharacterId, PendingInvite> invitations = [];
    private readonly Dictionary<PartyDirectoryScope, SortedSet<ushort>> directory = [];
    private ushort nextPartyId = 1;
    private long nextGeneration;

    public void UpdateDirectoryPresence(CharacterId characterId, PartyDirectoryScope? scope)
    {
        lock (sync)
        {
            if (!membership.TryGetValue(characterId, out var party)
                || party.Find(characterId) is not { } member
                || member.Character.DirectoryScope == scope)
            {
                return;
            }

            var previous = party.DirectoryScope;
            member.Character = member.Character with { DirectoryScope = scope };
            if (member.Slot == 0)
                Reindex(party, previous, party.DirectoryScope);
            party.Revision++;
        }
    }

    public IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Scope);
        if (query.Scope.ChannelId <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "A party directory query requires a logical channel.");

        lock (sync)
        {
            if (!directory.TryGetValue(query.Scope, out var partyIds))
                return [];

            return partyIds
                .Select(partyId => parties[partyId])
                .Where(party => query.Category is null || party.Settings.Category == query.Category)
                .Where(party => query.ModeTargetRaw is null
                    || party.Settings.ModeTargetRaw == query.ModeTargetRaw)
                .Select(Snapshot)
                .ToArray();
        }
    }

    public PartyOperationResult CreateOrUpdateSettings(
        PartyCharacterSnapshot character,
        PartySettings settings,
        PartyTitle title)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(title);

        lock (sync)
        {
            settings = modeRules.Normalize(settings);
            if (membership.TryGetValue(character.CharacterId, out var party))
            {
                var member = party.Find(character.CharacterId)
                    ?? throw new InvalidOperationException("Party membership index is inconsistent.");
                if (member.Slot != 0)
                    return PartyOperationResult.Reject(PartyFailureReason.NotPartyLeader);

                var validation = modeRules.Validate(settings, party.MemberCount);
                if (validation != PartyModeRuleFailure.None)
                    return PartyOperationResult.Reject(Map(validation));

                var previous = party.DirectoryScope;
                member.Character = character;
                party.Settings = settings;
                party.Title = title;
                party.Revision++;
                Reindex(party, previous, party.DirectoryScope);
                var snapshot = Snapshot(party);
                return PartyOperationResult.Success(new PartyNotificationPlan(
                    [], snapshot,
                    SettingsChange: PartySettingsChangeKind.Updated,
                    DirectoryChange: new PartyDirectoryChange(previous, party.DirectoryScope)));
            }

            var createValidation = modeRules.Validate(settings, memberCount: 1);
            if (createValidation != PartyModeRuleFailure.None)
                return PartyOperationResult.Reject(Map(createValidation));

            var created = new PartyState(
                AllocatePartyId(), checked(++nextGeneration), character, settings, title,
                modeRules.MaximumCapacity);
            parties.Add(created.PartyId, created);
            membership.Add(character.CharacterId, created);
            AddToDirectory(created.DirectoryScope, created.PartyId);
            var createdSnapshot = Snapshot(created);
            return PartyOperationResult.Success(new PartyNotificationPlan(
                [], createdSnapshot,
                SettingsChange: PartySettingsChangeKind.Created,
                DirectoryChange: new PartyDirectoryChange(null, created.DirectoryScope)));
        }
    }

    public PartyOperationResult UpdateEndpoint(
        PartyCharacterSnapshot character,
        PartyEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsValid)
            return PartyOperationResult.Reject(PartyFailureReason.InvalidEndpoint);

        lock (sync)
        {
            endpoints[character.CharacterId] = endpoint;
            if (membership.TryGetValue(character.CharacterId, out var party)
                && party.Find(character.CharacterId) is { } member)
            {
                var previous = party.DirectoryScope;
                member.Character = character;
                if (member.Slot == 0)
                    Reindex(party, previous, party.DirectoryScope);
                party.Revision++;
            }
            return PartyOperationResult.Success();
        }
    }

    public PartyOperationResult RequestInvite(
        PartyCharacterSnapshot inviter,
        PartyCharacterSnapshot target,
        PartyInviteDisplayState display)
    {
        ArgumentNullException.ThrowIfNull(inviter);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(display);

        lock (sync)
        {
            if (target.CharacterId == inviter.CharacterId)
                return PartyOperationResult.Reject(PartyFailureReason.SelfInvite);
            var domainFailure = ValidateTownPair(inviter, target);
            if (domainFailure != PartyFailureReason.None)
                return PartyOperationResult.Reject(domainFailure);
            if (membership.ContainsKey(target.CharacterId))
                return PartyOperationResult.Reject(PartyFailureReason.TargetAlreadyInParty);

            long? inviterPartyGeneration = null;
            if (membership.TryGetValue(inviter.CharacterId, out var inviterParty))
            {
                var member = inviterParty.Find(inviter.CharacterId)
                    ?? throw new InvalidOperationException("Party membership index is inconsistent.");
                if (member.Slot != 0)
                    return PartyOperationResult.Reject(PartyFailureReason.InviterNotLeader);
                var admission = modeRules.ValidateAdmission(
                    inviterParty.Settings, inviterParty.MemberCount);
                if (admission != PartyModeRuleFailure.None)
                    return PartyOperationResult.Reject(MapAdmission(admission));
                inviterPartyGeneration = inviterParty.Generation;
            }
            else
            {
                var admission = modeRules.ValidateAdmission(
                    modeRules.DefaultSettings, currentMemberCount: 1);
                if (admission != PartyModeRuleFailure.None)
                    return PartyOperationResult.Reject(MapAdmission(admission));
            }

            invitations[target.CharacterId] = new PendingInvite(
                inviter.CharacterId, target.CharacterId, inviterPartyGeneration,
                clock.GetUtcNow() + InvitationLifetime);
            return PartyOperationResult.Success(new PartyNotificationPlan(
                [],
                Invitation: new PartyInviteNotification(
                    target.CharacterId, inviter.WireUserId, display)));
        }
    }

    public PartyOperationResult AcceptInvite(
        PartyCharacterSnapshot invitee,
        PartyCharacterSnapshot inviter)
    {
        ArgumentNullException.ThrowIfNull(invitee);
        ArgumentNullException.ThrowIfNull(inviter);

        lock (sync)
        {
            if (!invitations.TryGetValue(invitee.CharacterId, out var invitation)
                || invitation.InviteeCharacterId != invitee.CharacterId
                || invitation.InviterCharacterId != inviter.CharacterId)
            {
                return PartyOperationResult.Reject(PartyFailureReason.InviteNotFoundOrStale);
            }
            if (clock.GetUtcNow() >= invitation.ExpiresAt)
                return PartyOperationResult.Reject(PartyFailureReason.InviteExpired);

            var domainFailure = ValidateTownPair(inviter, invitee);
            if (domainFailure != PartyFailureReason.None)
                return PartyOperationResult.Reject(domainFailure);
            if (membership.ContainsKey(invitee.CharacterId))
                return PartyOperationResult.Reject(PartyFailureReason.TargetAlreadyInParty);

            PartyState party;
            var createsParty = false;
            PartyDirectoryScope? previousDirectoryScope;
            if (invitation.InviterPartyGeneration is { } expectedGeneration)
            {
                if (!membership.TryGetValue(inviter.CharacterId, out party!)
                    || party.Generation != expectedGeneration)
                {
                    return PartyOperationResult.Reject(PartyFailureReason.PartyGenerationChanged);
                }
                var inviterMember = party.Find(inviter.CharacterId)
                    ?? throw new InvalidOperationException("Party membership index is inconsistent.");
                if (inviterMember.Slot != 0)
                    return PartyOperationResult.Reject(PartyFailureReason.InviterNotLeader);
                var admission = modeRules.ValidateAdmission(party.Settings, party.MemberCount);
                if (admission != PartyModeRuleFailure.None)
                    return PartyOperationResult.Reject(MapAdmission(admission));
                previousDirectoryScope = party.DirectoryScope;
            }
            else
            {
                if (membership.ContainsKey(inviter.CharacterId))
                    return PartyOperationResult.Reject(PartyFailureReason.PartyGenerationChanged);
                var defaults = modeRules.DefaultSettings;
                var admission = modeRules.ValidateAdmission(defaults, currentMemberCount: 1);
                if (admission != PartyModeRuleFailure.None)
                    return PartyOperationResult.Reject(MapAdmission(admission));
                party = new PartyState(
                    AllocatePartyId(), checked(nextGeneration + 1), inviter, defaults,
                    PartyTitle.Empty, modeRules.MaximumCapacity);
                createsParty = true;
                previousDirectoryScope = null;
            }

            var slot = party.FindAvailableSlot()
                ?? throw new InvalidOperationException(
                    "Party rules admitted a member without an available physical slot.");
            if (createsParty)
            {
                nextGeneration = party.Generation;
                parties.Add(party.PartyId, party);
                membership.Add(inviter.CharacterId, party);
                AddToDirectory(party.DirectoryScope, party.PartyId);
            }
            else
            {
                var leader = party.Members[0]
                    ?? throw new InvalidOperationException("A party has no leader.");
                leader.Character = inviter;
                Reindex(party, previousDirectoryScope, party.DirectoryScope);
            }

            invitations.Remove(invitee.CharacterId);
            party.Add(invitee, slot);
            membership.Add(invitee.CharacterId, party);
            var snapshot = Snapshot(party);
            return PartyOperationResult.Success(new PartyNotificationPlan(
                [], snapshot,
                Acceptance: new PartyAcceptanceNotification(
                    inviter.CharacterId, invitee.CharacterId,
                    inviter.WireUserId, invitee.WireUserId),
                DirectoryChange: new PartyDirectoryChange(
                    previousDirectoryScope, party.DirectoryScope)));
        }
    }

    public PartyOperationResult LeaveParty(CharacterId characterId)
    {
        lock (sync)
            return LeaveLocked(characterId);
    }

    public PartyOperationResult TransferLeadership(
        CharacterId currentLeaderCharacterId,
        CharacterId nextLeaderCharacterId)
    {
        lock (sync)
        {
            if (!membership.TryGetValue(currentLeaderCharacterId, out var party)
                || party.Find(currentLeaderCharacterId) is not { } currentLeader)
            {
                return PartyOperationResult.Reject(PartyFailureReason.NotInParty);
            }
            if (currentLeader.Slot != 0)
                return PartyOperationResult.Reject(PartyFailureReason.NotPartyLeader);
            if (currentLeaderCharacterId == nextLeaderCharacterId)
                return PartyOperationResult.Success();
            if (!membership.TryGetValue(nextLeaderCharacterId, out var nextParty)
                || !ReferenceEquals(nextParty, party)
                || party.Find(nextLeaderCharacterId) is not { } nextLeader)
            {
                return PartyOperationResult.Reject(PartyFailureReason.TargetNotFound);
            }

            var previous = party.DirectoryScope;
            party.Members[0] = new PartyMemberState(nextLeader.Character, 0);
            party.Members[nextLeader.Slot] = new PartyMemberState(
                currentLeader.Character,
                nextLeader.Slot);
            party.Revision++;
            Reindex(party, previous, party.DirectoryScope);
            var snapshot = Snapshot(party);
            return PartyOperationResult.Success(new PartyNotificationPlan(
                [],
                snapshot,
                DirectoryChange: new PartyDirectoryChange(previous, party.DirectoryScope)));
        }
    }

    public PartyOperationResult CleanupCharacter(CharacterId characterId)
    {
        lock (sync)
        {
            endpoints.Remove(characterId);
            foreach (var targetCharacterId in invitations
                         .Where(pair => pair.Value.InviterCharacterId == characterId
                             || pair.Value.InviteeCharacterId == characterId)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                invitations.Remove(targetCharacterId);
            }

            return membership.ContainsKey(characterId)
                ? LeaveLocked(characterId)
                : PartyOperationResult.Success();
        }
    }

    public PartySnapshot? FindParty(CharacterId characterId)
    {
        lock (sync)
            return membership.TryGetValue(characterId, out var party) ? Snapshot(party) : null;
    }

    public PartyEndpoint? FindEndpoint(CharacterId characterId)
    {
        lock (sync)
            return endpoints.GetValueOrDefault(characterId);
    }

    private PartyOperationResult LeaveLocked(CharacterId characterId)
    {
        if (!membership.TryGetValue(characterId, out var party)
            || party.Find(characterId) is not { } member)
        {
            return PartyOperationResult.Reject(PartyFailureReason.NotInParty);
        }

        var previous = party.DirectoryScope;
        if (member.Slot == 0)
        {
            party.Revision++;
            var audience = party.Members
                .Where(value => value is not null)
                .Select(value => value!.Character.CharacterId)
                .ToImmutableArray();
            foreach (var partyMember in party.Members)
            {
                if (partyMember is not null)
                    membership.Remove(partyMember.Character.CharacterId);
            }
            RemoveFromDirectory(previous, party.PartyId);
            parties.Remove(party.PartyId);
            return PartyOperationResult.Success(new PartyNotificationPlan(
                audience,
                ClearPartyId: party.PartyId,
                ClearPartyGeneration: party.Generation,
                ClearPartyRevision: party.Revision,
                DirectoryChange: new PartyDirectoryChange(previous, null)));
        }

        party.Members[member.Slot] = null;
        party.Revision++;
        membership.Remove(characterId);
        var snapshot = Snapshot(party);
        return PartyOperationResult.Success(new PartyNotificationPlan(
            [characterId], snapshot,
            ClearPartyId: party.PartyId,
            ClearPartyGeneration: party.Generation,
            ClearPartyRevision: party.Revision,
            DirectoryChange: new PartyDirectoryChange(previous, party.DirectoryScope)));
    }

    private static PartyFailureReason ValidateTownPair(
        PartyCharacterSnapshot inviter,
        PartyCharacterSnapshot target)
    {
        if (inviter.DirectoryScope is not { } inviterScope
            || target.DirectoryScope is not { } targetScope)
        {
            return PartyFailureReason.NotStandingInTown;
        }
        if (inviterScope.ChannelId != targetScope.ChannelId)
            return PartyFailureReason.NotInSameGameChannel;
        if (inviterScope.TownId != targetScope.TownId
            || inviterScope.AreaId != targetScope.AreaId)
        {
            return PartyFailureReason.NotInSameTownArea;
        }
        return PartyFailureReason.None;
    }

    private PartySnapshot Snapshot(PartyState party)
    {
        var members = ImmutableArray.CreateBuilder<PartyMemberSnapshot>(party.Members.Length);
        foreach (var member in party.Members)
        {
            if (member is null)
                continue;
            members.Add(new PartyMemberSnapshot(
                member.Character,
                member.Slot,
                endpoints.GetValueOrDefault(member.Character.CharacterId),
                memberStatus.CurrentHpPercentOf(member.Character.CharacterId)));
        }
        return new PartySnapshot(
            party.PartyId, party.Generation, party.Revision, party.LeaderWireUserId,
            party.Settings, party.Title, members.ToImmutable());
    }

    private void Reindex(
        PartyState party,
        PartyDirectoryScope? previous,
        PartyDirectoryScope? current)
    {
        if (previous == current)
            return;
        RemoveFromDirectory(previous, party.PartyId);
        AddToDirectory(current, party.PartyId);
    }

    private void AddToDirectory(PartyDirectoryScope? scope, ushort partyId)
    {
        if (scope is null)
            return;
        if (!directory.TryGetValue(scope, out var partyIds))
        {
            partyIds = [];
            directory.Add(scope, partyIds);
        }
        partyIds.Add(partyId);
    }

    private void RemoveFromDirectory(PartyDirectoryScope? scope, ushort partyId)
    {
        if (scope is null || !directory.TryGetValue(scope, out var partyIds))
            return;
        partyIds.Remove(partyId);
        if (partyIds.Count == 0)
            directory.Remove(scope);
    }

    private static PartyFailureReason Map(PartyModeRuleFailure failure) => failure switch
    {
        PartyModeRuleFailure.UnknownCategory => PartyFailureReason.UnknownPartyCategory,
        PartyModeRuleFailure.InvalidCapacity => PartyFailureReason.InvalidPartyCapacity,
        PartyModeRuleFailure.CapacityBelowMemberCount => PartyFailureReason.PartyCapacityBelowMemberCount,
        PartyModeRuleFailure.InvalidModeTarget => PartyFailureReason.InvalidPartyModeTarget,
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null),
    };

    private static PartyFailureReason MapAdmission(PartyModeRuleFailure failure) => failure switch
    {
        PartyModeRuleFailure.CapacityBelowMemberCount => PartyFailureReason.PartyFull,
        _ => Map(failure),
    };

    private ushort AllocatePartyId()
    {
        for (var attempts = 0; attempts < ushort.MaxValue - 1; attempts++)
        {
            var candidate = nextPartyId++;
            if (nextPartyId is 0 or ushort.MaxValue)
                nextPartyId = 1;
            if (candidate is not 0 and not ushort.MaxValue && !parties.ContainsKey(candidate))
                return candidate;
        }
        throw new InvalidOperationException("No party identifier is available.");
    }

    private sealed class PartyState
    {
        internal PartyState(
            ushort partyId,
            long generation,
            PartyCharacterSnapshot leader,
            PartySettings settings,
            PartyTitle title,
            byte maximumCapacity)
        {
            if (maximumCapacity == 0)
                throw new ArgumentOutOfRangeException(nameof(maximumCapacity));
            PartyId = partyId;
            Generation = generation;
            Settings = settings;
            Title = title;
            Members = new PartyMemberState?[maximumCapacity];
            Members[0] = new PartyMemberState(leader, 0);
        }

        internal ushort PartyId { get; }
        internal long Generation { get; }
        internal long Revision { get; set; } = 1;
        internal ushort LeaderWireUserId => Members[0]!.Character.WireUserId;
        internal PartyDirectoryScope? DirectoryScope => Members[0]!.Character.DirectoryScope;
        internal PartySettings Settings { get; set; }
        internal PartyTitle Title { get; set; }
        internal PartyMemberState?[] Members { get; }
        internal int MemberCount => Members.Count(member => member is not null);

        internal PartyMemberState? Find(CharacterId characterId) =>
            Members.FirstOrDefault(member => member?.Character.CharacterId == characterId);

        internal byte? FindAvailableSlot()
        {
            for (byte slot = 1; slot < Members.Length; slot++)
            {
                if (Members[slot] is null)
                    return slot;
            }
            return null;
        }

        internal void Add(PartyCharacterSnapshot character, byte slot)
        {
            if (slot == 0 || slot >= Members.Length || Members[slot] is not null)
                throw new InvalidOperationException("The selected party slot is not available.");
            Members[slot] = new PartyMemberState(character, slot);
            Revision++;
        }
    }

    private sealed class PartyMemberState(PartyCharacterSnapshot character, byte slot)
    {
        internal PartyCharacterSnapshot Character { get; set; } = character;
        internal byte Slot { get; } = slot;
    }

    private sealed record PendingInvite(
        CharacterId InviterCharacterId,
        CharacterId InviteeCharacterId,
        long? InviterPartyGeneration,
        DateTimeOffset ExpiresAt);
}
