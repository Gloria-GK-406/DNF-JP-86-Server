using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using static Dfo.Server.Application.Tests.Marriage.MarriageHarness;

namespace Dfo.Server.Application.Tests.Marriage;

/// <summary>Characterization of B-05 (newlywed entry), B-06 (crossing into a church) and B-08 (wedding appearance).</summary>
public sealed class MarriageChurchCharacterizationTests
{
    [Fact]
    public async Task Newlywed_entry_answers_minus_ten_for_mismatches_and_minus_seven_after_opening()
    {
        await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
        await ReserveAsync(w);
        var church = (await InTx(f, () => Venue(f).PrepareChurchAsync(f.Session, default)))!;
        var partner = checked((uint)peer.CharacterId.Value);
        Task<MarriageChurchEntryResult> Enter(SelectedSessionIdentity actor, uint target, uint area, byte flag) =>
            InTx(f, () => Venue(f).EnterChurchAsync(actor, target, area, flag, default));
        Assert.Equal(-10, (await Enter(f.Session, partner + 1, church.Room.AreaId, 0)).Status);
        Assert.Equal(-10, (await Enter(f.Session, partner, church.Room.AreaId + 1u, 0)).Status);
        Assert.Equal(-10, (await Enter(f.Session, partner, church.Room.AreaId, 1)).Status);
        var stale = f.Session with { ConnectionGeneration = f.Session.ConnectionGeneration + 1 };
        Assert.Equal(-10, (await Enter(stale, partner, church.Room.AreaId, 0)).Status);
        MoveTo(f, f.Session, 2, 1, channel: 2);
        Assert.Equal(-10, (await Enter(f.Session, partner, church.Room.AreaId, 0)).Status);
        MoveTo(f, f.Session, 2, 1);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_church_admissions"));
        w.Clock.Now = church.Room.ReadyUntil;
        var late = await Enter(f.Session, partner, church.Room.AreaId, 0);
        Assert.Equal(-7, late.Status); Assert.Null(late.Snapshot);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_church_admissions"));
    }

    [Fact]
    public async Task Crossing_admits_guests_by_admission_and_completed_weddings_only_from_inside()
    {
        await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
        var (ceremony, room) = await PreparedAsync(w);
        var guest = AddCharacter(f, 3, 73, job: 0, gold: 100_000);
        var stranger = AddCharacter(f, 4, 74, job: 0);
        Task<bool> Cross(SelectedSessionIdentity actor, int channel = 1) =>
            InTx(f, () => Query(f).CanCrossAsync(actor, channel, room.TownId, room.AreaId, default));
        var key = await InviteAsync(f, f.Session, guest);
        await PaidGuestAsync(f, guest, key, 10_000);
        Assert.False(await Cross(guest));
        Assert.Equal(0, (await InTx(f, () => Venue(f).EnterChurchAsync(guest, key, room.AreaId, 1, default))).Status);
        Assert.True(await Cross(guest));
        Assert.False(await Cross(guest, channel: 2));
        Assert.False(await Cross(stranger));
        w.Clock.Now = room.ReadyUntil.AddSeconds(-1);
        Assert.True(await Cross(guest)); Assert.True(await Cross(f.Session));
        Arrive(f, f.Session, room); Arrive(f, peer, room);
        w.Clock.Now = room.ReadyUntil;
        Assert.False(await Cross(guest));
        Arrive(f, guest, room);
        Assert.True(await Cross(guest));
        Assert.Equal(MarriageWeddingPhase.Completed, (await InTx(f, () => Venue(f).AdvanceAsync(ceremony.Id, default)))!.Wedding.Phase);
        Assert.True(await Cross(f.Session));
        MoveTo(f, f.Session, 2, 1);
        Assert.False(await Cross(f.Session));
        Assert.True(await Cross(guest));
        MoveTo(f, guest, 2, 1);
        Assert.False(await Cross(guest));
    }

    [Fact]
    public async Task Wedding_appearance_depends_on_session_admission_channel_and_phase()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
            var ceremony = await ReserveAsync(w);
            Assert.Null(await Query(f).AppearanceAsync(f.Session, default));
            var room = (await InTx(f, () => Couple(f).OpenWeddingAsync(ceremony, default)))[0].Church.Room;
            var bride = (await Query(f).AppearanceAsync(f.Session, default))!;
            var groom = (await Query(f).AppearanceAsync(peer, default))!;
            Assert.Equal(peer.CharacterId, bride.Peer); Assert.Equal(f.Session.CharacterId, groom.Peer);
            Assert.Equal("learning"u8.ToArray(), bride.ApplicantName); Assert.Equal("other"u8.ToArray(), bride.PartnerName);
            Assert.Equal(bride.ApplicantName, groom.ApplicantName); Assert.Equal(bride.PartnerName, groom.PartnerName);
            Assert.Equal(room, bride.Room);
            Assert.Null(await Query(f).AppearanceAsync(f.Session with { ConnectionGeneration = f.Session.ConnectionGeneration + 1 }, default));
            MoveTo(f, f.Session, 2, 1, channel: 2);
            Assert.Null(await Query(f).AppearanceAsync(f.Session, default));
            MoveTo(f, f.Session, 2, 1);
            var guest = AddCharacter(f, 3, 73, job: 0, gold: 100_000);
            var key = await InviteAsync(f, peer, guest);
            Assert.Equal(71u, key);
            await PaidGuestAsync(f, guest, key, 10_000);
            Assert.Null(await Query(f).AppearanceAsync(guest, default));
            Assert.Equal(0, (await InTx(f, () => Venue(f).EnterChurchAsync(guest, key, room.AreaId, 1, default))).Status);
            Assert.Equal(f.Session.CharacterId, (await Query(f).AppearanceAsync(guest, default))!.Peer);
            Arrive(f, f.Session, room); Arrive(f, peer, room);
            w.Clock.Now = room.ReadyUntil;
            await InTx(f, () => Venue(f).AdvanceAsync(ceremony.Id, default));
            Assert.NotNull(await Query(f).AppearanceAsync(f.Session, default));
            Assert.Null(await Query(f).AppearanceAsync(guest, default));
            MoveTo(f, f.Session, 2, 1);
            Assert.Null(await Query(f).AppearanceAsync(f.Session, default));
        });
    }
}
