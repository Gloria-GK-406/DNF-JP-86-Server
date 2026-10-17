using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

internal sealed class MarriageWeddingDomainService(IMarriageWeddingDomainStore store,
    IMarriageCeremonyDomainStore ceremonies, IMarriageChurchDomainService churches,
    IMarriageChurchDomainStore rooms, IMarriageChurchRuleService rules, IMarriageProposalRuleService genders,
    ISelectedSessionDomainService sessions, ISelectedSessionSnapshotQuery online, ITownAreaDomainService areas,
    ICharacterDomainService characters, ICharacterPositionDomainService positions,
    ISelectedSessionPositionPersistence persistence, IMailboxDomainService mail,
    IItemDeliveryDomainService delivery, ICharacterTable owners, ICharacterDomainStore characterStore, IDomainTransactionAccessor transactions, TimeProvider clock) : IMarriageWeddingDomainService
{
    public async ValueTask<bool> CanLeaveAsync(SelectedSessionIdentity actor, byte town, byte area, CancellationToken ct)
    {
        var church = await AppearanceAsync(actor, ct).ConfigureAwait(false);
        if (church is null || areas.Find(actor.CharacterId)?.Area != new TownAreaKey(church.Room.ChannelId, church.Room.TownId, church.Room.AreaId)
            || church.Room.TownId == town && church.Room.AreaId == area) return true;
        // A21 RVA0089464D locks ordinary departure below sixty seconds.
        return await PhaseAsync(church.Room.CeremonyId, ct).ConfigureAwait(false) != MarriageWeddingPhase.Preparing
            || church.Room.ReadyUntil - clock.GetUtcNow() >= TimeSpan.FromSeconds(60);
    }
    public async ValueTask<MarriageWeddingPhase?> PhaseAsync(Guid ceremony, CancellationToken ct) => (await store.ReadAsync(ceremony, ct).ConfigureAwait(false))?.Phase;

    public ValueTask CompleteCleanupAsync(Guid ceremony, CharacterId character, CancellationToken ct) => store.CompleteCleanupAsync(ceremony, character, ct);
    public ValueTask<bool> IsMarriedAsync(Guid relation, CancellationToken ct) => store.IsMarriedAsync(relation, ct);
    public ValueTask<IReadOnlyList<MarriageWedding>> PendingAsync(CancellationToken ct) => store.PendingAsync(ct);

    public async ValueTask<MarriageChurchSnapshot?> AppearanceAsync(SelectedSessionIdentity actor, CancellationToken ct)
    {
        if (sessions.Find(actor.AccountId) != actor || areas.Find(actor.CharacterId) is not { } member || !actor.Owns(member)) return null;
        foreach (var wedding in await store.PendingAsync(ct).ConfigureAwait(false))
        {
            var room = await rooms.ReadAsync(wedding.Ceremony.Id, ct).ConfigureAwait(false);
            if (room is null || member.Area.ChannelId != room.ChannelId
                || wedding.Phase != MarriageWeddingPhase.Preparing && member.Area != new TownAreaKey(room.ChannelId, room.TownId, room.AreaId)
                || !await rooms.IsAdmittedAsync(wedding.Ceremony.Id, actor.CharacterId, ct).ConfigureAwait(false)) continue;
            var key = wedding.Ceremony.Applicant.CharacterId == actor.CharacterId ? wedding.Ceremony.Partner.CharacterId.Value : wedding.Ceremony.Applicant.CharacterId.Value;
            var invitation = (await store.InvitationsAsync(wedding.Ceremony.Id, ct).ConfigureAwait(false)).SingleOrDefault(x => x.Guest == actor.CharacterId);
            return await SnapshotAsync(wedding.Ceremony, room, invitation?.Key ?? checked((uint)key), ct).ConfigureAwait(false);
        }
        return null;
    }

    public async ValueTask<IReadOnlyList<AccountId>> AccountsAsync(Guid ceremony, CancellationToken ct)
    {
        var wedding = await store.ReadAsync(ceremony, ct).ConfigureAwait(false);
        if (wedding is null) return [];
        var accounts = new HashSet<AccountId> { wedding.Ceremony.Applicant.AccountId, wedding.Ceremony.Partner.AccountId };
        foreach (var invitation in await store.InvitationsAsync(ceremony, ct).ConfigureAwait(false))
            if (await owners.FindOwnerAsync(invitation.Guest, ct).ConfigureAwait(false) is { } account) accounts.Add(account);
        return accounts.OrderBy(x => x.Value).ToArray();
    }

    public async ValueTask<IReadOnlyList<MarriageWeddingAdmission>> PrepareAsync(MarriageCeremony ceremony, CancellationToken ct)
    {
        if (await store.ReadAsync(ceremony.Id, ct).ConfigureAwait(false) is not null) return [];
        if (ceremony.State != MarriageCeremonyState.Reserved
            || await IsMarriedAsync(ceremony.RelationId, ct).ConfigureAwait(false)) throw new MarriageWeddingUnavailableException();
        var rule = rules.WeddingRules(ceremony.Tier);
        var result = new List<MarriageWeddingAdmission>();
        foreach (var original in new[] { ceremony.Applicant, ceremony.Partner })
        {
            var actor = sessions.FindByCharacter(original.CharacterId);
            if (actor != original || await churches.PrepareAsync(actor, ct).ConfigureAwait(false) is not { } church)
                throw new MarriageWeddingUnavailableException();
            var admitted = await churches.EnterAsync(actor, checked((uint)church.Peer.Value), church.Room.AreaId, 0, ct).ConfigureAwait(false);
            if (!admitted.Succeeded) throw new MarriageWeddingUnavailableException();
            result.Add(new(actor, church));
        }
        if (await characters.Get(ceremony.Applicant.CharacterId).TrySpendGoldAsync(rule.Gold, ct).ConfigureAwait(false) is null)
            throw new MarriageWeddingUnavailableException();
        await store.CreateAsync(new(ceremony, MarriageWeddingPhase.Preparing, rule.Gold), ct).ConfigureAwait(false);
        return result;
    }

    private bool Present(SelectedSessionIdentity original, MarriageChurchRoom room)
    {
        var current = sessions.FindByCharacter(original.CharacterId);
        return current is not null && current.AccountId == original.AccountId
            && areas.Find(current.CharacterId) is { } member && current.Owns(member)
            && member.Area == new TownAreaKey(room.ChannelId, room.TownId, room.AreaId);
    }
    private async ValueTask<MarriageChurchSnapshot> SnapshotAsync(MarriageCeremony ceremony, MarriageChurchRoom room, uint key, CancellationToken ct)
    {
        var location = (await rules.LocationsAsync(room.TownId, ct).ConfigureAwait(false)).Single(x => x.AreaId == room.AreaId);
        var first = await characters.Get(ceremony.Applicant.CharacterId).IdentityAsync(ct).ConfigureAwait(false);
        var second = await characters.Get(ceremony.Partner.CharacterId).IdentityAsync(ct).ConfigureAwait(false);
        if (first is null || second is null) throw new MarriageWeddingUnavailableException();
        return new(room, new(checked((int)key)), genders.IsFemale(first.Job) ? first.Name : second.Name, genders.IsFemale(first.Job) ? second.Name : first.Name, location);
    }
    private async ValueTask RefundAsync(CharacterId character, int gold, string text, CancellationToken ct)
    {
        if (gold > 0) await mail.SendAsync(MailBuilder.FromSystem(character).WithText("婚礼退款", text).AddGold(gold).Build(), ct).ConfigureAwait(false);
    }
    public async ValueTask<MarriageWeddingTransition?> AdvanceAsync(Guid id, CancellationToken ct)
    {
        var wedding = await store.ReadAsync(id, ct).ConfigureAwait(false);
        var room = await rooms.ReadAsync(id, ct).ConfigureAwait(false);
        if (wedding is null) return null;
        if (wedding.Phase != MarriageWeddingPhase.Preparing)
            return await RetryCleanupAsync(wedding, room, ct).ConfigureAwait(false);
        if (room is null) return null;
        var occupied = online.Snapshot().Where(x => areas.Find(x.CharacterId) is { } a && x.Owns(a)
            && a.Area == new TownAreaKey(room.ChannelId, room.TownId, room.AreaId)).ToArray();
        if (clock.GetUtcNow() < room.ReadyUntil) return null;
        var ceremony = wedding.Ceremony;
        var both = Present(ceremony.Applicant, room) && Present(ceremony.Partner, room)
            && await rooms.IsAdmittedAsync(id, ceremony.Applicant.CharacterId, ct).ConfigureAwait(false)
            && await rooms.IsAdmittedAsync(id, ceremony.Partner.CharacterId, ct).ConfigureAwait(false);
        var phase = both ? MarriageWeddingPhase.Completed : MarriageWeddingPhase.Cancelled;
        var finishedAt = clock.GetUtcNow();
        if (!await store.FinishAsync(id, phase, finishedAt, ct).ConfigureAwait(false)) return null;
        var guests = await store.InvitationsAsync(id, ct).ConfigureAwait(false);
        var rule = rules.WeddingRules(ceremony.Tier);
        var participants = new List<MarriageWeddingAdmission>();
        foreach (var original in new[] { ceremony.Applicant, ceremony.Partner })
        {
            var actor = sessions.FindByCharacter(original.CharacterId);
            if (actor is not null && actor.AccountId == original.AccountId)
            {
                var peer = original.CharacterId == ceremony.Applicant.CharacterId ? ceremony.Partner.CharacterId : ceremony.Applicant.CharacterId;
                participants.Add(new(actor, await SnapshotAsync(ceremony, room, checked((uint)peer.Value), ct).ConfigureAwait(false)));
            }
        }
        var standing = new List<MarriageWeddingStanding>();
        var departures = new List<MarriageWeddingDeparture>();
        if (both)
        {
            foreach (var owner in new[] { ceremony.Applicant, ceremony.Partner })
            {
                if (rule.CeremonyPositions.Count == 2)
                {
                    var identity = await characters.Get(owner.CharacterId).IdentityAsync(ct).ConfigureAwait(false) ?? throw new MarriageWeddingUnavailableException();
                    // Assign the two PVF ground positions consistently with our bride/groom projection.
                    var spot = rule.CeremonyPositions[genders.IsFemale(identity.Job) ? 0 : 1];
                    var actor = sessions.FindByCharacter(owner.CharacterId) ?? throw new MarriageWeddingUnavailableException();
                    var position = positions.Find(owner.CharacterId) ?? throw new MarriageWeddingUnavailableException();
                    position = position with { X = spot.X, Y = spot.Y };
                    await persistence.PersistAsync(actor.AccountId, actor.CharacterId, position, ct).ConfigureAwait(false);
                    standing.Add(new(actor, position));
                }
                if (rule.GiftItemId > 0) await delivery.DeliverAsync(owner.AccountId, owner.CharacterId,
                    [new ItemDeliveryGrant(rule.GiftItemId, 1)], "普通婚礼奖励", ct, ItemDeliveryMode.MailOnly).ConfigureAwait(false);
                var share = guests.Sum(x => (long)x.Gold) * (100 - rule.GiftChargePercent) / 100 / 2;
                if (share > 0) await mail.SendAsync(MailBuilder.FromSystem(owner.CharacterId).WithText("婚礼礼金", "婚礼宾客礼金结算").AddGold(checked((int)share)).Build(), ct).ConfigureAwait(false);
            }
            foreach (var guest in guests.Where(x => x.Gold > 0))
            {
                var bracket = rule.GuestRewards.Where(x => x.MaximumGold >= guest.Gold).OrderBy(x => x.MaximumGold).FirstOrDefault()?.MaximumGold;
                var rewards = rule.GuestRewards.Where(x => x.MaximumGold == bracket).Select(x => new ItemDeliveryGrant(x.ItemId, x.Count)).ToArray();
                if (rewards.Length > 0 && await owners.FindOwnerAsync(guest.Guest, ct).ConfigureAwait(false) is { } owner)
                    await delivery.DeliverAsync(owner, guest.Guest, rewards, "婚礼宾客回礼", ct, ItemDeliveryMode.MailOnly).ConfigureAwait(false);
            }
        }
        else
        {
            await ceremonies.TransitionAsync(ceremony, MarriageCeremonyState.Expired, 2, ct).ConfigureAwait(false);
            await RefundAsync(ceremony.Applicant.CharacterId, wedding.PaidGold, "未能按时开场，退回婚礼费用。", ct).ConfigureAwait(false);
            foreach (var guest in guests) await RefundAsync(guest.Guest, guest.Gold, "婚礼取消，退回礼金。", ct).ConfigureAwait(false);
            // Include offline admitted characters: persisted church coordinates must not survive cancellation.
            await PersistExitAsync(room, new[] { ceremony.Applicant.CharacterId, ceremony.Partner.CharacterId }
                .Concat(guests.Select(x => x.Guest)).Concat(occupied.Select(x => x.CharacterId)).Distinct(),
                rule.ExitLocation, ct).ConfigureAwait(false);
            departures.AddRange(occupied.Select(x => new MarriageWeddingDeparture(x, room.ChannelId, rule.ExitLocation)));
        }
        var guestEntries = new List<MarriageWeddingAdmission>();
        foreach (var actor in occupied.Where(x => x.CharacterId != ceremony.Applicant.CharacterId && x.CharacterId != ceremony.Partner.CharacterId))
            if (guests.SingleOrDefault(x => x.Guest == actor.CharacterId) is { } invitation)
                guestEntries.Add(new(actor, await SnapshotAsync(ceremony, room, invitation.Key, ct).ConfigureAwait(false)));
        foreach (var owner in new[] { ceremony.Applicant, ceremony.Partner })
        {
            var key = owner.CharacterId == ceremony.Applicant.CharacterId ? ceremony.Partner.CharacterId : ceremony.Applicant.CharacterId;
            await store.QueueCleanupAsync(id, new(owner.CharacterId, await SnapshotAsync(ceremony, room, checked((uint)key.Value), ct).ConfigureAwait(false)), ct).ConfigureAwait(false);
        }
        var cleanup = new List<MarriageWeddingAdmission>();
        if (!both) cleanup.AddRange(participants.Where(x => !occupied.Contains(x.Actor)));
        foreach (var invitation in guests)
        {
            await store.QueueCleanupAsync(id, new(invitation.Guest, await SnapshotAsync(ceremony, room, invitation.Key, ct).ConfigureAwait(false)), ct).ConfigureAwait(false);
            var actor = sessions.FindByCharacter(invitation.Guest);
            if (actor is null || occupied.Contains(actor)) continue;
            cleanup.Add(new(actor, await SnapshotAsync(ceremony, room, invitation.Key, ct).ConfigureAwait(false)));
        }
        return new(wedding with { Phase = phase, CompletedAt = both ? finishedAt : null }, participants, guestEntries) { Standing = standing, Departures = departures, Cleanup = cleanup };
    }

    private async ValueTask PersistExitAsync(MarriageChurchRoom room, IEnumerable<CharacterId> charactersToMove, MarriageChurchLocation exit, CancellationToken ct)
    {
        foreach (var character in charactersToMove)
            if (await owners.FindOwnerAsync(character, ct).ConfigureAwait(false) is { } account
                && await characterStore.SessionIn(transactions.Current ?? throw new InvalidOperationException("Wedding advancement requires a transaction.")).ReadPositionAsync(new(account, character), ct).ConfigureAwait(false) is { } saved
                && saved.TownId == room.TownId && saved.AreaId == room.AreaId)
                await persistence.PersistAsync(account, character, saved with { TownId = exit.TownId, AreaId = exit.AreaId, X = exit.X, Y = exit.Y }, ct).ConfigureAwait(false);
    }

    private async ValueTask<MarriageWeddingTransition?> RetryCleanupAsync(MarriageWedding wedding, MarriageChurchRoom? room, CancellationToken ct)
    {
        var cleanup = new List<MarriageWeddingAdmission>();
        var departures = new List<MarriageWeddingDeparture>();
        foreach (var pending in await store.CleanupAsync(wedding.Ceremony.Id, ct).ConfigureAwait(false))
        {
            var actor = sessions.FindByCharacter(pending.Character);
            if (actor is null) continue;
            var current = await AppearanceAsync(actor, ct).ConfigureAwait(false);
            var hasNewInvitation = false;
            foreach (var newer in await store.PendingAsync(ct).ConfigureAwait(false))
                if (newer.Ceremony.Id != wedding.Ceremony.Id && newer.Phase == MarriageWeddingPhase.Preparing
                    && (await store.InvitationsAsync(newer.Ceremony.Id, ct).ConfigureAwait(false)).Any(x => x.Guest == pending.Character))
                { hasNewInvitation = true; break; }
            // Never erase a newer wedding's context after an old offline cleanup.
            if (hasNewInvitation || current is not null && current.Room.CeremonyId != wedding.Ceremony.Id)
            { await store.CompleteCleanupAsync(wedding.Ceremony.Id, pending.Character, ct).ConfigureAwait(false); continue; }
            var member = areas.Find(actor.CharacterId);
            if (member is null || !actor.Owns(member)) continue;
            if (room is not null && member.Area == new TownAreaKey(room.ChannelId, room.TownId, room.AreaId))
            {
                if (wedding.Phase == MarriageWeddingPhase.Cancelled)
                    departures.Add(new(actor, room.ChannelId, rules.WeddingRules(wedding.Ceremony.Tier).ExitLocation));
                continue; // Successful native animation owns its exit; op1 would interrupt it.
            }
            cleanup.Add(new(actor, pending.Church));
        }
        if (room is not null && !online.Snapshot().Any(x => areas.Find(x.CharacterId) is { } a && x.Owns(a)
            && a.Area == new TownAreaKey(room.ChannelId, room.TownId, room.AreaId)))
        {
            await PersistExitAsync(room, (await store.CleanupAsync(wedding.Ceremony.Id, ct).ConfigureAwait(false)).Select(x => x.Character),
                rules.WeddingRules(wedding.Ceremony.Tier).ExitLocation, ct).ConfigureAwait(false);
            await rooms.ReleaseAsync(wedding.Ceremony.Id, ct).ConfigureAwait(false);
        }
        return cleanup.Count == 0 && departures.Count == 0 ? null : new(wedding, [], []) { Cleanup = cleanup, Departures = departures };
    }

    public async ValueTask<IReadOnlyList<uint>> InvitedAsync(Guid ceremony, CancellationToken ct) =>
        (await store.InvitationsAsync(ceremony, ct).ConfigureAwait(false)).Select(x => checked((uint)x.Guest.Value)).ToArray();

    public async ValueTask<ushort> RemainingAsync(Guid ceremony, CharacterId actor, CancellationToken ct)
    {
        var wedding = await store.ReadAsync(ceremony, ct).ConfigureAwait(false);
        if (wedding is null || wedding.Phase != MarriageWeddingPhase.Preparing) return 0;
        var used = (await store.InvitationsAsync(ceremony, ct).ConfigureAwait(false)).Count(x => x.Inviter == actor);
        return checked((ushort)Math.Max(0, rules.WeddingRules(wedding.Ceremony.Tier).InvitationLimit - used));
    }
    public async ValueTask<MarriageWeddingLetterResult> InviteAsync(SelectedSessionIdentity actor, byte mode, uint target, CancellationToken ct)
    {
        if (sessions.Find(actor.AccountId) != actor || mode > 1) return new(-1, 0, []);
        var wedding = (await store.PendingAsync(ct).ConfigureAwait(false)).SingleOrDefault(x => x.Phase == MarriageWeddingPhase.Preparing
            && (x.Ceremony.Applicant.CharacterId == actor.CharacterId || x.Ceremony.Partner.CharacterId == actor.CharacterId));
        if (wedding is null || await rooms.ReadAsync(wedding.Ceremony.Id, ct).ConfigureAwait(false) is not { } room
            || clock.GetUtcNow() >= room.ReadyUntil.AddSeconds(-rules.WeddingRules(wedding.Ceremony.Tier).InvitationCutoffSeconds)) return new(-1, 0, []);
        var ceremony = wedding.Ceremony;
        var left = await RemainingAsync(ceremony.Id, actor.CharacterId, ct).ConfigureAwait(false);
        if (left == 0) return new(-4, 0, []);
        var invited = (await store.InvitationsAsync(ceremony.Id, ct).ConfigureAwait(false)).Select(x => x.Guest).ToHashSet();
        if (mode == 0 && invited.Any(x => x.Value == target)) return new(-5, left, []);
        if (mode == 0 && (target == ceremony.Applicant.CharacterId.Value || target == ceremony.Partner.CharacterId.Value)) return new(-6, left, []);
        var targets = online.Snapshot().Where(x => x.CharacterId != ceremony.Applicant.CharacterId && x.CharacterId != ceremony.Partner.CharacterId
            && !invited.Contains(x.CharacterId) && areas.Find(x.CharacterId)?.Area.ChannelId == room.ChannelId
            && (mode == 1 || x.CharacterId.Value == target)).Take(mode == 1 ? left : 1).ToArray();
        if (targets.Length == 0) return new(-7, left, []);
        var key = checked((uint)(actor.CharacterId == ceremony.Applicant.CharacterId ? ceremony.Partner.CharacterId.Value : ceremony.Applicant.CharacterId.Value));
        var letters = new List<MarriageWeddingLetter>();
        foreach (var guest in targets)
        {
            await store.InviteAsync(new(ceremony.Id, guest.CharacterId, actor.CharacterId, key), ct).ConfigureAwait(false);
            letters.Add(new(guest, await SnapshotAsync(ceremony, room, key, ct).ConfigureAwait(false), key));
        }
        return new(0, checked((ushort)(left - letters.Count)), letters) { Invited = await InvitedAsync(ceremony.Id, ct).ConfigureAwait(false) };
    }
    private async ValueTask<(MarriageWedding, MarriageWeddingInvitation, MarriageChurchRoom)?> GuestAsync(SelectedSessionIdentity actor, uint key, CancellationToken ct)
    {
        if (sessions.Find(actor.AccountId) != actor) return null;
        foreach (var wedding in await store.PendingAsync(ct).ConfigureAwait(false))
        {
            if (wedding.Phase != MarriageWeddingPhase.Preparing) continue;
            var invitation = (await store.InvitationsAsync(wedding.Ceremony.Id, ct).ConfigureAwait(false)).SingleOrDefault(x => x.Guest == actor.CharacterId && x.Key == key);
            if (invitation is not null && await rooms.ReadAsync(wedding.Ceremony.Id, ct).ConfigureAwait(false) is { } room
                && areas.Find(actor.CharacterId)?.Area.ChannelId == room.ChannelId && clock.GetUtcNow() < room.ReadyUntil)
                return (wedding, invitation, room);
        }
        return null;
    }
    public async ValueTask<int> PayAsync(SelectedSessionIdentity actor, uint key, int gold, CancellationToken ct)
    {
        if (await GuestAsync(actor, key, ct).ConfigureAwait(false) is not { } found) return -1;
        var (wedding, invitation, _) = found;
        var rule = rules.WeddingRules(wedding.Ceremony.Tier);
        if (gold < rule.GiftMinimum || gold > rule.GiftMaximum) return -1;
        if (invitation.Gold != 0) return invitation.Gold == gold ? 0 : -1;
        if (await characters.Get(actor.CharacterId).TrySpendGoldAsync(gold, ct).ConfigureAwait(false) is null) return -1;
        if (!await store.PayAsync(wedding.Ceremony.Id, actor.CharacterId, gold, ct).ConfigureAwait(false)) throw new MarriageWeddingUnavailableException();
        return 0;
    }
    public async ValueTask<MarriageChurchEntryResult> EnterGuestAsync(SelectedSessionIdentity actor, uint key, uint area, CancellationToken ct)
    {
        if (await GuestAsync(actor, key, ct).ConfigureAwait(false) is not { } found) return new(-10);
        var (wedding, invitation, room) = found;
        if (room.AreaId != area) return new(-10);
        if (invitation.Gold == 0) return new(-6);
        if (positions.Find(actor.CharacterId) is not { } standing) return new(-10);
        var church = await SnapshotAsync(wedding.Ceremony, room, key, ct).ConfigureAwait(false);
        await persistence.PersistAsync(actor.AccountId, actor.CharacterId, standing with
        { TownId = room.TownId, AreaId = room.AreaId, X = church.Location.X, Y = church.Location.Y }, ct).ConfigureAwait(false);
        await rooms.AdmitAsync(wedding.Ceremony.Id, actor.CharacterId, ct).ConfigureAwait(false);
        return new(0, church);
    }
}
