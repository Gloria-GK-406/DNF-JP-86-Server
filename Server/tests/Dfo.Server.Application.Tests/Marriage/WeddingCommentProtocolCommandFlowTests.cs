using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Dfo.Server.Application.Tests.Marriage.MarriageHarness;

namespace Dfo.Server.Application.Tests.Marriage;

public sealed class WeddingCommentProtocolCommandFlowTests
{
    [Fact]
    public async Task Real_wire_invitation_gift_church_and_guestbook_keep_authorization_assets_and_native_replies()
    {
        await using var w = await ChurchTownAsync(Tier0() with { GiftMinimum = 10_000, GiftMaximum = 100_000 }, s =>
        {
            s.RemoveAll<IGameCommandOutbox>();
            s.AddSingleton(typeof(IGameCommandOutbox), typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.GameCommandOutbox", true)!);
            s.AddSingleton<ISelectedCharacterCurrentViewDomainService, CurrentViews>();
        });
        var f = w.F;
        var (_, room) = await PreparedAsync(w);
        var partnerQuery = new byte[18]; BinaryPrimitives.WriteUInt32LittleEndian(partnerQuery.AsSpan(14), checked((uint)w.Peer.CharacterId.Value));
        var recorded = f.Services.GetRequiredService<RecordedOutbound>();
        var beforeGold = f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=71");
        await Wire(w, f.Session, 0x03F3, partnerQuery);
        var partner = SingleFrame(recorded, 1, 0x03F3);
        Assert.Equal(98, partner.Length - 15); Assert.Equal(1, partner[15]);
        Assert.Equal((uint)w.Peer.CharacterId.Value, BinaryPrimitives.ReadUInt32LittleEndian(partner.AsSpan(20)));
        Assert.Equal(beforeGold, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=71"));
        recorded.Clear();
        var guest = AddCharacter(f, 3, 73, 0, 50_000);
        var outbound = f.Services.GetRequiredService<RecordedOutbound>();
        var letter = new byte[22]; BinaryPrimitives.WriteUInt32LittleEndian(letter.AsSpan(15), 73);
        await Wire(w, f.Session, 0x03F6, letter);
        var inviteAck = SingleFrame(outbound, 1, 0x03F6);
        Assert.Equal(new byte[] { 1, 0, 0, 0, 0, 9, 0 }, inviteAck[15..]);
        var invitation = Assert.Single(outbound.Entries, e => e.Connection.ConnectionId == guest.ConnectionId);
        Assert.Equal((ushort)0x03D5, Type(invitation.Frames.Span));
        var key = BinaryPrimitives.ReadUInt32LittleEndian(invitation.Frames.Span[15..]);
        Assert.Equal(1, f.Scalar("SELECT count(*) FROM marriage_wedding_guests"));
        outbound.Clear();

        var gift = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(gift, key); BinaryPrimitives.WriteInt32LittleEndian(gift.AsSpan(4), 10_000);
        await Wire(w, guest, 0x03F2, gift);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(SingleFrame(outbound, 1, 0x03F2).AsSpan(16)));
        Assert.Equal(40_000, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=73"));
        outbound.Clear();
        await Wire(w, guest, 0x03F2, gift);
        Assert.Equal(40_000, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=73"));
        outbound.Clear();

        var church = new byte[23]; BinaryPrimitives.WriteUInt32LittleEndian(church.AsSpan(14), key);
        BinaryPrimitives.WriteUInt32LittleEndian(church.AsSpan(18), room.AreaId); church[22] = 1;
        await Wire(w, guest, 0x03F7, church);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(SingleFrame(outbound, 1, 0x03F7).AsSpan(16)));
        TestGameplay.Run(f.Services, () => Assert.Equal(room.AreaId,
            f.Services.GetRequiredService<Dfo.Server.Abstractions.World.ITownAreaDomainService>().Find(guest.CharacterId)!.Area.AreaId));
        outbound.Clear();

        byte[] text = [0xD0, 0xC2, 0xBB, 0xE9];
        var post = new byte[8 + text.Length]; BinaryPrimitives.WriteUInt32LittleEndian(post, key);
        BinaryPrimitives.WriteInt32LittleEndian(post.AsSpan(4), text.Length); text.CopyTo(post, 8);
        await Wire(w, guest, 0x03F8, post);
        Assert.Equal(new byte[] { 1, 0, 0, 0, 0 }, SingleFrame(outbound, 1, 0x03F8)[15..]);
        Assert.Equal(1, f.Scalar("SELECT count(*) FROM marriage_guest_comments WHERE author_id=73"));
        var id = checked((uint)f.Scalar("SELECT id FROM marriage_guest_comments")); outbound.Clear();

        var read = new byte[19]; BinaryPrimitives.WriteUInt32LittleEndian(read.AsSpan(14), key);
        await Wire(w, f.Session, 0x03FA, read);
        var body = SingleFrame(outbound, 1, 0x03FA).AsSpan(15).ToArray();
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(1)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(9)));
        Assert.Equal(id, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(13)));
        Assert.Equal(text, body[^text.Length..]); outbound.Clear();

        var delete = new byte[23]; BinaryPrimitives.WriteUInt32LittleEndian(delete.AsSpan(14), key);
        BinaryPrimitives.WriteUInt32LittleEndian(delete.AsSpan(18), id);
        await Wire(w, guest, 0x03F9, delete);
        Assert.Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(SingleFrame(outbound, 1, 0x03F9).AsSpan(16)));
        Assert.Equal(1, f.Scalar("SELECT count(*) FROM marriage_guest_comments")); outbound.Clear();
        await Wire(w, f.Session, 0x03F9, delete);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(SingleFrame(outbound, 1, 0x03F9).AsSpan(16)));
        Assert.Equal(0, f.Scalar("SELECT count(*) FROM marriage_guest_comments")); outbound.Clear();
        await Wire(w, f.Session, 0x03FA, read);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(SingleFrame(outbound, 1, 0x03FA).AsSpan(24)));
        Assert.DoesNotContain(outbound.Entries, e => e.IsClose);
    }

    private sealed class CurrentViews : ISelectedCharacterCurrentViewDomainService
    {
        public async ValueTask<SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity session, CancellationToken ct) =>
            await Characters.CharacterEntryInventoryTests.CreateLifecycleResultAsync(session.AccountId, session.CharacterId.Value, 0);
    }
    private static ushort Type(ReadOnlySpan<byte> frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame[1..]);
    private static byte[] SingleFrame(RecordedOutbound outbound, byte selector, ushort type) =>
        Assert.Single(outbound.Frames, frame => frame[0] == selector && Type(frame) == type);
    private static ValueTask Wire(MarriageHarness.World world, SelectedSessionIdentity actor, ushort type, byte[] body)
    {
        var services = world.F.Services;
        var metadata = new GameMessageMetadata(1, type, 0, 7) { InboundOrdinal = 7, SequenceIsFresh = true }.WithRawBody(body);
        var registration = services.GetServices<GameMessageDecoderRegistration>().Single(x => x.Selector == 1 && x.Type == type);
        var input = ((IGameMessageDecoder)services.GetRequiredService(registration.DecoderType)).Decode(metadata, body)!;
        return services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(MarriageTestCommands.Dispatch(services, actor), input, default);
    }
}
