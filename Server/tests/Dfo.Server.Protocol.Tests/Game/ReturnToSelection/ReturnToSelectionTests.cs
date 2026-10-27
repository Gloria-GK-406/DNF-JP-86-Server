using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.ReturnToSelection;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.ReturnToSelection;

public sealed class ReturnToSelectionTests
{
    [Fact]
    public void The_observed_empty_request_decodes()
    {
        var decoded = new ReturnToSelectionMessageDecoder().Decode(GameMessageMetadata.Empty, []);
        Assert.IsType<ReturnToSelectionRequest>(decoded);
    }

    [Fact]
    public void A_request_with_an_unexpected_body_is_refused() =>
        Assert.Throws<InvalidDataException>(() =>
            new ReturnToSelectionMessageDecoder().Decode(GameMessageMetadata.Empty, [0]));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_return_batch_uses_the_reference_order_and_cmd_envelope(bool knight)
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var sender = new EncodedSender(provider);
        var sessions = TestSessions.Directory(out var state);
        var session = sessions.InWorld(state, new AccountId(1), new CharacterId(1), "return");
        var roster = new AccountCharacterRoster(session.AccountId, 24,
            knight ? [new CharacterSummary(session.CharacterId, 0, new byte[20], 12, 0, 1, 0, [])] : []);
        var characters = ProtocolAlgorithmFixture.Decorate(ProtocolAlgorithmFixture.Default<ICharacterDomainService>(),
            (method, _) => method.Name == nameof(ICharacterDomainService.GetAccountRosterWithAppearanceAsync)
                ? (true, (object)ValueTask.FromResult(roster)) : (false, null));
        var cleanup = ProtocolAlgorithmFixture.Decorate(ProtocolAlgorithmFixture.Default<ICommandSelectedSessionCleanup>(),
            (method, _) => method.Name == nameof(ICommandSelectedSessionCleanup.CleanupAsync)
                ? (true, (object)ValueTask.FromResult(SelectedSessionCleanupNotificationPlan.Empty(session))) : (false, null));
        var handler = ProtocolAlgorithmFixture.Create<GameReturnToSelectionCommandHandler>(sessions, characters, cleanup, sender);
        var input = new ReturnToSelectionRequest(GameMessageMetadata.Empty);
        var dispatch = new NetworkDispatchContext(new(session.ConnectionId, NetworkEndpointKind.Game, 10011));
        await TestGameplay.RunAsync(async () =>
        {
            var request = new CommandRequestContext.ProtocolNonEffect(dispatch, input, new ProtocolFrameMetadata.Game(input.Metadata), () => true);
            try
            {
                await handler.HandleAsync(new(Guid.NewGuid(), typeof(GameReturnToSelectionCommand), handler.GetType()) { Request = request }, new(dispatch, input), default);
            }
            finally { await request.DrainOperationsAsync(); request.Close(); }
        });

        Assert.Equal(knight ? new ushort[] { 0x0245, 7, 2, 0x0289 } : [7, 2, 0x0289],
            sender.Frames.Select(frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1))));
        var offset = knight ? 1 : 0;
        if (knight)
        {
            Assert.Equal(0, sender.Frames[0][0]);
            Assert.Equal(new byte[20], sender.Frames[0][15..]);
        }
        Assert.Equal(1, sender.Frames[offset][0]);
        Assert.Equal(new byte[] { 1 }, sender.Frames[offset][15..]);
        Assert.Equal(0, sender.Frames[offset + 1][0]);
        Assert.Equal(0, sender.Frames[offset + 1][7]);
        Assert.Equal(2, sender.Frames[offset + 1][15]);
        Assert.Equal(0, sender.Frames[offset + 2][0]);
    }

    /// <summary>
    /// The frames the protocol's registered encoders made at a real registration point, read from its outbox; every
    /// call goes to the real protocol sender over it (TASK-25). An explicit delivery fails there as the old double
    /// refused one.
    /// </summary>
    private sealed class EncodedSender : IGameMessageSender
    {
        private readonly RecordedOutbound outbound;
        public EncodedSender(ServiceProvider provider)
        {
            var encoders = new RegistrationEncoder();
            encoders.Use(provider);
            outbound = new RecordedOutbound(encoders);
            outbound.RefuseExplicit();
        }
        public List<byte[]> Frames => [.. outbound.Frames];
        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendCmdAsync(connectionId, message, cancellationToken);
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendNotiAsync(connectionId, message, cancellationToken);
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken) =>
            outbound.Game.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);
    }
}
