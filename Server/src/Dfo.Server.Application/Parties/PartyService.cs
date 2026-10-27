using Dfo.Server.Abstractions.Execution;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Application.Parties;

/// <summary>
/// Owns all process-local party behavior and directory indexes. Transport/session ownership is
/// validated before callers enter this boundary.
/// </summary>
public sealed class PartyService(
    IPartyMemberStatusDomainService memberStatus,
    IPartyModeRuleService modeRules,
    TimeProvider clock,
    IPartyMemberSessionEndpointSource sessionEndpoints) : IPartyService
{
    internal static readonly TimeSpan InvitationLifetime = TimeSpan.FromSeconds(30);

    private readonly object sync = new();
    private readonly Dictionary<PartyKey, PartyInstance> parties = [];
    private readonly Dictionary<CharacterId, PartyInstance> membership = [];
    private readonly Dictionary<CharacterId, PendingInvite> invitations = [];
    private readonly Dictionary<PartyDirectoryScope, SortedSet<PartyKey>> directory = [];
    private long nextGeneration;

    public void UpdateDirectoryPresence(CharacterId characterId, PartyDirectoryScope? scope)
    {
        GameplayWriteAssertion.Writing(nameof(PartyService) + "." + nameof(UpdateDirectoryPresence));
        lock (sync)
        {
            if (!membership.TryGetValue(characterId, out var party)
                || party.Find(characterId) is not { } member
                || member.Character.DirectoryScope == scope)
            {
                return;
            }

            var previous = party.DirectoryScope;
            party.UpdatePresence(characterId, scope);
            Reindex(party, previous, party.DirectoryScope);
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
            if (!directory.TryGetValue(query.Scope, out var partyKeys))
                return [];

            return partyKeys
                .Select(key => parties[key])
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
        GameplayWriteAssertion.Writing(nameof(PartyService) + "." + nameof(CreateOrUpdateSettings));
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
                if (!party.IsLeader(character.CharacterId))
                    return PartyOperationResult.Reject(PartyFailureReason.NotPartyLeader);

                var validation = modeRules.Validate(settings, party.MemberCount);
                if (validation != PartyModeRuleFailure.None)
                    return PartyOperationResult.Reject(Map(validation));

                var previous = party.DirectoryScope;
                party.UpdateSettings(character, settings, title);
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

            var created = new PartyInstance(
                checked(++nextGeneration), character, settings, title,
                modeRules.MaximumCapacity);
            parties.Add(created.Key, created);
            membership.Add(character.CharacterId, created);
            AddToDirectory(created.DirectoryScope, created.Key);
            var createdSnapshot = Snapshot(created);
            return PartyOperationResult.Success(new PartyNotificationPlan(
                [], createdSnapshot,
                SettingsChange: PartySettingsChangeKind.Created,
                DirectoryChange: new PartyDirectoryChange(null, created.DirectoryScope)));
        }
    }

    public PartyOperationResult RequestInvite(
        PartyCharacterSnapshot inviter,
        PartyCharacterSnapshot target,
        PartyInviteDisplayState display)
    {
        GameplayWriteAssertion.Writing(nameof(PartyService) + "." + nameof(RequestInvite));
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
            if (membership.TryGetValue(target.CharacterId, out var targetParty))
            {
                // The same native peer request also lets an unattached character apply to
                // an existing leader. Keep request sender/recipient separate from party owner.
                if (membership.ContainsKey(inviter.CharacterId))
                    return PartyOperationResult.Reject(PartyFailureReason.TargetAlreadyInParty);
                if (!targetParty.IsLeader(target.CharacterId))
                    return PartyOperationResult.Reject(PartyFailureReason.NotPartyLeader);
                var admission = modeRules.ValidateAdmission(targetParty.Settings, targetParty.MemberCount);
                if (admission != PartyModeRuleFailure.None)
                    return PartyOperationResult.Reject(MapAdmission(admission));
                invitations[target.CharacterId] = new PendingInvite(inviter.CharacterId, target.CharacterId,
                    null, clock.GetUtcNow() + InvitationLifetime, targetParty.Generation, targetParty.LeadershipVersion);
                return PartyOperationResult.Success(new PartyNotificationPlan([], Invitation:
                    new PartyInviteNotification(target.CharacterId, inviter.WireUserId, display)));
            }

            long? inviterPartyGeneration = null;
            if (membership.TryGetValue(inviter.CharacterId, out var inviterParty))
            {
                var member = inviterParty.Find(inviter.CharacterId)
                    ?? throw new InvalidOperationException("Party membership index is inconsistent.");
                if (!inviterParty.IsLeader(inviter.CharacterId))
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
        GameplayWriteAssertion.Writing(nameof(PartyService) + "." + nameof(AcceptInvite));
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
            if (invitation.JoinPartyGeneration is not null)
                return AcceptJoinLocked(invitee, inviter, invitation);
            if (membership.ContainsKey(invitee.CharacterId))
                return PartyOperationResult.Reject(PartyFailureReason.TargetAlreadyInParty);

            PartyInstance party;
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
                if (!party.IsLeader(inviter.CharacterId))
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
                party = new PartyInstance(
                    checked(nextGeneration + 1), inviter, defaults,
                    PartyTitle.Empty, modeRules.MaximumCapacity);
                createsParty = true;
                previousDirectoryScope = null;
            }

            party.Add(invitee);
            if (createsParty)
            {
                nextGeneration = party.Generation;
                parties.Add(party.Key, party);
                membership.Add(inviter.CharacterId, party);
                AddToDirectory(party.DirectoryScope, party.Key);
            }
            else
            {
                party.RefreshCharacter(inviter);
                Reindex(party, previousDirectoryScope, party.DirectoryScope);
            }

            invitations.Remove(invitee.CharacterId);
            membership.Add(invitee.CharacterId, party);
            var snapshot = Snapshot(party);
            return PartyOperationResult.Success(new PartyNotificationPlan(
                [invitee.CharacterId], snapshot,
                Acceptance: new PartyAcceptanceNotification(
                    inviter.CharacterId, invitee.CharacterId,
                    inviter.WireUserId, invitee.WireUserId),
                ClearPartyKey: party.Key,
                ClearLeadershipVersion: party.LeadershipVersion,
                ClearPartyGeneration: snapshot.Generation,
                ClearPartyRevision: snapshot.Revision,
                DirectoryChange: new PartyDirectoryChange(
                    previousDirectoryScope, party.DirectoryScope)));
        }
    }

    public PartyOperationResult LeaveParty(CharacterId characterId)
    {
        GameplayWriteAssertion.Writing(nameof(PartyService) + "." + nameof(LeaveParty));
        lock (sync)
            return LeaveLocked(characterId);
    }

    private PartyOperationResult AcceptJoinLocked(PartyCharacterSnapshot leader,
        PartyCharacterSnapshot applicant, PendingInvite invitation)
    {
        if (membership.ContainsKey(applicant.CharacterId))
            return PartyOperationResult.Reject(PartyFailureReason.TargetAlreadyInParty);
        if (!membership.TryGetValue(leader.CharacterId, out var party)
            || party.Generation != invitation.JoinPartyGeneration
            || party.LeadershipVersion != invitation.JoinLeadershipVersion)
            return PartyOperationResult.Reject(PartyFailureReason.PartyGenerationChanged);
        if (!party.IsLeader(leader.CharacterId))
            return PartyOperationResult.Reject(PartyFailureReason.NotPartyLeader);
        var admission = modeRules.ValidateAdmission(party.Settings, party.MemberCount);
        if (admission != PartyModeRuleFailure.None)
            return PartyOperationResult.Reject(MapAdmission(admission));
        var previous = party.DirectoryScope;
        party.RefreshCharacter(leader);
        party.Add(applicant);
        membership.Add(applicant.CharacterId, party);
        invitations.Remove(leader.CharacterId);
        Reindex(party, previous, party.DirectoryScope);
        var snapshot = Snapshot(party);
        return PartyOperationResult.Success(new PartyNotificationPlan([applicant.CharacterId], snapshot,
            Acceptance: new PartyAcceptanceNotification(applicant.CharacterId, leader.CharacterId,
                applicant.WireUserId, leader.WireUserId, JoiningCharacterId: applicant.CharacterId),
            ClearPartyKey: party.Key, ClearLeadershipVersion: party.LeadershipVersion,
            ClearPartyGeneration: snapshot.Generation, ClearPartyRevision: snapshot.Revision,
            DirectoryChange: new PartyDirectoryChange(previous, party.DirectoryScope)));
    }

    public PartyOperationResult TransferLeadership(
        CharacterId currentLeaderCharacterId,
        CharacterId nextLeaderCharacterId)
    {
        GameplayWriteAssertion.Writing(nameof(PartyService) + "." + nameof(TransferLeadership));
        lock (sync)
        {
            if (!membership.TryGetValue(currentLeaderCharacterId, out var party)
                || party.Find(currentLeaderCharacterId) is null)
            {
                return PartyOperationResult.Reject(PartyFailureReason.NotInParty);
            }
            if (!party.IsLeader(currentLeaderCharacterId))
                return PartyOperationResult.Reject(PartyFailureReason.NotPartyLeader);
            if (currentLeaderCharacterId == nextLeaderCharacterId)
                return PartyOperationResult.Success();
            if (!membership.TryGetValue(nextLeaderCharacterId, out var nextParty)
                || !ReferenceEquals(nextParty, party)
                || party.Find(nextLeaderCharacterId) is null)
            {
                return PartyOperationResult.Reject(PartyFailureReason.TargetNotFound);
            }

            var previous = party.DirectoryScope;
            party.TransferLeadership(nextLeaderCharacterId);
            Reindex(party, previous, party.DirectoryScope);
            var snapshot = Snapshot(party);
            return PartyOperationResult.Success(new PartyNotificationPlan(
                [],
                snapshot,
                DirectoryChange: new PartyDirectoryChange(previous, party.DirectoryScope),
                LeadershipTransfer: snapshot));
        }
    }

    public PartyOperationResult RemoveMember(
        CharacterId currentLeaderCharacterId,
        CharacterId targetCharacterId)
    {
        GameplayWriteAssertion.Writing(nameof(PartyService) + "." + nameof(RemoveMember));
        lock (sync)
        {
            if (!membership.TryGetValue(currentLeaderCharacterId, out var party))
                return PartyOperationResult.Reject(PartyFailureReason.NotInParty);
            if (!party.IsLeader(currentLeaderCharacterId))
                return PartyOperationResult.Reject(PartyFailureReason.NotPartyLeader);
            if (targetCharacterId == currentLeaderCharacterId
                || !membership.TryGetValue(targetCharacterId, out var targetParty)
                || !ReferenceEquals(targetParty, party))
            {
                return PartyOperationResult.Reject(PartyFailureReason.TargetNotFound);
            }

            var previous = party.DirectoryScope;
            party.Remove(targetCharacterId);
            membership.Remove(targetCharacterId);
            Reindex(party, previous, party.DirectoryScope);
            var snapshot = Snapshot(party);
            return PartyOperationResult.Success(new PartyNotificationPlan(
                [targetCharacterId],
                snapshot,
                ClearPartyKey: party.Key,
                ClearLeadershipVersion: party.LeadershipVersion,
                ClearPartyGeneration: party.Generation,
                ClearPartyRevision: party.Revision,
                DirectoryChange: new PartyDirectoryChange(previous, party.DirectoryScope)));
        }
    }

    public PartyOperationResult CleanupCharacter(CharacterId characterId)
    {
        GameplayWriteAssertion.Writing(nameof(PartyService) + "." + nameof(CleanupCharacter));
        lock (sync)
        {
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

    public PartySnapshot FindParty(PartyCharacterSnapshot character)
    {
        ArgumentNullException.ThrowIfNull(character);
        lock (sync)
            return membership.TryGetValue(character.CharacterId, out var party)
                ? Snapshot(party)
                : PartySnapshot.Virtual(character, modeRules.DefaultSettings);
    }

    public PartyKey KeyOf(CharacterId characterId)
    {
        lock (sync)
            return membership.TryGetValue(characterId, out var party)
                ? party.Key
                : PartyKey.Virtual(characterId);
    }

    public PartyMembershipSnapshot? FindByKey(PartyKey key)
    {
        lock (sync)
        {
            if (key.Kind == PartyKeyKind.Virtual && key.Value is > 0 and <= int.MaxValue)
            {
                var characterId = new CharacterId(checked((int)key.Value));
                return membership.ContainsKey(characterId)
                    ? null
                    : new PartyMembershipSnapshot(
                        key, characterId, modeRules.DefaultSettings, PartyTitle.Empty,
                        [new PartyMembershipMember(characterId, 0)]);
            }

            if (key.Kind != PartyKeyKind.Real || !parties.TryGetValue(key, out var party))
                return null;

            return new PartyMembershipSnapshot(
                key,
                party.LeaderCharacterId,
                party.Settings,
                party.Title,
                [.. party.Members
                    .Where(member => member is not null)
                    .Select(member => new PartyMembershipMember(
                        member!.Character.CharacterId, member.Slot))]);
        }
    }

    private PartyOperationResult LeaveLocked(CharacterId characterId)
    {
        if (!membership.TryGetValue(characterId, out var party)
            || party.Find(characterId) is not { } member)
        {
            return PartyOperationResult.Reject(PartyFailureReason.NotInParty);
        }

        var previous = party.DirectoryScope;
        if (party.MemberCount == 1)
        {
            var audience = party.Members
                .Where(value => value is not null)
                .Select(value => value!.Character.CharacterId)
                .ToImmutableArray();
            party.Remove(characterId);
            membership.Remove(characterId);
            RemoveFromDirectory(previous, party.Key);
            parties.Remove(party.Key);
            return PartyOperationResult.Success(new PartyNotificationPlan(
                audience,
                ClearPartyKey: party.Key,
                ClearLeadershipVersion: party.LeadershipVersion,
                ClearPartyGeneration: party.Generation,
                ClearPartyRevision: party.Revision,
                DirectoryChange: new PartyDirectoryChange(previous, null)));
        }

        long? previousLeadershipVersion = null;
        ImmutableArray<CharacterId> clearAudience = [characterId];
        if (party.IsLeader(characterId))
        {
            previousLeadershipVersion = party.LeadershipVersion;
            clearAudience = party.Members
                .Where(value => value is not null)
                .Select(value => value!.Character.CharacterId)
                .ToImmutableArray();
            party.PromoteBeforeDeparture(characterId);
        }

        party.Remove(characterId);
        membership.Remove(characterId);
        Reindex(party, previous, party.DirectoryScope);
        var snapshot = Snapshot(party);
        return PartyOperationResult.Success(new PartyNotificationPlan(
            clearAudience, snapshot,
            ClearPartyKey: party.Key,
            ClearLeadershipVersion: previousLeadershipVersion ?? party.LeadershipVersion,
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
            || inviterScope.AreaId != targetScope.AreaId || inviterScope.Instance != targetScope.Instance)
        {
            return PartyFailureReason.NotInSameTownArea;
        }
        return PartyFailureReason.None;
    }

    private PartySnapshot Snapshot(PartyInstance party)
    {
        var members = ImmutableArray.CreateBuilder<PartyMemberSnapshot>(party.Members.Length);
        foreach (var member in party.Members)
        {
            if (member is null)
                continue;
            members.Add(new PartyMemberSnapshot(
                member.Character,
                member.Slot,
                sessionEndpoints.FindThroughCurrentSession(member.Character.CharacterId),
                memberStatus.CurrentHpPercentOf(member.Character.CharacterId)));
        }
        return new PartySnapshot(
            0, party.Generation, party.Revision, party.LeaderWireUserId,
            party.Settings, party.Title, members.ToImmutable(), Key: party.Key,
            LeadershipVersion: party.LeadershipVersion);
    }

    private void Reindex(
        PartyInstance party,
        PartyDirectoryScope? previous,
        PartyDirectoryScope? current)
    {
        if (previous == current)
            return;
        RemoveFromDirectory(previous, party.Key);
        AddToDirectory(current, party.Key);
    }

    private void AddToDirectory(PartyDirectoryScope? scope, PartyKey key)
    {
        if (scope is null)
            return;
        if (!directory.TryGetValue(scope, out var partyKeys))
        {
            partyKeys = [];
            directory.Add(scope, partyKeys);
        }
        partyKeys.Add(key);
    }

    private void RemoveFromDirectory(PartyDirectoryScope? scope, PartyKey key)
    {
        if (scope is null || !directory.TryGetValue(scope, out var partyKeys))
            return;
        partyKeys.Remove(key);
        if (partyKeys.Count == 0)
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

    private sealed class PartyInstance
    {
        internal PartyInstance(
            long generation,
            PartyCharacterSnapshot leader,
            PartySettings settings,
            PartyTitle title,
            byte maximumCapacity)
        {
            if (maximumCapacity == 0)
                throw new ArgumentOutOfRangeException(nameof(maximumCapacity));
            Generation = generation;
            Key = PartyKey.Real(generation);
            Settings = settings;
            Title = title;
            Members = new PartyMemberState?[maximumCapacity];
            Members[0] = new PartyMemberState(leader, 0);
            LeaderCharacterId = leader.CharacterId;
        }

        internal CharacterId LeaderCharacterId { get; private set; }
        internal long LeadershipVersion { get; set; } = 1;
        internal PartyKey Key { get; }
        internal long Generation { get; }
        internal long Revision { get; set; } = 1;
        internal ushort LeaderWireUserId => Leader.Character.WireUserId;
        internal PartyDirectoryScope? DirectoryScope => Leader.Character.DirectoryScope;
        internal PartySettings Settings { get; set; }
        internal PartyTitle Title { get; set; }
        internal PartyMemberState?[] Members { get; }
        internal int MemberCount => Members.Count(member => member is not null);
        private PartyMemberState Leader => Find(LeaderCharacterId)
            ?? throw new InvalidOperationException("A party has no leader.");

        internal bool IsLeader(CharacterId characterId) => LeaderCharacterId == characterId;

        internal PartyMemberState? Find(CharacterId characterId) =>
            Members.FirstOrDefault(member => member?.Character.CharacterId == characterId);

        private byte? FindAvailableSlot()
        {
            for (byte slot = 0; slot < Members.Length; slot++)
            {
                if (Members[slot] is null)
                    return slot;
            }
            return null;
        }

        internal void Add(PartyCharacterSnapshot character)
        {
            var slot = FindAvailableSlot()
                ?? throw new InvalidOperationException(
                    "Party rules admitted a member without an available physical slot.");
            Members[slot] = new PartyMemberState(character, slot);
            Revision++;
        }

        internal void UpdatePresence(CharacterId characterId, PartyDirectoryScope? scope)
        {
            var member = Find(characterId)
                ?? throw new InvalidOperationException("Party membership index is inconsistent.");
            member.Character = member.Character with { DirectoryScope = scope };
            Revision++;
        }

        internal void UpdateSettings(
            PartyCharacterSnapshot character, PartySettings settings, PartyTitle title)
        {
            RefreshCharacter(character);
            Settings = settings;
            Title = title;
            Revision++;
        }

        internal void RefreshCharacter(PartyCharacterSnapshot character)
        {
            var member = Find(character.CharacterId)
                ?? throw new InvalidOperationException("Party membership index is inconsistent.");
            member.Character = character;
        }

        internal void TransferLeadership(CharacterId nextLeaderCharacterId)
        {
            if (Find(nextLeaderCharacterId) is null)
                throw new InvalidOperationException("The next leader must be a party member.");
            LeaderCharacterId = nextLeaderCharacterId;
            Revision++;
        }

        internal void PromoteBeforeDeparture(CharacterId departingLeaderCharacterId)
        {
            if (!IsLeader(departingLeaderCharacterId) || MemberCount < 2)
                throw new InvalidOperationException("Only a departing leader can be succeeded.");
            var successor = Members.First(member =>
                member is not null && member.Character.CharacterId != departingLeaderCharacterId)!;
            LeaderCharacterId = successor.Character.CharacterId;
            LeadershipVersion++;
            Revision++;
        }

        internal void Remove(CharacterId characterId)
        {
            var member = Find(characterId)
                ?? throw new InvalidOperationException("Party membership index is inconsistent.");
            if (IsLeader(characterId) && MemberCount > 1)
                throw new InvalidOperationException("The leader must be succeeded before removal.");
            Members[member.Slot] = null;
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
        DateTimeOffset ExpiresAt,
        long? JoinPartyGeneration = null,
        long? JoinLeadershipVersion = null);
}
