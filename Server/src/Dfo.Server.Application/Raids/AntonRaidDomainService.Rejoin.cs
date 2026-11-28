using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Raids;

internal sealed partial class AntonRaidDomainService
{
    private bool restoreSquadsRequested;

    public AntonRaidRecruitmentChange? Disconnected(SelectedSessionIdentity session)
    {
        GameplayWriteAssertion.Writing(nameof(AntonRaidDomainService) + "." + nameof(Disconnected));
        InvalidateApprovals(session);
        if (Of(session) is not { } raid)
            return null;

        if (raid.Stage is AntonRaidStage.Completed or AntonRaidStage.Failed)
            return RemoveMember(raid, session);
        var now = clock.GetUtcNow();
        var index = raid.Members.FindIndex(member => member.Session == session);
        raid.Members[index] = raid.Members[index] with { DisconnectedAt = now, RejoinUntil = now + raidRules.Current.RejoinTime };
        raid.DeferredSquadMembers.Remove(session.CharacterId);
        if (raid.Stage == AntonRaidStage.Preparing)
            RestoreFromPreparation(raid);
        deadlines.Wake(DungeonDeadlineKind.AntonRaid);
        return Change(raid) with { Squads = ReplaceSquads(raid) };
    }

    public AntonRaidRejoinOffer? RejoinOffer(SelectedSessionIdentity session)
    {
        if (!sessions.IsInWorld(session) || !memberships.TryGetValue(session.CharacterId, out var raid)
            || raid.Stage is AntonRaidStage.Completed or AntonRaidStage.Failed
            || !raid.Members.Any(member => MatchesReservation(member, session, clock.GetUtcNow())))
        {
            return null;
        }
        return new(raid.Number, checked((ushort)raid.Channel));
    }

    public AntonRaidRecruitmentResult Rejoin(SelectedSessionIdentity session, uint number)
    {
        GameplayWriteAssertion.Writing(nameof(AntonRaidDomainService) + "." + nameof(Rejoin));
        if (RejoinOffer(session) is not { } offer || offer.Number != number
            || RecruitmentChannel(session) != offer.Channel
            || !raids.TryGetValue(number, out var raid))
        {
            return AntonRaidRecruitmentResult.Refused;
        }
        var index = raid.Members.FindIndex(member => MatchesReservation(member, session, clock.GetUtcNow()));
        if (index < 0)
            return AntonRaidRecruitmentResult.Refused;
        var oldParty = ExitOrdinaryParty(session);
        if (!oldParty.Succeeded)
            return AntonRaidRecruitmentResult.Refused;
        var returning = raid.Members[index] with { Session = session, DisconnectedAt = null, RejoinUntil = null };
        raid.Members[index] = returning;
        if (returning.Squad != 0 && raid.Stage is AntonRaidStage.Attacking or AntonRaidStage.Settling
            && raid.Members.Any(member => member != returning && member.Squad == returning.Squad
                && dungeonRuns.Find(member.Session.CharacterId) is { } run
                && run.PartyKey == raid.Lease.Keys[returning.Squad - 1]))
        {
            raid.DeferredSquadMembers.Add(session.CharacterId);
        }
        return new(true, number, [Change(raid) with { Squads = ReplaceSquads(raid) }], oldParty.Notifications);
    }

    public IReadOnlyList<AntonRaidRejoinExpiry> RejoinExpirations() =>
        raids.Values.SelectMany(raid => raid.Members.Where(member => member.RejoinUntil is not null)
            .Select(member => new AntonRaidRejoinExpiry(raid.Lease.Owner, raid.Number, member.Session, member.RejoinUntil!.Value)))
            .ToArray();

    public AntonRaidRecruitmentChange? ExpireRejoin(AntonRaidRejoinExpiry expiry)
    {
        GameplayWriteAssertion.Writing(nameof(AntonRaidDomainService) + "." + nameof(ExpireRejoin));
        if (expiry.At > clock.GetUtcNow() || !raids.TryGetValue(expiry.Number, out var raid)
            || raid.Lease.Owner != expiry.Instance
            || !raid.Members.Any(member => member.Session == expiry.DepartedSession && member.RejoinUntil == expiry.At))
        {
            return null;
        }
        return RemoveMember(raid, expiry.DepartedSession);
    }

    public AntonRaidRecruitmentResult TransferLeadership(SelectedSessionIdentity sender, ushort target)
    {
        GameplayWriteAssertion.Writing(nameof(AntonRaidDomainService) + "." + nameof(TransferLeadership));
        if (Of(sender) is not { } raid || raid.Stage == AntonRaidStage.Preparing || raid.Leader.Session != sender
            || raid.Members.Find(member => member.Session.WireUserId == target && member.RejoinUntil is null) is not { } candidate
            || !sessions.IsInWorld(candidate.Session))
        {
            return AntonRaidRecruitmentResult.Refused;
        }
        InvalidateApprovals(sender);
        raid.LeaderId = candidate.Session.CharacterId;
        return new(true, raid.Number, [Change(raid)], PartyNotificationPlan.Empty);
    }

    public AntonRaidRecruitmentResult ChangeTitle(SelectedSessionIdentity sender, ReadOnlyMemory<byte> title)
    {
        GameplayWriteAssertion.Writing(nameof(AntonRaidDomainService) + "." + nameof(ChangeTitle));
        if (Of(sender) is not { } raid || raid.Leader.Session != sender || raid.Stage == AntonRaidStage.Preparing
            || title.Length > byte.MaxValue)
        {
            return AntonRaidRecruitmentResult.Refused;
        }
        raid.Title = title.ToArray();
        return new(true, raid.Number, [Change(raid)], PartyNotificationPlan.Empty);
    }

    private static bool MatchesReservation(AntonRaidMember member, SelectedSessionIdentity session, DateTimeOffset now) =>
        member.RejoinUntil > now && member.Session.CharacterId == session.CharacterId
        && member.Session.AccountId == session.AccountId && member.Session != session;

    public bool HasSquadRestorations() => restoreSquadsRequested;

    public IReadOnlyList<PartyNotificationPlan> RestoreAvailableSquads()
    {
        GameplayWriteAssertion.Writing(nameof(AntonRaidDomainService) + "." + nameof(RestoreAvailableSquads));
        restoreSquadsRequested = false;
        var changes = new List<PartyNotificationPlan>();
        foreach (var raid in raids.Values.Where(raid => raid.DeferredSquadMembers.Count != 0))
        {
            var ready = raid.Members.Where(member => member.Squad != 0 && raid.DeferredSquadMembers.Contains(member.Session.CharacterId))
                .Select(member => member.Squad).Distinct()
                .Where(squad => raid.Members.Where(member => member.Squad == squad)
                    .All(member => dungeonRuns.Find(member.Session.CharacterId) is not { } run || run.PartyKey != raid.Lease.Keys[squad - 1]))
                .ToHashSet();
            if (raid.DeferredSquadMembers.RemoveWhere(character => raid.Members.Any(member =>
                member.Session.CharacterId == character && ready.Contains(member.Squad))) != 0)
            {
                changes.AddRange(ReplaceSquads(raid));
            }
        }
        return changes;
    }

    private void OnRaidRunEnded(DungeonRunId run)
    {
        ReturnRetiredLease(run);
        if (raids.Values.Any(raid => raid.DeferredSquadMembers.Count != 0))
        {
            restoreSquadsRequested = true;
            deadlines.Wake(DungeonDeadlineKind.AntonRaid);
        }
    }
}
