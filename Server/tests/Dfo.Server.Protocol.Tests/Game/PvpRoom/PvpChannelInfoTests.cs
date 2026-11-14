using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.PvpChannelInfo;
using Dfo.Server.Protocol.Tests.Game.Party;
using Dfo.Server.Testing;
using Dfo.Server.Application.Commands;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.PvpRoom;

/// <summary>CMD 0x00C3 (Spec AC-001): the upstream success on a duel channel, the upstream failure elsewhere.</summary>
public sealed class PvpChannelInfoTests
{
    [Theory]
    [InlineData(ChannelEntryKind.Pvp, true, true)]
    [InlineData(ChannelEntryKind.FreeDuel, true, true)]
    [InlineData(ChannelEntryKind.General, true, false)]
    [InlineData(ChannelEntryKind.Pvp, false, false)]
    public Task Channel_info_is_available_only_on_a_duel_channel(ChannelEntryKind kind, bool wellFormed, bool available) =>
        TestGameplay.RunAsync(async () =>
        {
            var world = new PvpRoomCommandTests.World();
            var outbox = new RecordingCommandOutbox();
            var handler = new GamePvpChannelInfoCommandHandler(world.Sessions, new Channels(kind), outbox,
                NullLogger<GamePvpChannelInfoCommandHandler>.Instance);
            var command = new GamePvpChannelInfoCommand(PvpRoomCommandTests.Dispatch(world.A),
                new PvpChannelInfoRequest(GameMessageMetadata.Empty, wellFormed));
            var request = new CommandRequestContext.ProtocolNonEffect(command.Dispatch, command.Message, command.Frame, () => true);

            Assert.Equal(available, await handler.HandleAsync(
                new(Guid.NewGuid(), typeof(GamePvpChannelInfoCommand), handler.GetType()) { Request = request }, command, default));

            var (connection, message) = Assert.Single(outbox.Sent);
            Assert.Equal(world.A.ConnectionId, connection);
            Assert.Equal(new PvpChannelInfoResponse(available), message);
        });

    private sealed class Channels(ChannelEntryKind kind) : IChannelEntryDomainService
    {
        public ValueTask<ChannelEntryIdentity> ResolveAsync(int listenerPort, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ChannelEntryIdentity(1, listenerPort, kind, kind == ChannelEntryKind.Pvp ? (byte)8 : null));

        public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int characterLevel) => throw new NotSupportedException();

        public ValueTask<ChannelLoginEntry> ResolveLoginAsync(int listenerPort, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
