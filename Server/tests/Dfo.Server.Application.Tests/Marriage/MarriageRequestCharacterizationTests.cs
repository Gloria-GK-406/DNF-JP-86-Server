using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Marriage;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Application.Tests.Marriage.MarriageHarness;

namespace Dfo.Server.Application.Tests.Marriage;

/// <summary>Characterization of B-01 (proposal), B-02 (reservation) and B-03 (wedding preparation).</summary>
public sealed class MarriageRequestCharacterizationTests
{
    [Fact]
    public async Task Proposal_guards_answer_their_own_failure_codes()
    {
        await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
        var third = AddCharacter(f, 3, 73, job: 0);
        Assert.Equal(MarriageProposalFailure.Invalid, (await InTx(f, () => Couple(f).ProposeAsync(f.Session, f.Session, default))).Failure);
        MoveTo(f, peer, 2, 0);
        Assert.Equal(MarriageProposalFailure.Unavailable, (await InTx(f, () => Couple(f).ProposeAsync(f.Session, peer, default))).Failure);
        MoveTo(f, peer, 2, 1);
        f.Sql("UPDATE characters SET exp=0 WHERE character_id=72");
        Assert.Equal(MarriageProposalFailure.Ineligible, (await InTx(f, () => Couple(f).ProposeAsync(f.Session, peer, default))).Failure);
        f.Sql("UPDATE characters SET exp=(SELECT exp FROM characters WHERE character_id=71) WHERE character_id=72");
        Assert.True((await InTx(f, () => Couple(f).ProposeAsync(f.Session, peer, default))).Succeeded);
        Assert.Equal(MarriageProposalFailure.Occupied, (await InTx(f, () => Couple(f).ProposeAsync(peer, f.Session, default))).Failure);
        Assert.Equal(MarriageProposalFailure.Invalid, (await InTx(f, () => Couple(f).RespondProposalAsync(peer, f.Session, 3, default))).Failure);
        Assert.Equal(MarriageProposalFailure.Stale, (await InTx(f, () => Couple(f).RespondProposalAsync(f.Session, peer, 0, default))).Failure);
        Assert.Equal(MarriageProposalState.Engaged, (await InTx(f, () => Couple(f).RespondProposalAsync(peer, f.Session, 0, default))).Proposal!.State);
        Assert.Equal(MarriageProposalFailure.Stale, (await InTx(f, () => Couple(f).RespondProposalAsync(peer, f.Session, 1, default))).Failure);
        Assert.Equal(MarriageProposalFailure.Occupied, (await InTx(f, () => Couple(f).ProposeAsync(third, f.Session, default))).Failure);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_proposals"));
    }

    [Fact]
    public async Task Acceptance_rechecks_town_and_eligibility_without_closing_the_proposal()
    {
        await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
        Assert.True((await InTx(f, () => Couple(f).ProposeAsync(f.Session, peer, default))).Succeeded);
        MoveTo(f, peer, 2, 0);
        var away = await InTx(f, () => Couple(f).RespondProposalAsync(peer, f.Session, 0, default));
        Assert.Equal(MarriageProposalFailure.Unavailable, away.Failure); Assert.False(away.Changed);
        MoveTo(f, peer, 2, 1);
        f.Sql("UPDATE characters SET exp=0 WHERE character_id=71");
        Assert.Equal(MarriageProposalFailure.Ineligible, (await InTx(f, () => Couple(f).RespondProposalAsync(peer, f.Session, 0, default))).Failure);
        Assert.Equal(0, f.Scalar("SELECT state FROM marriage_proposals"));
        f.Sql("UPDATE characters SET exp=(SELECT exp FROM characters WHERE character_id=72) WHERE character_id=71");
        Assert.Equal(MarriageProposalState.Engaged, (await InTx(f, () => Couple(f).RespondProposalAsync(peer, f.Session, 0, default))).Proposal!.State);
    }

    [Fact]
    public async Task Expired_proposal_answered_with_timeout_is_recorded_once()
    {
        await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
        Assert.True((await InTx(f, () => Couple(f).ProposeAsync(f.Session, peer, default))).Succeeded);
        w.Clock.Now += TimeSpan.FromMinutes(2);
        var expired = await InTx(f, () => Couple(f).RespondProposalAsync(peer, f.Session, 2, default));
        Assert.Equal(MarriageProposalFailure.None, expired.Failure); Assert.True(expired.Changed);
        Assert.Equal(MarriageProposalState.Expired, expired.Proposal!.State); Assert.Equal((byte)2, expired.Proposal.Response);
        var replay = await InTx(f, () => Couple(f).RespondProposalAsync(peer, f.Session, 2, default));
        Assert.Equal(MarriageProposalFailure.None, replay.Failure); Assert.False(replay.Changed);
        Assert.Equal(3, f.Scalar("SELECT state FROM marriage_proposals"));
    }

