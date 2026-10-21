using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

internal sealed partial class MarriageDomainService
{
    public async ValueTask<MarriageChurchSnapshot?> PrepareChurchAsync(SelectedSessionIdentity actor, CancellationToken ct) =>
        await ReservedAsync(actor, ct).ConfigureAwait(false) is { } reserved
            ? await church.PrepareAsync(actor, reserved.Ceremony, ct).ConfigureAwait(false) : null;

    public ValueTask<MarriageChurchEntryResult> EnterChurchAsync(SelectedSessionIdentity actor, uint key, uint area, byte flag, CancellationToken ct) =>
        flag == 1 ? EnterGuestAsync(actor, key, area, ct) : EnterNewlywedAsync(actor, key, area, flag, ct);

    private async ValueTask<MarriageChurchEntryResult> EnterNewlywedAsync(SelectedSessionIdentity actor,
        uint peer, uint churchArea, byte flag, CancellationToken ct)
    {
        if (flag != 0 || await ReservedAsync(actor, ct).ConfigureAwait(false) is not { } reserved) return new(-10);
        return await church.EnterNewlywedAsync(actor, reserved.Ceremony, peer, churchArea, ct).ConfigureAwait(false);
    }

    private async ValueTask<MarriageChurchEntryResult> EnterGuestAsync(SelectedSessionIdentity actor, uint key, uint area, CancellationToken ct)
    {
        if (await GuestAsync(actor, key, ct).ConfigureAwait(false) is not { } found) return new(-10);
        var (wedding, invitation, room) = found;
        if (room.AreaId != area) return new(-10);
        if (invitation.Gold == 0) return new(-6);
        return await church.EnterGuestAsync(actor, wedding.Ceremony, room, key, ct).ConfigureAwait(false);
    }

    public async ValueTask<MarriageWeddingLetterResult> InviteAsync(SelectedSessionIdentity actor, byte mode, uint target, CancellationToken ct)
    {
        if (!eligibility.IsCurrent(actor) || mode > 1) return new(-1, 0, []);
        var wedding = (await settlement.PendingAsync(ct).ConfigureAwait(false)).SingleOrDefault(x => x.Phase == MarriageWeddingPhase.Preparing
            && (x.Ceremony.Applicant.CharacterId == actor.CharacterId || x.Ceremony.Partner.CharacterId == actor.CharacterId));
        if (wedding is null || await church.RoomAsync(wedding.Ceremony.Id, ct).ConfigureAwait(false) is not { } room
            || clock.GetUtcNow() >= room.ReadyUntil.AddSeconds(-settlement.Rules(wedding.Ceremony.Tier).InvitationCutoffSeconds)) return new(-1, 0, []);
        var ceremony = wedding.Ceremony;
        var left = await RemainingInvitationsAsync(ceremony.Id, actor.CharacterId, ct).ConfigureAwait(false);
        if (left == 0) return new(-4, 0, []);
        var invited = (await guests.InvitationsAsync(ceremony.Id, ct).ConfigureAwait(false)).Select(x => x.Guest).ToHashSet();
        if (mode == 0 && invited.Any(x => x.Value == target)) return new(-5, left, []);
        if (mode == 0 && (target == ceremony.Applicant.CharacterId.Value || target == ceremony.Partner.CharacterId.Value)) return new(-6, left, []);
        var targets = guests.Candidates(ceremony, invited, room.ChannelId, mode, target, left);
        if (targets.Length == 0) return new(-7, left, []);
        var key = checked((uint)PeerOf(ceremony, actor.CharacterId).Value);
        var letters = new List<MarriageWeddingLetter>();
        foreach (var guest in targets)
        {
            await guests.InviteAsync(new(ceremony.Id, guest.CharacterId, actor.CharacterId, key), ct).ConfigureAwait(false);
            letters.Add(new(guest, await church.SnapshotAsync(ceremony, room, key, ct).ConfigureAwait(false), key));
        }
        return new(0, checked((ushort)(left - letters.Count)), letters) { Invited = await InvitedAsync(ceremony.Id, ct).ConfigureAwait(false) };
    }

