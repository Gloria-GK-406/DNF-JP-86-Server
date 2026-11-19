using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Raids;

internal sealed partial class AntonRaidDomainService
{
    public AntonRaidRecruitmentResult MoveFromPartyRequest(SelectedSessionIdentity sender, SelectedSessionIdentity peer,
        AntonRaidSquadSignal signal)
    {
        GameplayWriteAssertion.Writing(nameof(MoveFromPartyRequest));
        if (Of(sender) is not { } raid || Of(peer) != raid || !CanEditActiveSquads(raid) || sender == peer)
            return AntonRaidRecruitmentResult.Refused;
        var senderSquad = raid.Members.First(member => member.Session == sender).Squad;
        var peerSquad = raid.Members.First(member => member.Session == peer).Squad;
        return signal switch
        {
            AntonRaidSquadSignal.Invite when raid.Leader.Session == sender && senderSquad != 0 =>
                Assign(sender, peer.WireUserId, senderSquad),
            AntonRaidSquadSignal.Accept when raid.Leader.Session == peer && senderSquad != 0 && senderSquad == peerSquad =>
                new(true, raid.Number, [], PartyNotificationPlan.Empty),
            _ => AntonRaidRecruitmentResult.Refused,
        };
    }

    public AntonRaidRecruitmentResult LeaveSquad(SelectedSessionIdentity sender)
    {
        GameplayWriteAssertion.Writing(nameof(LeaveSquad));
        if (Of(sender) is not { } raid || !CanEditActiveSquads(raid))
            return AntonRaidRecruitmentResult.Refused;
        var index = raid.Members.FindIndex(member => member.Session == sender);
        return CanMoveIdleMember(raid, raid.Members[index], 0) ? MoveMember(raid, index, 0) : AntonRaidRecruitmentResult.Refused;
    }

    public AntonRaidRecruitmentResult SetSquadLeader(SelectedSessionIdentity sender, SelectedSessionIdentity target)
    {
        GameplayWriteAssertion.Writing(nameof(SetSquadLeader));
        if (Of(sender) is not { } raid || Of(target) != raid || raid.Leader.Session != sender
            || !CanEditActiveSquads(raid) || RecruitmentChannel(sender) != raid.Channel
            || raid.Members.FirstOrDefault(member => member.Session == target) is not { Squad: > 0 } candidate
            || !SquadIdle(raid, candidate.Squad))
        {
            return AntonRaidRecruitmentResult.Refused;
        }
        raid.SquadLeaders[candidate.Squad] = target.CharacterId;
        return new(true, raid.Number, [Change(raid) with { Squads = ReplaceSquads(raid) }], PartyNotificationPlan.Empty);
    }

    private static bool CanEditActiveSquads(Recruitment raid) =>
        raid.Stage is AntonRaidStage.Attacking or AntonRaidStage.Break;

    private bool CanMoveIdleMember(Recruitment raid, AntonRaidMember member, byte destination) =>
        RecruitmentChannel(member.Session) == raid.Channel && SquadIdle(raid, member.Squad) && SquadIdle(raid, destination);

    private bool SquadIdle(Recruitment raid, byte squad) => squad == 0 || raid.Members
        .Where(member => member.Squad == squad && member.RejoinUntil is null)
        .All(member => RecruitmentChannel(member.Session) == raid.Channel
            && dungeonRuns?.Find(member.Session.CharacterId) is null);

    private AntonRaidRecruitmentResult MoveMember(Recruitment raid, int index, byte squad)
    {
        var member = raid.Members[index];
        if (member.Squad == squad)
            return new(true, raid.Number, [], PartyNotificationPlan.Empty);
        if (squad != 0 && raid.Members.Count(candidate => candidate.Squad == squad) >= SquadCapacity)
            return AntonRaidRecruitmentResult.Refused;
        if (raid.SquadLeaders.GetValueOrDefault(member.Squad) == member.Session.CharacterId)
            raid.SquadLeaders.Remove(member.Squad);
        raid.Members[index] = member with { Squad = squad };
        if (squad != 0 && CanEditActiveSquads(raid))
            raid.PreparedSquads.Add(squad);
        return new(true, raid.Number, [Change(raid) with { Squads = ReplaceSquads(raid) }], PartyNotificationPlan.Empty);
    }

    private static IEnumerable<AntonRaidMember> SquadOrder(Recruitment raid, byte squad, IEnumerable<AntonRaidMember> members) =>
        raid.SquadLeaders.TryGetValue(squad, out var leader)
            ? members.OrderByDescending(member => member.Session.CharacterId == leader)
            : members;
}
