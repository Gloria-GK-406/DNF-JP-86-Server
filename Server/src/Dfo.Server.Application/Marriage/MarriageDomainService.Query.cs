using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

internal sealed partial class MarriageDomainService
{
    public async ValueTask<MarriageView?> ViewAsync(SelectedSessionIdentity actor, CancellationToken ct)
    {
        if (await ledger.ForSessionAsync(actor, ct).ConfigureAwait(false) is not { } relation) return null;
        var own = relation.Proposer.CharacterId == actor.CharacterId ? relation.Proposer : relation.Recipient;
        var stage = relation.Stage;
        var ceremony = relation.CeremonyId;
        if (stage == MarriageStage.Proposed && await ledger.FindProposalAsync(actor.CharacterId, ct).ConfigureAwait(false) is
                { State: MarriageProposalState.Pending } proposal && proposal.Id == relation.Id && ProposalLapsed(proposal))
            stage = MarriageStageRules.Next(stage, MarriageTrigger.ProposalLapsed)!.Value;
        if (stage == MarriageStage.Requested && await reservations.ActiveAsync(relation.Id, ct).ConfigureAwait(false) is
                { State: MarriageCeremonyState.Pending } pending && pending.Id == relation.CeremonyId && ReservationLapsed(pending))
        {
            stage = MarriageStageRules.Next(stage, MarriageTrigger.CeremonyWithdrawn)!.Value;
            ceremony = null;
        }
        var partner = PartnerOf(relation, actor.CharacterId);
        return new(relation.Id, stage, relation.Revision, own, partner, ceremony)
        {
            MarriedAt = relation.MarriedAt,
            LovePoints = relation.LovePoints,
            TrialsCleared = MarriageStageRules.IsCouple(stage) && await TrialsClearedAsync(relation.Id, own, partner, ct).ConfigureAwait(false),
        };
    }

    /// <remarks>A view is only a read: without the wedding quest content (no archive, or unreadable) it reports not cleared.</remarks>
    private async ValueTask<bool> TrialsClearedAsync(Guid relation, SelectedSessionIdentity own, SelectedSessionIdentity partner,
        CancellationToken ct)
    {
        try
        {
            return await WeddingQuestsReadyAsync(relation, own, partner, ct).ConfigureAwait(false);
        }
        catch (Exception error) when (error is FileNotFoundException or InvalidDataException)
        {
            return false;
        }
    }

    public async ValueTask<MarriageEngagementSnapshot> SnapshotAsync(AccountId account, CharacterId character, CancellationToken ct)
    {
        var relation = await ledger.ForParticipantAsync(account, character, ct).ConfigureAwait(false);
        if (relation is null || !MarriageStageRules.IsCouple(relation.Stage)) return MarriageEngagementSnapshot.Empty;
        if (await eligibility.ProfileAsync(PartnerOf(relation, character), ct).ConfigureAwait(false) is not { } profile)
            return MarriageEngagementSnapshot.Empty;
        return new(relation.Id, profile)
        {
            Stage = relation.Stage,
            MarriedAt = relation.MarriedAt,
            LovePoints = relation.LovePoints,
        };
    }

    public ValueTask<MarriageProposal?> ReceivedProposalAsync(CharacterId character, CancellationToken ct) =>
        ledger.ReceivedProposalAsync(character, ct);

    public ValueTask<MarriageCeremony?> ReceivedReservationAsync(CharacterId character, CancellationToken ct) =>
        reservations.ReceivedAsync(character, ct);

    public async ValueTask<bool> IsMarriedAsync(Guid relation, CancellationToken ct) =>
        (await ledger.ReadAsync(relation, ct).ConfigureAwait(false))?.Stage == MarriageStage.Married;

    public async ValueTask<MarriageWeddingPhase?> WeddingPhaseAsync(Guid ceremony, CancellationToken ct) =>
        (await settlement.ReadAsync(ceremony, ct).ConfigureAwait(false))?.Phase;