    public async ValueTask<int> GiftAsync(SelectedSessionIdentity actor, uint key, int gold, CancellationToken ct)
    {
        if (await GuestAsync(actor, key, ct).ConfigureAwait(false) is not { } found) return -1;
        var (wedding, invitation, _) = found;
        return await guests.PayAsync(actor, wedding, invitation, gold, ct).ConfigureAwait(false);
    }

    /// <summary>The preparing wedding that invited the actor under this key, while the actor is on its channel and it has not opened.</summary>
    private async ValueTask<(MarriageWedding, MarriageWeddingInvitation, MarriageChurchRoom)?> GuestAsync(SelectedSessionIdentity actor,
        uint key, CancellationToken ct)
    {
        if (!eligibility.IsCurrent(actor)) return null;
        foreach (var wedding in await settlement.PendingAsync(ct).ConfigureAwait(false))
        {
            if (wedding.Phase != MarriageWeddingPhase.Preparing) continue;
            var invitation = await guests.InvitationAsync(wedding.Ceremony.Id, actor.CharacterId, key, ct).ConfigureAwait(false);
            if (invitation is not null && await church.RoomAsync(wedding.Ceremony.Id, ct).ConfigureAwait(false) is { } room
                && church.ChannelOf(actor.CharacterId) == room.ChannelId && clock.GetUtcNow() < room.ReadyUntil)
                return (wedding, invitation, room);
        }
        return null;
    }

    public async ValueTask<MarriageWeddingTransition?> AdvanceAsync(Guid id, CancellationToken ct)
    {
        var wedding = await settlement.ReadAsync(id, ct).ConfigureAwait(false);
        var room = await church.RoomAsync(id, ct).ConfigureAwait(false);
        if (wedding is null) return null;
        if (wedding.Phase != MarriageWeddingPhase.Preparing)
            return await RetryCleanupAsync(wedding, room, ct).ConfigureAwait(false);
        if (room is null) return null;
        var occupied = church.Occupants(room);
        if (clock.GetUtcNow() < room.ReadyUntil) return null;
        var ceremony = wedding.Ceremony;
        var both = church.IsPresent(ceremony.Applicant, room) && church.IsPresent(ceremony.Partner, room)
            && await church.IsAdmittedAsync(id, ceremony.Applicant.CharacterId, ct).ConfigureAwait(false)
            && await church.IsAdmittedAsync(id, ceremony.Partner.CharacterId, ct).ConfigureAwait(false);
        var phase = both ? MarriageWeddingPhase.Completed : MarriageWeddingPhase.Cancelled;
        var finishedAt = clock.GetUtcNow();
        var relation = await ledger.ReadAsync(ceremony.RelationId, ct).ConfigureAwait(false);
        if (!await settlement.FinishAsync(id, phase, finishedAt, ct).ConfigureAwait(false)) return null;
        await ledger.MustCommitAsync(relation, both ? MarriageTrigger.WeddingCompleted : MarriageTrigger.WeddingCancelled,
            null, finishedAt, ct).ConfigureAwait(false);
        if (both) await housingState.EnsureAsync(ceremony.RelationId, ct).ConfigureAwait(false);
        var invitations = await guests.InvitationsAsync(id, ct).ConfigureAwait(false);
        var rule = settlement.Rules(ceremony.Tier);
        var participants = new List<MarriageWeddingAdmission>();
        foreach (var original in new[] { ceremony.Applicant, ceremony.Partner })
        {
            var actor = eligibility.InWorldSessionOf(original.CharacterId);
            if (actor is not null && actor.AccountId == original.AccountId)
                participants.Add(new(actor, await church.SnapshotAsync(ceremony, room,
                    checked((uint)PeerOf(ceremony, original.CharacterId).Value), ct).ConfigureAwait(false)));
        }
        IReadOnlyList<MarriageWeddingStanding> standing = [];
        var departures = new List<MarriageWeddingDeparture>();
        if (both) standing = await settlement.CompleteAsync(ceremony, rule, invitations, ct).ConfigureAwait(false);
        else
        {
            await reservations.TransitionAsync(ceremony, MarriageCeremonyState.Expired, 2, ct).ConfigureAwait(false);
            await settlement.RefundAsync(wedding, invitations, ct).ConfigureAwait(false);
            // Include offline admitted characters: persisted church coordinates must not survive cancellation.
            await settlement.PersistExitAsync(room, new[] { ceremony.Applicant.CharacterId, ceremony.Partner.CharacterId }
                .Concat(invitations.Select(x => x.Guest)).Concat(occupied.Select(x => x.CharacterId)).Distinct(),
                rule.ExitLocation, ct).ConfigureAwait(false);
            departures.AddRange(occupied.Select(x => new MarriageWeddingDeparture(x, room.ChannelId, rule.ExitLocation)));
        }
        var guestEntries = new List<MarriageWeddingAdmission>();
        foreach (var actor in occupied.Where(x => x.CharacterId != ceremony.Applicant.CharacterId && x.CharacterId != ceremony.Partner.CharacterId))
            if (invitations.SingleOrDefault(x => x.Guest == actor.CharacterId) is { } invitation)
                guestEntries.Add(new(actor, await church.SnapshotAsync(ceremony, room, invitation.Key, ct).ConfigureAwait(false)));
        foreach (var owner in new[] { ceremony.Applicant, ceremony.Partner })
            await settlement.QueueCleanupAsync(id, new(owner.CharacterId, await church.SnapshotAsync(ceremony, room,
                checked((uint)PeerOf(ceremony, owner.CharacterId).Value), ct).ConfigureAwait(false)), ct).ConfigureAwait(false);
        var cleanup = new List<MarriageWeddingAdmission>();
        if (!both) cleanup.AddRange(participants.Where(x => !occupied.Contains(x.Actor)));
        foreach (var invitation in invitations)
        {
            await settlement.QueueCleanupAsync(id, new(invitation.Guest, await church.SnapshotAsync(ceremony, room, invitation.Key, ct).ConfigureAwait(false)), ct).ConfigureAwait(false);
            var actor = eligibility.InWorldSessionOf(invitation.Guest);
            if (actor is null || occupied.Contains(actor)) continue;
            cleanup.Add(new(actor, await church.SnapshotAsync(ceremony, room, invitation.Key, ct).ConfigureAwait(false)));
        }
        return new(wedding with { Phase = phase, CompletedAt = both ? finishedAt : null }, participants, guestEntries)
            { Standing = standing, Departures = departures, Cleanup = cleanup };
    }

