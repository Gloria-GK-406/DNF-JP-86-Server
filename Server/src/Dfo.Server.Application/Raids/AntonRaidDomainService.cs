using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Raids;

internal sealed class AntonRaidDomainService(
    ISelectedSessionDomainService sessions,
    ITownAreaDomainService towns,
    IPartyService parties,
    ChannelEntryOptions channelOptions,
    TimeProvider clock) : IAntonRaidDomainService
{
    private const int RaidCapacity = 20;
    private const int SquadCapacity = 4;
    private const uint LastSquad = 10;
    private static readonly TimeSpan ApprovalLifetime = TimeSpan.FromSeconds(60);

    private readonly Dictionary<uint, Recruitment> raids = [];
    private readonly Dictionary<CharacterId, Recruitment> memberships = [];
    private readonly Dictionary<int, ushort> lastRaidNumbers = [];
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
        GameplayWriteAssertion.Writing(nameof(Create));
        if (RecruitmentChannel(leader.Session) is not { } channel
            || lastRaidNumbers.GetValueOrDefault(channel) == ushort.MaxValue)
            return AntonRaidRecruitmentResult.Refused;

        var party = ExitOrdinaryParty(leader.Session);
        if (!party.Succeeded)
            return AntonRaidRecruitmentResult.Refused;

        var prior = Remove(leader.Session);
        var sequence = checked((ushort)(lastRaidNumbers.GetValueOrDefault(channel) + 1));
        lastRaidNumbers[channel] = sequence;
        var raid = new Recruitment(((uint)channel << 16) | sequence, channel, title.ToArray(), leader with { Squad = 0 });
        raids.Add(raid.Number, raid);
        memberships.Add(leader.Session.CharacterId, raid);
        return new(true, raid.Number, prior is null ? [Change(raid)] : [prior, Change(raid)], party.Notifications);
    }

    public AntonRaidApproval? Request(AntonRaidMember sender, AntonRaidMember recipient)
    {
        GameplayWriteAssertion.Writing(nameof(Request));
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

        var approval = new AntonRaidApproval(sender.Session, recipient.Session, applicant, leader.Session, raid.Number, now + ApprovalLifetime);
        approvals.Add(key, approval);
        return approval;
    }

    public AntonRaidRecruitmentResult Respond(SelectedSessionIdentity recipient, SelectedSessionIdentity sender, bool accepted)
    {
        GameplayWriteAssertion.Writing(nameof(Respond));
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

        raid.Members.Add(approval.Applicant with { Squad = 0 });
        memberships.Add(approval.Applicant.Session.CharacterId, raid);
        InvalidateApprovals(approval.Applicant.Session);
        return new(true, raid.Number, [Change(raid)], party.Notifications);
    }

    public AntonRaidRecruitmentResult Assign(SelectedSessionIdentity sender, ushort target, uint squad)
    {
        GameplayWriteAssertion.Writing(nameof(Assign));
        if (Of(sender) is not { } raid || RecruitmentChannel(sender) != raid.Channel
            || (sender.WireUserId != target && raid.Leader.Session != sender) || squad > LastSquad)
            return AntonRaidRecruitmentResult.Refused;

        var index = raid.Members.FindIndex(member => member.Session.WireUserId == target);
        if (index < 0)
            return AntonRaidRecruitmentResult.Refused;
        if (raid.Members[index].Squad == squad)
            return new(true, raid.Number, [], PartyNotificationPlan.Empty);
        if (squad != 0 && raid.Members.Count(member => member.Squad == squad) >= SquadCapacity)
            return AntonRaidRecruitmentResult.Refused;

        raid.Members[index] = raid.Members[index] with { Squad = (byte)squad };
        return new(true, raid.Number, [Change(raid)], PartyNotificationPlan.Empty);
    }

    public AntonRaidRecruitmentResult Leave(SelectedSessionIdentity sender, ushort target)
    {
        GameplayWriteAssertion.Writing(nameof(Leave));
        if (Of(sender) is not { } raid || (sender.WireUserId != target && raid.Leader.Session != sender))
            return AntonRaidRecruitmentResult.Refused;
        var member = raid.Members.Find(candidate => candidate.Session.WireUserId == target);
        return member is not null && Remove(member.Session) is { } change
            ? new(true, change.Number, [change], PartyNotificationPlan.Empty)
            : AntonRaidRecruitmentResult.Refused;
    }

    public AntonRaidRecruitmentChange? Disconnected(SelectedSessionIdentity session)
    {
        GameplayWriteAssertion.Writing(nameof(Disconnected));
        return Remove(session);
    }

    private Recruitment? Of(SelectedSessionIdentity session) =>
        memberships.TryGetValue(session.CharacterId, out var raid)
        && raid.Members.Any(member => member.Session == session) ? raid : null;

    private bool CanJoin(SelectedSessionIdentity applicant, Recruitment raid) =>
        RecruitmentChannel(applicant) == raid.Channel
        && RecruitmentChannel(raid.Leader.Session) == raid.Channel
        && !memberships.ContainsKey(applicant.CharacterId)
        && raid.Members.Count < RaidCapacity
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

        memberships.Remove(session.CharacterId);
        raid.Members.RemoveAll(member => member.Session == session);
        if (raid.Members.Count == 0)
            raids.Remove(raid.Number);

        return Change(raid);
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

    private sealed class Recruitment(uint number, int channel, ReadOnlyMemory<byte> title, AntonRaidMember leader)
    {
        public uint Number { get; } = number;
        public int Channel { get; } = channel;
        public List<AntonRaidMember> Members { get; } = [leader];
        public AntonRaidMember Leader => Members[0];

        public AntonRaidRecruitmentSnapshot Snapshot() => new(Number, title, Members.ToArray());
    }
}