    public async ValueTask<MarriageChurchSnapshot?> AppearanceAsync(SelectedSessionIdentity actor, CancellationToken ct)
    {
        if (!eligibility.IsCurrent(actor) || church.Standing(actor) is not { } member) return null;
        foreach (var wedding in await settlement.PendingAsync(ct).ConfigureAwait(false))
        {
            if (await church.AttendedRoomAsync(actor, member, wedding.Ceremony.Id, wedding.Phase, ct).ConfigureAwait(false) is not { } room) continue;
            var key = PeerOf(wedding.Ceremony, actor.CharacterId).Value;
            var invitation = await guests.InvitationOfAsync(wedding.Ceremony.Id, actor.CharacterId, ct).ConfigureAwait(false);
            return await church.SnapshotAsync(wedding.Ceremony, room, invitation?.Key ?? checked((uint)key), ct).ConfigureAwait(false);
        }
        return null;
    }

    public async ValueTask<bool> CanCrossAsync(SelectedSessionIdentity actor, int channelId, byte townId, byte areaId, CancellationToken ct)
    {
        if (!await church.IsChurchAsync(townId, areaId, ct).ConfigureAwait(false)) return true;
        if (await ReservedAsync(actor, ct).ConfigureAwait(false) is { } reserved)
            return await church.CanCrossOwnAsync(actor, reserved.Ceremony.Id, channelId, townId, areaId, ct).ConfigureAwait(false);
        if (!eligibility.IsCurrent(actor)) return false;
        foreach (var wedding in await settlement.PendingAsync(ct).ConfigureAwait(false))
            if (await church.CanCrossAsGuestAsync(actor, wedding.Ceremony.Id, wedding.Phase, channelId, townId, areaId, ct)
                    .ConfigureAwait(false) is { } verdict)
                return verdict;
        return false;
    }

    public async ValueTask<bool> CanLeaveAsync(SelectedSessionIdentity actor, byte town, byte area, CancellationToken ct)
    {
        var appearance = await AppearanceAsync(actor, ct).ConfigureAwait(false);
        if (appearance is null || !church.IsIn(actor.CharacterId, appearance.Room)
            || appearance.Room.TownId == town && appearance.Room.AreaId == area) return true;
        // A21 RVA0089464D locks ordinary departure below sixty seconds.
        return await WeddingPhaseAsync(appearance.Room.CeremonyId, ct).ConfigureAwait(false) != MarriageWeddingPhase.Preparing
            || appearance.Room.ReadyUntil - clock.GetUtcNow() >= TimeSpan.FromSeconds(60);
    }

    public async ValueTask<ushort> RemainingInvitationsAsync(Guid ceremony, CharacterId actor, CancellationToken ct)
    {
        var wedding = await settlement.ReadAsync(ceremony, ct).ConfigureAwait(false);
        if (wedding is null || wedding.Phase != MarriageWeddingPhase.Preparing) return 0;
        return await guests.RemainingAsync(wedding.Ceremony, actor, ct).ConfigureAwait(false);
    }

    public ValueTask<IReadOnlyList<uint>> InvitedAsync(Guid ceremony, CancellationToken ct) => guests.InvitedAsync(ceremony, ct);

    public async ValueTask<IReadOnlyList<AccountId>> CoupleAccountsAsync(SelectedSessionIdentity actor, CancellationToken ct) =>
        CoupleAccounts(eligibility.IsCurrent(actor) ? await ledger.ForSessionAsync(actor, ct).ConfigureAwait(false) : null, actor);

    public async ValueTask<IReadOnlyList<AccountId>> WeddingAccountsAsync(Guid ceremony, CancellationToken ct) =>
        await settlement.ReadAsync(ceremony, ct).ConfigureAwait(false) is { } wedding
            ? await guests.AccountsAsync(wedding, ct).ConfigureAwait(false) : [];

    public async ValueTask<IReadOnlyList<Guid>> ActiveWeddingsAsync(CancellationToken ct) =>
        (await settlement.PendingAsync(ct).ConfigureAwait(false)).Select(x => x.Ceremony.Id).ToArray();
}