    [Fact]
    public async Task Expired_requests_answered_with_acceptance_are_stale_yet_recorded_expired()
    {
        await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
        Assert.True((await InTx(f, () => Couple(f).ProposeAsync(f.Session, peer, default))).Succeeded);
        w.Clock.Now += TimeSpan.FromMinutes(2);
        var proposal = await InTx(f, () => Couple(f).RespondProposalAsync(peer, f.Session, 0, default));
        Assert.Equal(MarriageProposalFailure.Stale, proposal.Failure); Assert.False(proposal.Changed); Assert.Null(proposal.Proposal);
        Assert.Equal(3, f.Scalar("SELECT state FROM marriage_proposals"));
        await EligibleAsync(w, f.Session, peer);
        Assert.True((await InTx(f, () => Couple(f).RequestReservationAsync(f.Session, peer, 0, 0, default))).Succeeded);
        w.Clock.Now += TimeSpan.FromMinutes(2);
        var ceremony = await InTx(f, () => Couple(f).RespondReservationAsync(peer, f.Session, 0, default));
        Assert.Equal(MarriageCeremonyFailure.Stale, ceremony.Failure); Assert.True(ceremony.Changed);
        Assert.Equal(MarriageCeremonyState.Expired, ceremony.Ceremony!.State);
        Assert.Equal(3, f.Scalar("SELECT state FROM marriage_ceremonies"));
    }

    [Fact]
    public async Task Pending_proposal_lapses_when_a_participant_session_is_replaced()
    {
        await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
        var first = await InTx(f, () => Couple(f).ProposeAsync(f.Session, peer, default));
        var reconnected = peer with { ConnectionGeneration = peer.ConnectionGeneration + 1 };
        f.Services.GetRequiredService<ISelectedSessionDomainService>().Select(reconnected);
        f.Services.GetRequiredService<ITownAreaDomainService>().Enter(new(reconnected.CharacterId, reconnected.TownAreaLease(),
            new(1, 2, 1), new(2, 1, 100, 200, 0), true));
        var next = await InTx(f, () => Couple(f).ProposeAsync(f.Session, reconnected, default));
        Assert.True(next.Succeeded); Assert.True(next.Changed);
        Assert.NotEqual(first.Proposal!.Id, next.Proposal!.Id);
        Assert.Equal(3, f.Scalar($"SELECT state FROM marriage_proposals WHERE id='{first.Proposal.Id:N}'"));
        Assert.Equal(2, f.Scalar($"SELECT response FROM marriage_proposals WHERE id='{first.Proposal.Id:N}'"));
    }

    [Fact]
    public async Task Reservation_guards_answer_their_own_failure_codes_and_failed_rechecks_expire()
    {
        await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
        var third = AddCharacter(f, 3, 73, job: 0, gold: 500_000);
        Assert.Equal(MarriageCeremonyFailure.Ineligible, (await InTx(f, () => Couple(f).RequestReservationAsync(f.Session, peer, 0, 0, default))).Failure);
        await EligibleAsync(w, f.Session, peer);
        Assert.Equal(MarriageCeremonyFailure.Invalid, (await InTx(f, () => Couple(f).RequestReservationAsync(f.Session, f.Session, 0, 0, default))).Failure);
        Assert.Equal(MarriageCeremonyFailure.Ineligible, (await InTx(f, () => Couple(f).RequestReservationAsync(third, peer, 0, 0, default))).Failure);
        MoveTo(f, peer, 2, 0);
        Assert.Equal(MarriageCeremonyFailure.Unavailable, (await InTx(f, () => Couple(f).RequestReservationAsync(f.Session, peer, 0, 0, default))).Failure);
        MoveTo(f, peer, 2, 1);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_ceremonies"));
        Assert.True((await InTx(f, () => Couple(f).RequestReservationAsync(f.Session, peer, 0, 0, default))).Succeeded);
        Assert.Equal(MarriageCeremonyFailure.Invalid, (await InTx(f, () => Couple(f).RespondReservationAsync(peer, f.Session, 3, default))).Failure);
        MoveTo(f, peer, 2, 0);
        var unavailable = await InTx(f, () => Couple(f).RespondReservationAsync(peer, f.Session, 0, default));
        Assert.Equal(MarriageCeremonyFailure.Unavailable, unavailable.Failure); Assert.True(unavailable.Changed);
        Assert.Equal(MarriageCeremonyState.Expired, unavailable.Ceremony!.State);
        MoveTo(f, peer, 2, 1);
        Assert.True((await InTx(f, () => Couple(f).RequestReservationAsync(f.Session, peer, 0, 0, default))).Succeeded);
        f.Sql("DELETE FROM marriage_quest_bindings WHERE character_id=71 AND quest_id=20737");
        var ineligible = await InTx(f, () => Couple(f).RespondReservationAsync(peer, f.Session, 0, default));
        Assert.Equal(MarriageCeremonyFailure.Ineligible, ineligible.Failure); Assert.True(ineligible.Changed);
        Assert.Equal(MarriageCeremonyState.Expired, ineligible.Ceremony!.State);
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM marriage_ceremonies WHERE state=3 AND response=2"));
    }

