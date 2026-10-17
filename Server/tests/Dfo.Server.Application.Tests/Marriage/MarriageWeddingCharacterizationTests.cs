using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Application.Tests.Marriage.MarriageHarness;

namespace Dfo.Server.Application.Tests.Marriage;

/// <summary>
/// Characterization of B-09 (invitations), B-10 (gifts and guest entry), B-11 (completion),
/// B-12 (cancellation) and B-13 (cleanup retry).
/// </summary>
public sealed class MarriageWeddingCharacterizationTests
{
    private static readonly MarriageChurchLocation Exit = new(2, 2, 2016, 212);

    private sealed class RecordingDelivery : IItemDeliveryDomainService
    {
        public List<(int Character, string Grants, string Source, ItemDeliveryMode Mode)> Calls { get; } = [];
        public ValueTask<long> PendingQuantityAsync(CharacterId character, int itemId, CancellationToken ct) => ValueTask.FromResult(0L);
        public ValueTask<ItemDeliveryReceipt> DeliverAsync(AccountId account, CharacterId character, IReadOnlyList<ItemDeliveryGrant> grants,
            string source, CancellationToken ct, ItemDeliveryMode mode)
        {
            Calls.Add((character.Value, string.Join(",", grants.Select(x => $"{x.ItemId}x{x.Count}")), source, mode));
            return ValueTask.FromResult(new ItemDeliveryReceipt([], [], 0));
        }
    }

    private static int[] Characters<T>(IEnumerable<T> entries, Func<T, SelectedSessionIdentity> actor) =>
        entries.Select(x => actor(x).CharacterId.Value).Order().ToArray();

    [Fact]
    public async Task Invitation_status_codes_per_inviter_quota_random_dispatch_and_cutoff()
    {
        await using var w = await ChurchTownAsync(Tier0(invitationLimit: 2)); var f = w.F; var peer = w.Peer;
        var (ceremony, room) = await PreparedAsync(w);
        var g73 = AddCharacter(f, 3, 73, job: 0); AddCharacter(f, 4, 74, job: 0); AddCharacter(f, 5, 75, job: 0);
        AddCharacter(f, 6, 76, job: 0, channel: 2);
        Task<MarriageWeddingLetterResult> Invite(SelectedSessionIdentity actor, byte mode, uint target) =>
            InTx(f, () => Venue(f).InviteAsync(actor, mode, target, default));
        Assert.Equal(-1, (await Invite(f.Session with { ConnectionGeneration = f.Session.ConnectionGeneration + 1 }, 0, 73)).Status);
        Assert.Equal(-1, (await Invite(f.Session, 2, 73)).Status);
        Assert.Equal(-1, (await Invite(g73, 0, 74)).Status);
        Assert.Equal(-6, (await Invite(f.Session, 0, 72)).Status);
        Assert.Equal(-6, (await Invite(f.Session, 0, 71)).Status);
        Assert.Equal(-7, (await Invite(f.Session, 0, 999)).Status);
        Assert.Equal(-7, (await Invite(f.Session, 0, 76)).Status);
        var first = await Invite(f.Session, 0, 73);
        Assert.Equal(0, first.Status); Assert.Equal((ushort)1, first.Remaining);
        Assert.Equal(72u, Assert.Single(first.Letters).Key); Assert.Equal(new[] { 73u }, first.Invited);
        var duplicate = await Invite(f.Session, 0, 73);
        Assert.Equal(-5, duplicate.Status); Assert.Equal((ushort)1, duplicate.Remaining);
        Assert.Equal(-5, (await Invite(peer, 0, 73)).Status);
        Assert.Equal((ushort)1, await Query(f).RemainingInvitationsAsync(ceremony.Id, f.Session.CharacterId, default));
        Assert.Equal((ushort)2, await Query(f).RemainingInvitationsAsync(ceremony.Id, peer.CharacterId, default));
        var random = await Invite(f.Session, 1, 0);
        Assert.Equal(0, random.Status); Assert.Equal((ushort)0, random.Remaining);
        Assert.Contains(Assert.Single(random.Letters).Guest.CharacterId.Value, new[] { 74, 75 });
        Assert.Equal(-4, (await Invite(f.Session, 0, 75)).Status);
        var byPeer = await Invite(peer, 1, 0);
        Assert.Equal(0, byPeer.Status); Assert.Equal((ushort)1, byPeer.Remaining);
        Assert.Equal(71u, Assert.Single(byPeer.Letters).Key);
        Assert.Equal(new[] { 73u, 74u, 75u }, byPeer.Invited.Order());
        var none = await Invite(peer, 1, 0);
        Assert.Equal(-7, none.Status); Assert.Equal((ushort)1, none.Remaining);
        w.Clock.Now = room.ReadyUntil.AddSeconds(-61);
        Assert.Equal(-7, (await Invite(peer, 1, 0)).Status);
        w.Clock.Now = room.ReadyUntil.AddSeconds(-60);
        Assert.Equal(-1, (await Invite(peer, 1, 0)).Status);
        Assert.Equal(3, f.Scalar("SELECT COUNT(*) FROM marriage_wedding_guests"));
    }

