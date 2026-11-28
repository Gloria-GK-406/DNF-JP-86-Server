using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Raids;

internal sealed partial class AntonRaidDomainService
{
    public PartyOperationResult RequestSquadInvite(SelectedSessionIdentity inviter, SelectedSessionIdentity invitee,
        PartyInviteDisplayState display)
    {
        GameplayWriteAssertion.Writing(nameof(AntonRaidDomainService) + "." + nameof(RequestSquadInvite));
        if (PlanSquadInvite(inviter, invitee) is not { } plan)
            return PartyOperationResult.Reject(PartyFailureReason.RaidRecruiting);

        var now = clock.GetUtcNow();
        var invites = plan.Raid.SquadInvites;
        foreach (var expired in invites.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
            invites.Remove(expired);
        invites[(inviter, invitee)] = new(plan.Inviter.Squad, plan.Invitee.Squad, plan.Raid.PreparationGeneration, now + AntonRaidRuleService.ApprovalLifetime);
        return PartyOperationResult.Success(new([], Invitation: new(invitee.CharacterId, inviter.WireUserId, display)));
    }

    public AntonRaidRecruitmentResult RespondSquadInvite(SelectedSessionIdentity invitee, SelectedSessionIdentity inviter, bool accepted)
    {
        GameplayWriteAssertion.Writing(nameof(AntonRaidDomainService) + "." + nameof(RespondSquadInvite));
        if (Of(invitee) is not { } raid || !raid.SquadInvites.Remove((inviter, invitee), out var pending)
            || pending.ExpiresAt <= clock.GetUtcNow() || pending.PreparationGeneration != raid.PreparationGeneration)
            return AntonRaidRecruitmentResult.Refused;
        if (!accepted)
            return AntonRaidRecruitmentResult.Declined;
        if (PlanSquadInvite(inviter, invitee) is not { } plan
            || pending.InviterSquad != plan.Inviter.Squad || pending.InviteeSquad != plan.Invitee.Squad)
            return AntonRaidRecruitmentResult.Refused;

        if (plan.Inviter.Squad == 0)
            SetMemberSquad(raid, raid.Members.IndexOf(plan.Inviter), plan.Squad);
        if (plan.Invitee.Squad == 0)
            SetMemberSquad(raid, raid.Members.IndexOf(plan.Invitee), plan.Squad);
        if (plan.Inviter.Squad == 0 && plan.Invitee.Squad == 0)
            raid.SquadLeaders[plan.Squad] = inviter.CharacterId;

        var key = raid.Lease.Keys[plan.Squad - 1];
        var joining = plan.Invitee.Squad == 0 ? invitee.CharacterId : inviter.CharacterId;
        var acceptance = new PartyAcceptanceNotification(inviter.CharacterId, invitee.CharacterId,
            inviter.WireUserId, invitee.WireUserId, joining);
        var changes = ReplaceSquads(raid).Select(change => change.Snapshot?.Key == key
            ? change with { SettingsChange = null, Acceptance = acceptance }
            : change).ToArray();
        return new(true, raid.Number, [Change(raid) with { Squads = changes }], PartyNotificationPlan.Empty);
    }

    private SquadInvitePlan? PlanSquadInvite(SelectedSessionIdentity inviter, SelectedSessionIdentity invitee)
    {
        if (inviter == invitee || Of(inviter) is not { } raid || Of(invitee) != raid || !CanEditActiveSquads(raid)
            || RecruitmentChannel(inviter) != raid.Channel || RecruitmentChannel(invitee) != raid.Channel
            || dungeonRuns.Find(inviter.CharacterId) is not null || dungeonRuns.Find(invitee.CharacterId) is not null)
            return null;

        var from = raid.Members.First(member => member.Session == inviter);
        var to = raid.Members.First(member => member.Session == invitee);
        if (from.Squad != 0 && to.Squad != 0)
            return null;

        var existingSquad = Math.Max(from.Squad, to.Squad);
        var destination = existingSquad != 0 ? existingSquad : (byte)Enumerable.Range(1, raid.Lease.Keys.Length)
            .FirstOrDefault(number => raid.Members.All(member => member.Squad != number));
        if (destination == 0 || !SquadIdle(raid, destination)
            || !raidRules.HasSquadSpace(raid.Members.Count(member => member.Squad == destination)))
            return null;
        return new(raid, from, to, destination);
    }

    private sealed record SquadInvitePlan(Recruitment Raid, AntonRaidMember Inviter, AntonRaidMember Invitee, byte Squad);
    private sealed record PendingSquadInvite(byte InviterSquad, byte InviteeSquad, long PreparationGeneration, DateTimeOffset ExpiresAt);

    public AntonRaidRecruitmentResult LeaveSquad(SelectedSessionIdentity sender)
    {
        GameplayWriteAssertion.Writing(nameof(AntonRaidDomainService) + "." + nameof(LeaveSquad));
        if (Of(sender) is not { } raid || !CanEditActiveSquads(raid))
            return AntonRaidRecruitmentResult.Refused;
        var index = raid.Members.FindIndex(member => member.Session == sender);
        return CanMoveIdleMember(raid, raid.Members[index], 0) ? MoveMember(raid, index, 0) : AntonRaidRecruitmentResult.Refused;
    }

    public AntonRaidRecruitmentResult SetSquadLeader(SelectedSessionIdentity sender, SelectedSessionIdentity target)
    {
        GameplayWriteAssertion.Writing(nameof(AntonRaidDomainService) + "." + nameof(SetSquadLeader));
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
        if (squad != 0 && !raidRules.HasSquadSpace(raid.Members.Count(candidate => candidate.Squad == squad)))
            return AntonRaidRecruitmentResult.Refused;
        SetMemberSquad(raid, index, squad);
        return new(true, raid.Number, [Change(raid) with { Squads = ReplaceSquads(raid) }], PartyNotificationPlan.Empty);
    }

    private void SetMemberSquad(Recruitment raid, int index, byte squad)
    {
        var member = raid.Members[index];
        if (raid.SquadLeaders.GetValueOrDefault(member.Squad) == member.Session.CharacterId)
            raid.SquadLeaders.Remove(member.Squad);
        foreach (var invite in raid.SquadInvites.Keys.Where(pair => pair.Inviter == member.Session || pair.Invitee == member.Session).ToArray())
            raid.SquadInvites.Remove(invite);
        raid.Members[index] = member with { Squad = squad };
        if (squad != 0 && CanEditActiveSquads(raid))
        {
            // An incoming member can precede the current leader in the raid roster.
            if (parties.FindByKey(raid.Lease.Keys[squad - 1]) is { } current)
                raid.SquadLeaders[squad] = current.LeaderCharacterId;
            raid.PreparedSquads.Add(squad);
        }
    }

    private static IEnumerable<AntonRaidMember> SquadOrder(Recruitment raid, byte squad, IEnumerable<AntonRaidMember> members) =>
        raid.SquadLeaders.TryGetValue(squad, out var leader)
            ? members.OrderByDescending(member => member.Session.CharacterId == leader)
            : members;
}
