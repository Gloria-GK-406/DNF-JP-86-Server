using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Dfo.Server.Application.Tests.Marriage.MarriageHarness;

namespace Dfo.Server.Application.Tests.Parties;

public sealed class PartyMutationProtocolCommandFlowTests
{
    [Fact]
    public async Task Real_wire_leadership_transfer_updates_the_party_and_both_members_then_rejects_the_former_leader()
    {
        await using var w = await ChurchTownAsync(configure: services =>
        {
            services.RemoveAll<IGameCommandOutbox>();
            services.AddSingleton(typeof(IGameCommandOutbox), typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.GameCommandOutbox", true)!);
        });
        var services = w.F.Services;
        var parties = services.GetRequiredService<IPartyService>();
        var first = Character(w.F.Session); var second = Character(w.Peer);
        TestGameplay.Run(services, () =>
        {
            Assert.True(parties.RequestInvite(first, second, new(0, 100, 100, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(second, first).Succeeded);
        });
        var outbound = services.GetRequiredService<RecordedOutbound>();
        var nextSlot = TestGameplay.Run(services, () => parties.FindParty(first).Members.Single(x => x.CharacterId == second.CharacterId).Slot);
        await Transfer(w, w.F.Session, nextSlot);
        TestGameplay.Run(services, () => Assert.Equal(second.CharacterId, parties.FindParty(first).LeaderMember.CharacterId));
        foreach (var session in new[] { w.F.Session, w.Peer })
        {
            var messages = outbound.MessagesFor(session.ConnectionId);
            Assert.Contains(messages, m => m is UdpHostNotification);
            var roster = Assert.Single(messages.OfType<PartyRosterMessage>());
            Assert.True(roster.HostChanged); Assert.Equal(second.CharacterId, roster.Snapshot!.LeaderMember.CharacterId);
            Assert.Equal(new ushort[] { 0x001A, 0x0009 }, outbound.Entries.Where(e => e.Connection.ConnectionId == session.ConnectionId)
                .SelectMany(e => Frames(e.Frames.ToArray())).Select(frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1))));
        }
        Assert.DoesNotContain(outbound.Entries, e => e.IsClose);
        outbound.Clear();
        var formerSlot = TestGameplay.Run(services, () => parties.FindParty(first).Members.Single(x => x.CharacterId == first.CharacterId).Slot);
        await Transfer(w, w.F.Session, formerSlot);
        TestGameplay.Run(services, () => Assert.Equal(second.CharacterId, parties.FindParty(first).LeaderMember.CharacterId));
        Assert.Empty(outbound.Messages); Assert.DoesNotContain(outbound.Entries, e => e.IsClose);
    }
    private static PartyCharacterSnapshot Character(SelectedSessionIdentity session) =>
        new(session.AccountId, session.CharacterId, session.WireUserId, new(1, 2, 1));
    private static ValueTask Transfer(Marriage.MarriageHarness.World w, SelectedSessionIdentity actor, byte slot)
    {
        var services = w.F.Services; byte[] body = [slot];
        var metadata = new GameMessageMetadata(1, GameProtocol.ChangePartyLeaderType, 0, 7)
            { InboundOrdinal = 7, SequenceIsFresh = true }.WithRawBody(body);
        var registration = services.GetServices<GameMessageDecoderRegistration>().Single(x => x.Selector == 1 && x.Type == metadata.Type);
        var input = ((IGameMessageDecoder)services.GetRequiredService(registration.DecoderType)).Decode(metadata, body)!;
        return services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(
            Marriage.MarriageTestCommands.Dispatch(services, actor), input, default);
    }
    private static IEnumerable<byte[]> Frames(byte[] bytes)
    {
        for (var offset = 0; offset < bytes.Length;)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 3)));
            yield return bytes[offset..(offset + length)]; offset += length;
        }
    }
}
