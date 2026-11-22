using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Raids;

internal sealed partial class AntonRaidDomainService
{
    public bool IsPreparing(SelectedSessionIdentity session) => Of(session)?.Stage == AntonRaidStage.Preparing;

    public async ValueTask<IReadOnlyList<AntonRaidTicket>> TicketsAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        if (Of(session) is not { } raid)
            return [];
        var rules = raidRules.Current;
        var tickets = new List<AntonRaidTicket>(raid.Members.Count);
        foreach (var member in raid.Members)
        {
            var paid = raid.PaidTickets.Contains(member.Session.CharacterId);
            if (paid || member.RejoinUntil.HasValue)
            {
                tickets.Add(new(member.RejoinUntil.HasValue ? null : member.Session.CharacterId, 0, paid));
                continue;
            }
            var store = inventory.GetStore(member.Session.CharacterId, BusinessInventoryStoreKind.Item);
            long count = 0;
            foreach (var space in new[] { BusinessInventorySpace.QuickSlot, BusinessInventorySpace.Consumable,
                         BusinessInventorySpace.Material, BusinessInventorySpace.Quest, BusinessInventorySpace.ExpertMaterial })
            {
                if (store.FindContainer(space) is not { } container)
                    continue;
                var items = await container.GetAllAsync(ct).ConfigureAwait(false);
                count += items.Where(item => materialRules.IsConsumable(item, rules.TicketItemId)).Sum(item => (long)item.Count);
            }
            tickets.Add(new(member.Session.CharacterId, count, count >= rules.TicketCount));
        }
        return tickets;
    }

    public ValueTask<AntonRaidStart> StartAsync(SelectedSessionIdentity leader, CancellationToken ct) =>
        StartAsync(leader, automatic: false, ct);

    private async ValueTask<AntonRaidStart> StartAsync(SelectedSessionIdentity leader, bool automatic, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(StartAsync));
        if (Of(leader) is not { CanEdit: true } raid || raid.Leader.Session != leader
            || raid.Members.Any(member => RecruitmentChannel(member.Session) != raid.Channel))
        {
            return AntonRaidStart.Refused;
        }
        var rules = raidRules.Current;
        if (!AntonRaidRuleService.CanStartWith(rules, raid.Members.Count))
            return AntonRaidStart.Refused;

        var tickets = await TicketsAsync(leader, ct).ConfigureAwait(false);
        if (tickets.Any(ticket => !ticket.Ready))
            return new(false, Tickets: tickets);

        var preparation = new StartPreparation(raid.Stage, automatic ? null : raid.Deadline, raid.Members.ToArray(), [.. raid.PreparedSquads]);
        var deadline = clock.GetUtcNow() + rules.PreparationTime;
        StageAdmission(raid, () =>
        {
            raid.Preparation = preparation;
            raid.PreparationGeneration++;
            raid.Stage = AntonRaidStage.Preparing;
            raid.Deadline = deadline;
            foreach (var member in raid.Members)
                InvalidateApprovals(member.Session);
            deadlines.Wake(DungeonDeadlineKind.AntonRaid);
        });
        return new(true, StageChange(raid, AntonRaidStage.Preparing, raid.Phase), deadline, tickets);
    }

    public PartyOperationResult PrepareSquad(SelectedSessionIdentity sender, AntonRaidSquadSignal signal, SelectedSessionIdentity? peer = null)
    {
        GameplayWriteAssertion.Writing(nameof(PrepareSquad));
        if (signal == AntonRaidSquadSignal.Settings && Of(sender) is { Stage: AntonRaidStage.Attacking or AntonRaidStage.Break } active
            && active.Lease.Keys.Contains(parties.KeyOf(sender.CharacterId)))
        {
            return PartyOperationResult.Success();
        }
        if (Of(sender) is not { Stage: AntonRaidStage.Preparing, Preparation: { } preparation } raid
            || raid.Deadline <= clock.GetUtcNow())
        {
            return PartyOperationResult.Reject(PartyFailureReason.RaidRecruiting);
        }
        var member = preparation.Roster.First(item => item.Session == sender);
        var squad = SquadOrder(raid, member.Squad, preparation.Roster.Where(item => item.Squad == member.Squad)).ToArray();
        var leader = squad[0].Session;
        var validSignal = signal switch
        {
            AntonRaidSquadSignal.Settings => sender == leader && peer is null,
            AntonRaidSquadSignal.Invite => sender == leader && peer != leader && squad.Any(item => item.Session == peer),
            AntonRaidSquadSignal.Accept => sender != leader && peer == leader,
            _ => false,
        };
        if (member.Squad == 0 || !validSignal || squad.Any(item => RecruitmentChannel(item.Session) != raid.Channel))
        {
            return PartyOperationResult.Reject(PartyFailureReason.RaidRecruiting);
        }
        var changes = raid.PreparedSquads.Add(member.Squad) ? ReplaceSquads(raid) : [];
        var change = changes.SingleOrDefault();
        if (signal != AntonRaidSquadSignal.Accept)
            return PartyOperationResult.Success(change);
        change ??= new PartyNotificationPlan([], parties.FindParty(
            new(sender.AccountId, sender.CharacterId, sender.WireUserId, null)));
        return PartyOperationResult.Success(change with
        {
            SettingsChange = null,
            Acceptance = new(leader.CharacterId, sender.CharacterId, leader.WireUserId, sender.WireUserId, sender.CharacterId),
        });
    }

    public IReadOnlyList<AntonRaidDeadline> PreparationDeadlines() => raids.Values
        .Where(raid => raid.Stage == AntonRaidStage.Preparing && raid.Deadline.HasValue)
        .Select(raid => new AntonRaidDeadline(raid.Lease.Owner, raid.Number, raid.PreparationGeneration, raid.Deadline!.Value))
        .ToArray();

    public IReadOnlyList<SelectedSessionIdentity> PreparationParticipants(AntonRaidDeadline deadline) =>
        CurrentPreparation(deadline)?.Preparation?.Roster.Select(member => member.Session).ToArray() ?? [];

    public async ValueTask<AntonRaidPreparationOutcome> FinishPreparationAsync(AntonRaidDeadline deadline, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(FinishPreparationAsync));
        if (CurrentPreparation(deadline) is not { Preparation: { } preparation } raid || deadline.At > clock.GetUtcNow())
            return new();
        if (!raid.Members.SequenceEqual(preparation.Roster)
            || preparation.Roster.Any(member => RecruitmentChannel(member.Session) != raid.Channel)
            || preparation.Roster.Where(member => member.Squad != 0).GroupBy(member => member.Squad)
                .Any(squad => !HasFrozenSquad(raid, squad)))
        {
            var ticketStatus = await TicketsAsync(raid.Leader.Session, ct).ConfigureAwait(false);
            return CancelPreparation(deadline) with { Tickets = ticketStatus };
        }

        var rules = raidRules.Current;
        var unpaid = preparation.Roster.Where(member => !raid.PaidTickets.Contains(member.Session.CharacterId))
            .Select((member, slot) => new DungeonRunParticipant(member.Session.AccountId, member.Session.CharacterId,
                checked((byte)slot), member == raid.Leader)).ToArray();
        var payment = await materials.PrepareAsync(unpaid,
            [new DungeonRequiredItem(rules.TicketItemId, rules.TicketCount, true) { AllowSharedMaterial = false }],
            ct).ConfigureAwait(false);
        if (payment.Plan is null)
        {
            var ticketStatus = await TicketsAsync(raid.Leader.Session, ct).ConfigureAwait(false);
            return CancelPreparation(deadline) with { Tickets = ticketStatus };
        }

        var phase = preparation.Previous == AntonRaidStage.Break ? (byte)1 : (byte)0;
        var started = clock.GetUtcNow();
        var attackDeadline = started + rules.PhaseLimits[phase];
        var combat = new AntonRaidPhaseRuntime(raid.Lease.Owner, raid.Number, phase, raid.PreparationGeneration,
            rules, AntonRaidRuleService.ShieldFor(rules, phase), started, attackDeadline, Random.Shared.Next());
        var debits = await payment.Plan.ConsumeAsync(ct).ConfigureAwait(false);
        await PublishCombatAsync(raid, combat, ct, full: true).ConfigureAwait(false);
        StageAdmission(raid, () =>
        {
            raid.PaidTickets.UnionWith(unpaid.Select(member => member.CharacterId));
            raid.Stage = AntonRaidStage.Attacking;
            raid.Phase = phase;
            raid.Deadline = attackDeadline;
            raid.Combat = combat;
            raid.Preparation = null;
            deadlines.Wake(DungeonDeadlineKind.AntonRaid);
        });

        return new(StageChange(raid, AntonRaidStage.Attacking, phase), attackDeadline, Debits: debits);
    }

    private void StageAdmission(Recruitment raid, Action publish)
    {
        var transaction = transactions.Current ?? throw new InvalidOperationException("Raid admission requires a transaction.");
        transaction.Enlist((this, raid.Lease.Owner), () => new AdmissionCommit(publish));
    }

    private AntonRaidRecruitmentChange StageChange(Recruitment raid, AntonRaidStage stage, byte phase)
    {
        var change = Change(raid);
        var snapshot = change.Raid! with
        {
            Stage = stage,
            Phase = phase,
            PreparingFromIntermission = stage == AntonRaidStage.Preparing && raid.Stage == AntonRaidStage.Break,
        };
        return change with
        {
            Raid = snapshot,
            Directory = change.Directory.Select(item => item.Number == snapshot.Number ? snapshot : item).ToArray(),
        };
    }

    public AntonRaidPreparationOutcome CancelPreparation(AntonRaidDeadline deadline)
    {
        GameplayWriteAssertion.Writing(nameof(CancelPreparation));
        if (CurrentPreparation(deadline) is not { } raid)
            return new();
        RestoreFromPreparation(raid);
        return new(Change(raid) with { Squads = ReplaceSquads(raid) });
    }

    public AntonRaidPreparationOutcome CancelPreparationFor(SelectedSessionIdentity session) =>
        Of(session) is { Stage: AntonRaidStage.Preparing, Deadline: { } until } raid
            ? CancelPreparation(new(raid.Lease.Owner, raid.Number, raid.PreparationGeneration, until))
            : new();

    private Recruitment? CurrentPreparation(AntonRaidDeadline deadline) =>
        raids.TryGetValue(deadline.Number, out var raid) && raid.Lease.Owner == deadline.Instance
        && raid.Stage == AntonRaidStage.Preparing && raid.PreparationGeneration == deadline.Generation
        && raid.Deadline == deadline.At ? raid : null;

    private bool HasFrozenSquad(Recruitment raid, IGrouping<byte, AntonRaidMember> squad) =>
        parties.FindByKey(raid.Lease.Keys[squad.Key - 1]) is { } party
        && party.LeaderCharacterId == SquadOrder(raid, squad.Key, squad).First().Session.CharacterId
        && party.Members.OrderBy(member => member.Slot).Select(member => member.CharacterId)
            .SequenceEqual(SquadOrder(raid, squad.Key, squad).Select(member => member.Session.CharacterId));

    private void RestoreFromPreparation(Recruitment raid)
    {
        var preparation = raid.Preparation!;
        raid.Stage = preparation.Previous;
        raid.Deadline = preparation.PreviousDeadline;
        raid.Preparation = null;
        raid.PreparedSquads.Clear();
        raid.PreparedSquads.UnionWith(preparation.PreviousSquads);
    }

    private IReadOnlyList<PartyNotificationPlan> ReplaceSquads(Recruitment raid)
    {
        if (raid.LeaseReturned)
            return [];
        var squads = raid.Members.Where(member => member.RejoinUntil is null
                && !raid.DeferredSquadMembers.Contains(member.Session.CharacterId) && raid.PreparedSquads.Contains(member.Squad))
            .GroupBy(member => member.Squad).Select(squad => new PartyLeaseSquad(squad.Key,
                SquadOrder(raid, squad.Key, squad).Select(member => new PartyCharacterSnapshot(member.Session.AccountId,
                    member.Session.CharacterId, member.Session.WireUserId, null)).ToArray())).ToArray();
        return (leases.Replace(raid.Lease, squads)
            ?? throw new InvalidOperationException("The raid roster no longer owns its party lease.")).Notifications;
    }

    private sealed record StartPreparation(AntonRaidStage Previous, DateTimeOffset? PreviousDeadline,
        IReadOnlyList<AntonRaidMember> Roster, IReadOnlyList<byte> PreviousSquads);

    private sealed class AdmissionCommit(Action publish) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            publish();
            return ValueTask.CompletedTask;
        }
    }
}