    private async ValueTask<MarriageWeddingTransition?> RetryCleanupAsync(MarriageWedding wedding, MarriageChurchRoom? room, CancellationToken ct)
    {
        var cleanup = new List<MarriageWeddingAdmission>();
        var departures = new List<MarriageWeddingDeparture>();
        foreach (var pending in await settlement.CleanupAsync(wedding.Ceremony.Id, ct).ConfigureAwait(false))
        {
            var actor = eligibility.InWorldSessionOf(pending.Character);
            if (actor is null) continue;
            var current = await AppearanceAsync(actor, ct).ConfigureAwait(false);
            var hasNewInvitation = false;
            foreach (var newer in await settlement.PendingAsync(ct).ConfigureAwait(false))
                if (newer.Ceremony.Id != wedding.Ceremony.Id && newer.Phase == MarriageWeddingPhase.Preparing
                    && await guests.IsInvitedAsync(newer.Ceremony.Id, pending.Character, ct).ConfigureAwait(false))
                { hasNewInvitation = true; break; }
            // Never erase a newer wedding's context after an old offline cleanup.
            if (hasNewInvitation || current is not null && current.Room.CeremonyId != wedding.Ceremony.Id)
            { await settlement.CompleteCleanupAsync(wedding.Ceremony.Id, pending.Character, ct).ConfigureAwait(false); continue; }
            if (church.Standing(actor) is not { } member) continue;
            if (room is not null && member.Area == new TownAreaKey(room.ChannelId, room.TownId, room.AreaId))
            {
                if (wedding.Phase == MarriageWeddingPhase.Cancelled)
                    departures.Add(new(actor, room.ChannelId, settlement.Rules(wedding.Ceremony.Tier).ExitLocation));
                continue; // Successful native animation owns its exit; op1 would interrupt it.
            }
            cleanup.Add(new(actor, pending.Church));
        }
        if (room is not null && church.IsEmpty(room))
        {
            await settlement.PersistExitAsync(room, (await settlement.CleanupAsync(wedding.Ceremony.Id, ct).ConfigureAwait(false)).Select(x => x.Character),
                settlement.Rules(wedding.Ceremony.Tier).ExitLocation, ct).ConfigureAwait(false);
            await church.ReleaseAsync(wedding.Ceremony.Id, ct).ConfigureAwait(false);
        }
        return cleanup.Count == 0 && departures.Count == 0 ? null : new(wedding, [], []) { Cleanup = cleanup, Departures = departures };
    }