    [Fact]
    public async Task Expired_request_answered_with_timeout_is_recorded_once()
    {
        await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
        await EligibleAsync(w, f.Session, peer);
        Assert.True((await InTx(f, () => Couple(f).RequestReservationAsync(f.Session, peer, 0, 0, default))).Succeeded);
        w.Clock.Now += TimeSpan.FromMinutes(2);
        var expired = await InTx(f, () => Couple(f).RespondReservationAsync(peer, f.Session, 2, default));
        Assert.Equal(MarriageCeremonyFailure.None, expired.Failure); Assert.True(expired.Changed);
        Assert.Equal(MarriageCeremonyState.Expired, expired.Ceremony!.State);
        Assert.Equal(3, f.Scalar("SELECT state FROM marriage_ceremonies"));
    }

    [Fact]
    public async Task Wedding_preparation_refuses_pending_unpaid_or_offline_ceremonies_and_leaves_nothing()
    {
        await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
        await EligibleAsync(w, f.Session, peer);
        var pending = (await InTx(f, () => Couple(f).RequestReservationAsync(f.Session, peer, 0, 0, default))).Ceremony!;
        await Assert.ThrowsAsync<MarriageWeddingUnavailableException>(() => InTx(f, () => Couple(f).OpenWeddingAsync(pending, default)));
        var reserved = (await InTx(f, () => Couple(f).RespondReservationAsync(peer, f.Session, 0, default))).Ceremony!;
        f.Sql("UPDATE character_stats SET carried_gold=499999 WHERE character_id=71");
        await Assert.ThrowsAsync<MarriageWeddingUnavailableException>(() => InTx(f, () => Couple(f).OpenWeddingAsync(reserved, default)));
        f.Sql("UPDATE character_stats SET carried_gold=500000 WHERE character_id=71");
        GoOffline(f, peer);
        await Assert.ThrowsAsync<MarriageWeddingUnavailableException>(() => InTx(f, () => Couple(f).OpenWeddingAsync(reserved, default)));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_weddings"));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_church_rooms"));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_church_admissions"));
        Assert.Equal(500000, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=71"));
        Assert.Equal(1, f.Scalar("SELECT state FROM marriage_ceremonies"));
    }

    [Fact]
    public async Task Wedding_preparation_records_paid_fee_and_gives_each_spouse_the_other_as_peer()
    {
        await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
        var ceremony = await ReserveAsync(w);
        var entries = await InTx(f, () => Couple(f).OpenWeddingAsync(ceremony, default));
        Assert.Equal(new[] { f.Session.CharacterId, peer.CharacterId }, entries.Select(x => x.Actor.CharacterId));
        Assert.Equal(new[] { peer.CharacterId, f.Session.CharacterId }, entries.Select(x => x.Church.Peer));
        Assert.Equal(500000, f.Scalar("SELECT paid_gold FROM marriage_weddings WHERE phase=0"));
        Assert.Equal(MarriageWeddingPhase.Preparing, await Query(f).WeddingPhaseAsync(ceremony.Id, default));
        Assert.Equal(MarriageCeremonyFailure.Occupied, (await InTx(f, () => Couple(f).RequestReservationAsync(f.Session, peer, 0, 0, default))).Failure);
    }
}
