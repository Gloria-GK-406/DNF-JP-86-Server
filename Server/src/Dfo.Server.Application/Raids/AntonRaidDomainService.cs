using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Raids;

internal sealed partial class AntonRaidDomainService(
    ISelectedSessionDomainService sessions,
    ITownAreaDomainService towns,
    IPartyService parties,
    ChannelEntryOptions channelOptions,
    TimeProvider clock,
    IPartyLeaseService leases,
    AntonRaidRuleService raidRules,
    IDungeonEntryMaterialsDomainService materials,
    ICharacterBusinessInventoryDomainService inventory,
    IMaterialConsumptionRuleService materialRules,
    IDungeonDeadlineWake deadlines,
    IDomainTransactionAccessor transactions,
    IChannelEntryDomainService channelEntry,
    IDungeonAdmissionDomainService admission,
    IDungeonVisitDomainService visits,
    IDungeonStructureRuleService structures,
    IDungeonRunQueryDomainService dungeonRuns,
    IEventService events) : IAntonRaidDomainService, IDomainEventSubscriber<DungeonParticipantLeft>
{
    private readonly Dictionary<uint, Recruitment> raids = [];
    private readonly Dictionary<CharacterId, Recruitment> memberships = [];
    private readonly Dictionary<int, ushort> lastRaidNumbers = [];
    private readonly Dictionary<DungeonRunId, PartyLease> retiringLeases = [];
    private readonly Dictionary<(SelectedSessionIdentity Recipient, SelectedSessionIdentity Sender), AntonRaidApproval> approvals = [];

    public int? RecruitmentChannel(SelectedSessionIdentity session) =>
        sessions.IsInWorld(session)
        && towns.Find(session.CharacterId) is { } standing
        && standing.Session == session
        && standing.Area.TownId == channelOptions.RaidTownId
        && standing.Area.ChannelId is > 0 and <= byte.MaxValue
            ? standing.Area.ChannelId
            : null;

    public bool IsRecruitingMember(SelectedSessionIdentity session) => Of(session) is not null;

    public IReadOnlyList<AntonRaidRecruitmentSnapshot> Directory(int channel) => raids.Values
        .Where(raid => raid.Channel == channel)
        .OrderBy(raid => raid.Number)
        .Select(raid => raid.Snapshot())
        .ToArray();

    public AntonRaidRecruitmentSnapshot? Find(int channel, uint number) =>
        raids.TryGetValue(number, out var raid) && raid.Channel == channel ? raid.Snapshot() : null;

    public uint? CheckApplicantTarget(SelectedSessionIdentity applicant, SelectedSessionIdentity target) =>
        Of(target) is { } raid && raid.Leader.Session == target && CanJoin(applicant, raid)
            ? raid.Number
            : null;

    public AntonRaidRecruitmentResult Create(AntonRaidMember leader, ReadOnlyMemory<byte> title)
    {
        GameplayWriteAssertion.Writing(nameof(AntonRaidDomainService) + "." + nameof(Create));
        if (RecruitmentChannel(leader.Session) is not { } channel
            || Of(leader.Session) is { CanEdit: false }
            || lastRaidNumbers.GetValueOrDefault(channel) == ushort.MaxValue)
            return AntonRaidRecruitmentResult.Refused;

        var party = Of(leader.Session) is null ? ExitOrdinaryParty(leader.Session) : PartyOperationResult.Success();
        if (!party.Succeeded)
            return AntonRaidRecruitmentResult.Refused;

        var prior = Remove(leader.Session);
        var sequence = checked((ushort)(lastRaidNumbers.GetValueOrDefault(channel) + 1));
        lastRaidNumbers[channel] = sequence;
        var raid = new Recruitment(((uint)channel << 16) | sequence, channel, title.ToArray(),
            leader with { Squad = 0 }, leases.Rent(Guid.NewGuid(), AntonRaidRuleService.SquadCount));
        raids.Add(raid.Number, raid);
        memberships.Add(leader.Session.CharacterId, raid);
        return new(true, raid.Number, prior is null ? [Change(raid)] : [prior, Change(raid)], party.Notifications);
    }

    public AntonRaidApproval? Request(AntonRaidMember sender, AntonRaidMember recipient)
    {
        GameplayWriteAssertion.Writing(nameof(AntonRaidDomainService) + "." + nameof(Request));
        var senderRaid = Of(sender.Session);
        var recipientRaid = Of(recipient.Session);
        var raid = senderRaid ?? recipientRaid;
        if (raid is null || (senderRaid is not null && recipientRaid is not null))
            return null;

        var leader = senderRaid is not null ? sender : recipient;
        var applicant = senderRaid is not null ? recipient : sender;
        if (raid.Leader.Session != leader.Session || !CanJoin(applicant.Session, raid))
            return null;

        var now = clock.GetUtcNow();
        foreach (var expired in approvals.Where(entry => entry.Value.ExpiresAt <= now).Select(entry => entry.Key).ToArray())
            approvals.Remove(expired);

        var key = (recipient.Session, sender.Session);
        if (approvals.ContainsKey(key))
            return null;

        var approval = new AntonRaidApproval(sender.Session, recipient.Session, applicant, leader.Session, raid.Number, now + AntonRaidRuleService.ApprovalLifetime);
        approvals.Add(key, approval);
        return approval;
    }

    public AntonRaidRecruitmentResult Respond(SelectedSessionIdentity recipient, SelectedSessionIdentity sender, bool accepted)
    {
        GameplayWriteAssertion.Writing(nameof(AntonRaidDomainService) + "." + nameof(Respond));
        if (!approvals.Remove((recipient, sender), out var approval)
            || approval.ExpiresAt <= clock.GetUtcNow()
            || !raids.TryGetValue(approval.Number, out var raid)
            || raid.Leader.Session != approval.Leader
            || !CanJoin(approval.Applicant.Session, raid))
            return AntonRaidRecruitmentResult.Refused;
        if (!accepted)
            return AntonRaidRecruitmentResult.Declined;

        var party = ExitOrdinaryParty(approval.Applicant.Session);
        if (!party.Succeeded)
            return AntonRaidRecruitmentResult.Refused;

        raid.Members.Add(approval.Applicant with { Squad = 0, DisconnectedAt = null, RejoinUntil = null });
        memberships.Add(approval.Applicant.Session.CharacterId, raid);
        InvalidateApprovals(approval.Applicant.Session);
        return new(true, raid.Number, [Change(raid)], party.Notifications);
    }

    public AntonRaidRecruitmentResult Assign(SelectedSessionIdentity sender, ushort target, uint squad)
    {
        GameplayWriteAssertion.Writing(nameof(AntonRaidDomainService) + "." + nameof(Assign));
        if (Of(sender) is not { } raid || RecruitmentChannel(sender) != raid.Channel || squad > AntonRaidRuleService.SquadCount
            || (raid.Stage == AntonRaidStage.Recruiting
                ? sender.WireUserId != target && raid.Leader.Session != sender
                : !CanEditActiveSquads(raid) || raid.Leader.Session != sender))
            return AntonRaidRecruitmentResult.Refused;

        var index = raid.Members.FindIndex(member => member.Session.WireUserId == target && member.RejoinUntil is null);
        if (index < 0 || (raid.Stage != AntonRaidStage.Recruiting
            && !CanMoveIdleMember(raid, raid.Members[index], (byte)squad)))
            return AntonRaidRecruitmentResult.Refused;
        return MoveMember(raid, index, (byte)squad);
    }

    public AntonRaidRecruitmentResult Leave(SelectedSessionIdentity sender, ushort target)
    {
        GameplayWriteAssertion.Writing(nameof(AntonRaidDomainService) + "." + nameof(Leave));
        if (Of(sender) is not { } raid
            || (sender.WireUserId != target && (!raid.CanEdit || raid.Leader.Session != sender)))
            return AntonRaidRecruitmentResult.Refused;
        var member = raid.Members.Find(candidate => candidate.Session.WireUserId == target && candidate.RejoinUntil is null);
        return member is not null && Remove(member.Session) is { } change
            ? new(true, change.Number, [change], PartyNotificationPlan.Empty)
            : AntonRaidRecruitmentResult.Refused;
    }

    private Recruitment? Of(SelectedSessionIdentity session) =>
        memberships.TryGetValue(session.CharacterId, out var raid)
        && raid.Members.Any(member => member.Session == session && member.RejoinUntil is null) ? raid : null;

    private bool CanJoin(SelectedSessionIdentity applicant, Recruitment raid) =>
        raid.CanEdit
        && RecruitmentChannel(applicant) == raid.Channel
        && RecruitmentChannel(raid.Leader.Session) == raid.Channel
        && !memberships.ContainsKey(applicant.CharacterId)
        && raidRules.HasRecruitmentSpace(raid.Members.Count)
        && raid.Members.All(member => member.Session.AccountId != applicant.AccountId);

    private PartyOperationResult ExitOrdinaryParty(SelectedSessionIdentity session) =>
        parties.KeyOf(session.CharacterId).Kind == PartyKeyKind.Real
            ? parties.LeaveParty(session.CharacterId)
            : PartyOperationResult.Success();

    private AntonRaidRecruitmentChange? Remove(SelectedSessionIdentity session)
    {
        InvalidateApprovals(session);
        if (Of(session) is not { } raid)
            return null;
        return RemoveMember(raid, session);
    }

    private AntonRaidRecruitmentChange RemoveMember(Recruitment raid, SelectedSessionIdentity session)
    {
        var run = dungeonRuns?.Find(session.CharacterId)?.RunId;
        memberships.Remove(session.CharacterId);
        raid.Members.RemoveAll(member => member.Session == session);
        raid.DeferredSquadMembers.Remove(session.CharacterId);
        if (raid.LeaderId == session.CharacterId)
        {
            if (raid.Members.FirstOrDefault(member => member.RejoinUntil is null) is { } successor)
            {
                raid.LeaderId = successor.Session.CharacterId;
            }
            else
            {
                foreach (var member in raid.Members)
                    memberships.Remove(member.Session.CharacterId);
                raid.Members.Clear();
            }
        }
        var canceled = raid.Stage == AntonRaidStage.Preparing;
        if (canceled)
            RestoreFromPreparation(raid);
        raid.PreparedSquads.RemoveWhere(number => raid.Members.All(member => member.Squad != number));
        var squadChanges = ReplaceSquads(raid);
        if (raid.Members.Count == 0)
        {
            raids.Remove(raid.Number);
            if (!raid.LeaseReturned)
            {
                if (run is { } active)
                    retiringLeases.Add(active, raid.Lease);
                else
                    ReturnLease(raid.Lease);
            }
        }

        return Change(raid) with { Squads = squadChanges };
    }

    private void ReturnRetiredLease(DungeonRunId run)
    {
        if (retiringLeases.Remove(run, out var lease))
            ReturnLease(lease);
    }

    private void ReturnLease(PartyLease lease)
    {
        if (!leases.Return(lease))
            throw new InvalidOperationException("An empty raid must own an empty party lease.");
    }

    private void InvalidateApprovals(SelectedSessionIdentity session)
    {
        foreach (var key in approvals.Keys.Where(key => key.Sender == session || key.Recipient == session).ToArray())
            approvals.Remove(key);
    }

    private AntonRaidRecruitmentChange Change(Recruitment raid) =>
        new(raid.Number, raid.Members.Count == 0 ? null : raid.Snapshot(), Directory(raid.Channel),
            towns.Query(new(raid.Channel, checked((byte)channelOptions.RaidTownId)))
                .SelectMany(area => area.Members)
                .Select(member => member.Session)
                .Where(sessions.IsInWorld)
                .ToArray());

    private sealed class Recruitment(uint number, int channel, ReadOnlyMemory<byte> title, AntonRaidMember leader, PartyLease lease)
    {
        public uint Number { get; } = number;
        public int Channel { get; } = channel;
        public ReadOnlyMemory<byte> Title { get; set; } = title;
        public List<AntonRaidMember> Members { get; } = [leader];
        public CharacterId LeaderId { get; set; } = leader.Session.CharacterId;
        public AntonRaidMember Leader => Members.First(member => member.Session.CharacterId == LeaderId);
        public PartyLease Lease { get; } = lease;
        public bool LeaseReturned { get; set; }
        public AntonRaidStage Stage { get; set; }
        public byte Phase { get; set; }
        public bool CanEdit => Stage is AntonRaidStage.Recruiting or AntonRaidStage.Break;
        public DateTimeOffset? Deadline { get; set; }
        public StartPreparation? Preparation { get; set; }
        public long PreparationGeneration { get; set; }
        public HashSet<byte> PreparedSquads { get; } = [];
        public Dictionary<byte, CharacterId> SquadLeaders { get; } = [];
        public Dictionary<(SelectedSessionIdentity Inviter, SelectedSessionIdentity Invitee), PendingSquadInvite> SquadInvites { get; } = [];
        public HashSet<CharacterId> PaidTickets { get; } = [];
        public AntonRaidPhaseRuntime? Combat { get; set; }
        public Dictionary<byte, AntonRaidPhaseOutcome> Outcomes { get; } = [];
        public HashSet<CharacterId> DeferredSquadMembers { get; } = [];

        public AntonRaidRecruitmentSnapshot Snapshot() => new(Number, Title, Members.ToArray())
        {
            Stage = Stage,
            Phase = Phase,
            LeaderCharacterId = LeaderId,
            PreparingFromIntermission = Preparation?.Previous == AntonRaidStage.Break,
        };
    }
}