    public ValueTask CompleteCleanupAsync(Guid ceremony, CharacterId character, CancellationToken ct) =>
        settlement.CompleteCleanupAsync(ceremony, character, ct);

    public async ValueTask<MarriageGuestbookResult> ReadGuestbookAsync(SelectedSessionIdentity actor, uint key, CancellationToken ct) =>
        await GuestbookAccessAsync(actor, key, ct).ConfigureAwait(false) is { } access
            ? new(0, await guestbook.ReadAsync(access.Relation, ct).ConfigureAwait(false)) : new(-1, []);

    public async ValueTask<int> PostGuestbookAsync(SelectedSessionIdentity actor, uint key, byte[] text, CancellationToken ct)
    {
        if (!guestbook.Accepts(text) || await GuestbookAccessAsync(actor, key, ct).ConfigureAwait(false) is not { CanPost: true } access) return -1;
        return await guestbook.PostAsync(access.Relation, actor.CharacterId, text, ct).ConfigureAwait(false);
    }

    public async ValueTask<int> DeleteGuestbookAsync(SelectedSessionIdentity actor, uint key, uint id, CancellationToken ct)
    {
        if (await GuestbookAccessAsync(actor, key, ct).ConfigureAwait(false) is not { Owner: true } access) return -1;
        return await guestbook.DeleteAsync(access.Relation, id, ct).ConfigureAwait(false) ? 0 : -1;
    }

    /// <summary>The key is a partner's character id taken from the client: a newlywed reaches it as their own partner, a guest by invitation.</summary>
    private async ValueTask<(Guid Relation, bool Owner, bool CanPost)?> GuestbookAccessAsync(SelectedSessionIdentity actor, uint key, CancellationToken ct)
    {
        if (!eligibility.IsCurrent(actor) || key == 0 || key > int.MaxValue) return null;
        var relation = await ledger.FindAsync(new CharacterId((int)key), ct).ConfigureAwait(false);
        if (relation is null || !MarriageStageRules.IsCouple(relation.Stage)) return null;
        if (IMarriageRelations.Belongs(relation, actor))
            return PartnerOf(relation, actor.CharacterId).CharacterId.Value == key ? (relation.Id, true, false) : null;
        foreach (var wedding in await settlement.PendingAsync(ct).ConfigureAwait(false))
        {
            if (wedding.Ceremony.RelationId != relation.Id || wedding.Phase == MarriageWeddingPhase.Cancelled) continue;
            var invitation = await guests.InvitationAsync(wedding.Ceremony.Id, actor.CharacterId, key, ct).ConfigureAwait(false);
            if (invitation is null || await church.RoomAsync(wedding.Ceremony.Id, ct).ConfigureAwait(false) is not { } room) continue;
            if (church.Standing(actor) is not { } member || member.Area.ChannelId != room.ChannelId) continue;
            return (relation.Id, false, member.Area == new TownAreaKey(room.ChannelId, room.TownId, room.AreaId)
                && await church.IsAdmittedAsync(wedding.Ceremony.Id, actor.CharacterId, ct).ConfigureAwait(false));
        }
        return null;
    }
}