    [Fact]
    public async Task Gift_payment_bounds_idempotency_and_guest_entry_codes()
    {
        await using var w = await ChurchTownAsync(Tier0() with { GiftMinimum = 10_000, GiftMaximum = 100_000 }); var f = w.F;
        var (_, room) = await PreparedAsync(w);
        var rich = AddCharacter(f, 3, 73, job: 0, gold: 500_000);
        var poor = AddCharacter(f, 4, 74, job: 0, gold: 5_000);
        var late = AddCharacter(f, 5, 75, job: 0, gold: 100_000);
        var key = await InviteAsync(f, f.Session, rich);
        var poorKey = await InviteAsync(f, f.Session, poor);
        var lateKey = await InviteAsync(f, f.Session, late);
        Task<int> Pay(SelectedSessionIdentity guest, uint k, int gold) => InTx(f, () => Venue(f).GiftAsync(guest, k, gold, default));
        Task<MarriageChurchEntryResult> Enter(SelectedSessionIdentity guest, uint k, uint area) =>
            InTx(f, () => Venue(f).EnterChurchAsync(guest, k, area, 1, default));
        Assert.Equal(-1, await Pay(rich, key + 1, 10_000));
        Assert.Equal(-1, await Pay(rich, key, 9_999));
        Assert.Equal(-1, await Pay(rich, key, 100_001));
        Assert.Equal(500_000, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=73"));
        Assert.Equal(-10, (await Enter(rich, key + 1, room.AreaId)).Status);
        Assert.Equal(-10, (await Enter(rich, key, room.AreaId + 1u)).Status);
        Assert.Equal(-6, (await Enter(rich, key, room.AreaId)).Status);
        Assert.Equal(0, await Pay(rich, key, 10_000));
        Assert.Equal(0, await Pay(rich, key, 10_000));
        Assert.Equal(-1, await Pay(rich, key, 20_000));
        Assert.Equal(490_000, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=73"));
        Assert.Equal(10_000, f.Scalar("SELECT gold FROM marriage_wedding_guests WHERE guest_character_id=73"));
        Assert.Equal(-1, await Pay(poor, poorKey, 10_000));
        Assert.Equal(5_000, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=74"));
        Assert.Equal(0, f.Scalar("SELECT gold FROM marriage_wedding_guests WHERE guest_character_id=74"));
        MoveTo(f, late, 2, 1, channel: 2);
        Assert.Equal(-1, await Pay(late, lateKey, 10_000));
        MoveTo(f, late, 2, 1);
        var entered = await Enter(rich, key, room.AreaId);
        Assert.Equal(0, entered.Status); Assert.Equal(new CharacterId(72), entered.Snapshot!.Peer);
        Assert.Equal(room.AreaId, f.Scalar("SELECT area_id FROM characters WHERE character_id=73"));
        Assert.Equal(3, f.Scalar("SELECT COUNT(*) FROM marriage_church_admissions"));
        w.Clock.Now = room.ReadyUntil;
        Assert.Equal(-1, await Pay(late, lateKey, 10_000));
        Assert.Equal(100_000, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=75"));
        Assert.Equal(-10, (await Enter(rich, key, room.AreaId)).Status);
    }

    [Fact]
    public async Task Completion_splits_gifts_returns_bracket_rewards_and_queues_every_cleanup()
    {
        var delivery = new RecordingDelivery();
        var content = Tier0() with
        {
            Tiers = [new MarriageCeremonyTier(0, 500_000, 0, 10) { GiftItemId = 5001 }],
            GiftChargePercent = 30,
            GuestRewards = [new(15_000, 0, 6001, 1), new(15_000, 0, 6002, 2), new(50_000, 0, 6003, 1), new(15_000, 1, 6009, 1)],
        };
        await using var w = await ChurchTownAsync(content, s => s.AddSingleton<IItemDeliveryDomainService>(delivery)); var f = w.F; var peer = w.Peer;
        var (ceremony, room) = await PreparedAsync(w);
        var inside = AddCharacter(f, 3, 73, job: 0, gold: 100_000);
        var entered = AddCharacter(f, 4, 74, job: 0, gold: 100_000);
        var paid = AddCharacter(f, 5, 75, job: 0, gold: 100_000);
        var unpaid = AddCharacter(f, 6, 76, job: 0);
        await PaidGuestAsync(f, inside, await InviteAsync(f, f.Session, inside), 10_000, room);
        await PaidGuestAsync(f, entered, await InviteAsync(f, f.Session, entered), 20_001, room);
        await PaidGuestAsync(f, paid, await InviteAsync(f, f.Session, paid), 60_000);
        await InviteAsync(f, peer, unpaid);
        Arrive(f, f.Session, room); Arrive(f, peer, room); Arrive(f, inside, room);
        w.Clock.Now = room.ReadyUntil;
        var result = (await InTx(f, () => Venue(f).AdvanceAsync(ceremony.Id, default)))!;
        Assert.Equal(MarriageWeddingPhase.Completed, result.Wedding.Phase);
        Assert.Equal(31_500, f.Scalar("SELECT gold FROM mailbox_messages WHERE title='婚礼礼金' AND receiver_character_id=71"));
        Assert.Equal(31_500, f.Scalar("SELECT gold FROM mailbox_messages WHERE title='婚礼礼金' AND receiver_character_id=72"));
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM mailbox_messages WHERE title='婚礼礼金'"));
        Assert.Equal(
            new[]
            {
                (71, "5001x1", "普通婚礼奖励", ItemDeliveryMode.MailOnly),
                (72, "5001x1", "普通婚礼奖励", ItemDeliveryMode.MailOnly),
                (73, "6001x1,6002x2", "婚礼宾客回礼", ItemDeliveryMode.MailOnly),
                (74, "6003x1", "婚礼宾客回礼", ItemDeliveryMode.MailOnly),
            },
            delivery.Calls.OrderBy(x => x.Character));
        Assert.Equal(new[] { 71, 72 }, Characters(result.Participants, x => x.Actor));
        Assert.Equal(new[] { 73 }, Characters(result.Guests, x => x.Actor));
        Assert.Equal(new CharacterId(72), result.Guests[0].Church.Peer);
        Assert.Equal(new[] { 74, 75, 76 }, Characters(result.Cleanup, x => x.Actor));
        Assert.Empty(result.Departures);
        Assert.Equal(6, f.Scalar("SELECT COUNT(*) FROM marriage_wedding_cleanup"));
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM marriage_wedding_cleanup WHERE character_id IN (71,72)"));
    }

    [Fact]
    public async Task Cancellation_expires_ceremony_refunds_each_payer_and_moves_saved_church_positions_out()
    {
        await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
        var (ceremony, room) = await PreparedAsync(w);
        var inside = AddCharacter(f, 3, 73, job: 0, gold: 100_000);
        var outside = AddCharacter(f, 4, 74, job: 0, gold: 100_000);
        await PaidGuestAsync(f, inside, await InviteAsync(f, f.Session, inside), 10_000, room);
        await PaidGuestAsync(f, outside, await InviteAsync(f, f.Session, outside), 20_001);
        Arrive(f, f.Session, room); Arrive(f, inside, room);
        var untouched = (f.Scalar("SELECT town_id FROM characters WHERE character_id=74"), f.Scalar("SELECT area_id FROM characters WHERE character_id=74"));
        w.Clock.Now = room.ReadyUntil;
        var result = (await InTx(f, () => Venue(f).AdvanceAsync(ceremony.Id, default)))!;
        Assert.Equal(MarriageWeddingPhase.Cancelled, result.Wedding.Phase);
        Assert.Null(result.Wedding.CompletedAt);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_ceremonies WHERE state=3 AND response=2"));
        Assert.Equal(500_000, f.Scalar("SELECT COALESCE(SUM(gold),0) FROM mailbox_messages WHERE title='婚礼退款' AND receiver_character_id=71"));
        Assert.Equal(10_000, f.Scalar("SELECT COALESCE(SUM(gold),0) FROM mailbox_messages WHERE title='婚礼退款' AND receiver_character_id=73"));
        Assert.Equal(20_001, f.Scalar("SELECT COALESCE(SUM(gold),0) FROM mailbox_messages WHERE title='婚礼退款' AND receiver_character_id=74"));
        Assert.Equal(3, f.Scalar("SELECT COUNT(*) FROM mailbox_messages WHERE title='婚礼退款'"));
        foreach (var character in new[] { 71, 72, 73 })
        {
            Assert.Equal(Exit.TownId, f.Scalar($"SELECT town_id FROM characters WHERE character_id={character}"));
            Assert.Equal(Exit.AreaId, f.Scalar($"SELECT area_id FROM characters WHERE character_id={character}"));
            Assert.Equal(Exit.X, f.Scalar($"SELECT pos_x FROM characters WHERE character_id={character}"));
            Assert.Equal(Exit.Y, f.Scalar($"SELECT pos_y FROM characters WHERE character_id={character}"));
        }
        Assert.Equal(untouched, (f.Scalar("SELECT town_id FROM characters WHERE character_id=74"), f.Scalar("SELECT area_id FROM characters WHERE character_id=74")));
        Assert.Equal(new[] { 71, 73 }, Characters(result.Departures, x => x.Actor));
        Assert.All(result.Departures, x => { Assert.Equal(1, x.Channel); Assert.Equal(Exit, x.Location); });
        Assert.Equal(new[] { 72, 74 }, Characters(result.Cleanup, x => x.Actor));
        Assert.Equal(new[] { 73 }, Characters(result.Guests, x => x.Actor));
        Assert.Empty(result.Standing);
        Assert.Equal(4, f.Scalar("SELECT COUNT(*) FROM marriage_wedding_cleanup"));
        Assert.Equal((ushort)0, await Query(f).RemainingInvitationsAsync(ceremony.Id, f.Session.CharacterId, default));
    }

    [Fact]
    public async Task Cleanup_retry_completes_entries_owned_by_a_newer_wedding_without_projection_and_releases_the_empty_room()
    {
        await using var w = await ChurchTownAsync(); var f = w.F;
        var (old, oldRoom) = await PreparedAsync(w);
        var groom = AddCharacter(f, 3, 73, job: 0, gold: 100_000);
        var bride = AddCharacter(f, 4, 74, job: 12);
        var guest = AddCharacter(f, 5, 75, job: 0, gold: 100_000);
        await PaidGuestAsync(f, groom, await InviteAsync(f, f.Session, groom), 10_000);
        await PaidGuestAsync(f, guest, await InviteAsync(f, f.Session, guest), 10_000);
        w.Clock.Now = oldRoom.ReadyUntil;
        var cancelled = (await InTx(f, () => Venue(f).AdvanceAsync(old.Id, default)))!;
        Assert.Equal(new[] { 71, 72, 73, 75 }, Characters(cancelled.Cleanup, x => x.Actor));
        var newer = await ReserveAsync(w, groom, bride);
        var newerRoom = (await InTx(f, () => Couple(f).OpenWeddingAsync(newer, default)))[0].Church.Room;
        Assert.NotEqual(oldRoom.AreaId, newerRoom.AreaId);
        await InviteAsync(f, groom, guest);
        var retry = (await InTx(f, () => Venue(f).AdvanceAsync(old.Id, default)))!;
        Assert.Equal(new[] { 71, 72 }, Characters(retry.Cleanup, x => x.Actor));
        Assert.Empty(retry.Departures);
        Assert.Equal(2, f.Scalar($"SELECT COUNT(*) FROM marriage_wedding_cleanup WHERE ceremony_id='{old.Id:N}'"));
        Assert.Equal(0, f.Scalar($"SELECT COUNT(*) FROM marriage_wedding_cleanup WHERE ceremony_id='{old.Id:N}' AND character_id IN (73,75)"));
        Assert.Equal(0, f.Scalar($"SELECT COUNT(*) FROM marriage_church_rooms WHERE ceremony_id='{old.Id:N}'"));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_church_rooms"));
    }

    [Fact]
    public async Task Cleanup_retry_after_completion_waits_for_an_empty_room_then_moves_saved_positions_out()
    {
        await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
        var (ceremony, room) = await PreparedAsync(w);
        Arrive(f, f.Session, room); Arrive(f, peer, room);
        w.Clock.Now = room.ReadyUntil;
        Assert.Equal(MarriageWeddingPhase.Completed, (await InTx(f, () => Venue(f).AdvanceAsync(ceremony.Id, default)))!.Wedding.Phase);
        GoOffline(f, peer);
        Assert.Null(await InTx(f, () => Venue(f).AdvanceAsync(ceremony.Id, default)));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_church_rooms"));
        Assert.Equal(room.AreaId, f.Scalar("SELECT area_id FROM characters WHERE character_id=72"));
        MoveTo(f, f.Session, 2, 1);
        var retry = (await InTx(f, () => Venue(f).AdvanceAsync(ceremony.Id, default)))!;
        Assert.Equal(new[] { 71 }, Characters(retry.Cleanup, x => x.Actor));
        Assert.Empty(retry.Departures);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_church_rooms"));
        foreach (var character in new[] { 71, 72 })
        {
            Assert.Equal(Exit.AreaId, f.Scalar($"SELECT area_id FROM characters WHERE character_id={character}"));
            Assert.Equal(Exit.X, f.Scalar($"SELECT pos_x FROM characters WHERE character_id={character}"));
        }
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM marriage_wedding_cleanup"));
    }
}
