using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Data.Channel;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Protocol.Game.Login;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Application.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Game;

public sealed class GameLoginTests
{
    [Fact]
    public async Task CommandHandler_DelegatesTicketRedemptionToAuthenticationDomainService()
    {
        var onlineUserId = OnlineUserId.New();
        var authentication = new StubGameLoginAuthenticationDomainService(new AccountId(42));
        var handler = new RedeemGameLoginCommandHandler(authentication);
        var command = new RedeemGameLoginCommand(
            onlineUserId,
            "123456",
            "0123456789ABCDEF0123456789ABCDEF");
        var context = new CommandExecutionContext(
            Guid.NewGuid(),
            command.GetType(),
            handler.GetType(),
            CommandExecutionKind.Direct);

        var result = await handler.HandleAsync(context, command, CancellationToken.None);

        var typed = Assert.IsType<RedeemGameLoginResult>(result);
        Assert.Equal(new AccountId(42), typed.AccountId);
        Assert.Equal(onlineUserId, authentication.OnlineUserId);
        Assert.Equal("123456", authentication.MId);
        Assert.Equal("0123456789ABCDEF0123456789ABCDEF", authentication.Ticket);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData((byte)23, 23)]
    [InlineData((byte)8, 8)]
    [InlineData((byte)13, 13)]
    [InlineData((byte)24, 24)]
    public async Task MessageHandler_ResolvesConnectionAndSendsSuccessToSameConnection(byte? channelEnvironment, byte expectedEnvironment)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var account = new AccountId(7);
            var onlineUsers = new UserDomainServices(account);
            var connection = new NetworkConnectionContext(
                new NetworkConnectionId("Game:login-message"),
                NetworkEndpointKind.Game,
                10011);
            var anonymous = onlineUsers.RegisterConnection(connection);
            var sender = new RecordingSender();
            var bus = new PublishingRecordingCommandBus(onlineUsers, new AccountId(7));
            var accountEntryState = new StubAccountEntryStateDomainService();
            var closer = new RecordingCloser();
            var handler = new GameLoginHandler(
                onlineUsers,
                sender,
                bus,
                new StubServerConfigService(),
                accountEntryState,
                closer,
                NullLogger<GameLoginHandler>.Instance,
                new LoginChannelPolicy(channelEnvironment));

            await handler.HandleAsync(
                new NetworkDispatchContext(connection),
                new GameLoginRequest(
                    GameMessageMetadata.Empty,
                    "123456",
                    "0123456789ABCDEF0123456789ABCDEF"),
                CancellationToken.None);

            var command = Assert.IsType<RedeemGameLoginCommand>(bus.Command);
            Assert.Equal(anonymous.OnlineUserId, command.OnlineUserId);
            Assert.Equal("123456", command.MId);
            Assert.Equal("0123456789ABCDEF0123456789ABCDEF", command.Ticket);

            // The login response followed by the whole account-state batch, all to the connection the
            // request arrived on. The batch is asserted as a unit because the client treats it as one:
            // sending part of it left the roster screen unable to open character creation, and which
            // member is actually required has never been isolated.
            Assert.Equal(7, sender.Sends.Count);
            Assert.All(sender.Sends, send => Assert.Equal(connection.ConnectionId, send.ConnectionId));

            Assert.True(sender.Sends[0].IsCmd);
            Assert.Equal(
                new GameLoginResponse(true, expectedEnvironment, 10128, "192.0.2.20", 17200, 17200),
                sender.Sends[0].Message);

            Assert.All(sender.Sends.Skip(1), send => Assert.False(send.IsCmd));
            Assert.Equal(
                [
                    ServerMessageType.GameStateSynchronization,   // 0x00AD game options
                    ServerMessageType.GameStateSynchronization,   // 0x01C7 hotkeys
                    ServerMessageType.GameAuctionServiceState,
                    ServerMessageType.GameAuctionServiceState,
                    ServerMessageType.GameHonorLevelInfo,
                    ServerMessageType.GameInformNotice,
                ],
                sender.Sends.Skip(1).Select(send => send.Message.MessageType));

            Assert.Equal(new AccountId(7), accountEntryState.RequestedAccountId);
            Assert.Empty(closer.Closed);
        });
    }

    [Fact]
    public async Task MessageHandler_CommandFailureSendsNoSuccessAndRemovesTheAnonymousConnection()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var onlineUsers = new UserDomainServices(new AccountId(7));
            var connection = new NetworkConnectionContext(
                new NetworkConnectionId("Game:login-refused"),
                NetworkEndpointKind.Game,
                10011);
            _ = onlineUsers.RegisterConnection(connection);
            var sender = new RecordingSender();
            var closer = new RecordingCloser();
            var handler = new GameLoginHandler(
                onlineUsers,
                sender,
                new ThrowingCommandBus(),
                new StubServerConfigService(),
                new StubAccountEntryStateDomainService(),
                closer,
                NullLogger<GameLoginHandler>.Instance);

            await handler.HandleAsync(
                new NetworkDispatchContext(connection),
                new GameLoginRequest(GameMessageMetadata.Empty, "player", new string('t', 43)),
                CancellationToken.None);

            Assert.Empty(sender.Sends);
            Assert.False(onlineUsers.TryGetByConnection(connection.ConnectionId, out _));
            Assert.Equal([connection.ConnectionId], closer.Closed);
        });
    }

    [Fact]
    public async Task MessageHandler_InvalidTicketSendsNothingClosesAndDoesNotAuthenticate()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var onlineUsers = new UserDomainServices(new AccountId(7));
            var connection = new NetworkConnectionContext(
                new NetworkConnectionId("Game:invalid-ticket"),
                NetworkEndpointKind.Game,
                10011);
            _ = onlineUsers.RegisterConnection(connection);
            var sender = new RecordingSender();
            var closer = new RecordingCloser();
            var logger = new RecordingLogger();
            var handler = new GameLoginHandler(
                onlineUsers,
                sender,
                new ResultCommandBus(new RedeemGameLoginResult(null)),
                new StubServerConfigService(),
                new StubAccountEntryStateDomainService(),
                closer,
                logger);
            var loginId = "secret-player";
            var ticket = "secret-ticket-that-must-not-enter-the-log-123";

            await handler.HandleAsync(
                new NetworkDispatchContext(connection),
                new GameLoginRequest(GameMessageMetadata.Empty, loginId, ticket),
                CancellationToken.None);

            Assert.Empty(sender.Sends);
            Assert.False(onlineUsers.TryGetByConnection(connection.ConnectionId, out _));
            Assert.Equal([connection.ConnectionId], closer.Closed);
            Assert.Contains("ticket_rejected", logger.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(loginId, logger.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(ticket, logger.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task MessageHandler_SendFailureConsumesTheSuccessButRemovesItsPublishedIdentity()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var onlineUsers = new UserDomainServices(new AccountId(7));
            var connection = new NetworkConnectionContext(
                new NetworkConnectionId("Game:send-failure"),
                NetworkEndpointKind.Game,
                10011);
            _ = onlineUsers.RegisterConnection(connection);
            var sender = new RecordingSender(failAtAttempt: 2);
            var closer = new RecordingCloser();
            var handler = new GameLoginHandler(
                onlineUsers,
                sender,
                new PublishingRecordingCommandBus(onlineUsers, new AccountId(7)),
                new StubServerConfigService(),
                new StubAccountEntryStateDomainService(),
                closer,
                NullLogger<GameLoginHandler>.Instance);

            await handler.HandleAsync(
                new NetworkDispatchContext(connection),
                new GameLoginRequest(GameMessageMetadata.Empty, "player", new string('t', 43)),
                CancellationToken.None);

            Assert.Single(sender.Sends);
            Assert.True(sender.Sends[0].IsCmd);
            Assert.False(onlineUsers.TryGetByConnection(connection.ConnectionId, out _));
            Assert.Equal([connection.ConnectionId], closer.Closed);
        });
    }

    [Fact]
    public async Task MessageHandler_DoesNotSendWhenCommandReturnsWithoutPublishedIdentity()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var onlineUsers = new UserDomainServices(new AccountId(7));
            var connection = new NetworkConnectionContext(
                new NetworkConnectionId("Game:publication-missing"),
                NetworkEndpointKind.Game,
                10011);
            _ = onlineUsers.RegisterConnection(connection);
            var sender = new RecordingSender();
            var closer = new RecordingCloser();
            var handler = new GameLoginHandler(
                onlineUsers,
                sender,
                new ResultCommandBus(new RedeemGameLoginResult(new AccountId(7))),
                new StubServerConfigService(),
                new StubAccountEntryStateDomainService(),
                closer,
                NullLogger<GameLoginHandler>.Instance);

            await handler.HandleAsync(
                new NetworkDispatchContext(connection),
                new GameLoginRequest(GameMessageMetadata.Empty, "player", new string('t', 43)),
                CancellationToken.None);

            Assert.Empty(sender.Sends);
            Assert.False(onlineUsers.TryGetByConnection(connection.ConnectionId, out _));
            Assert.Equal([connection.ConnectionId], closer.Closed);
        });
    }

    private sealed class StubGameLoginAuthenticationDomainService(AccountId accountId)
        : IGameLoginAuthenticationDomainService
    {
        public OnlineUserId OnlineUserId { get; private set; }

        public string? MId { get; private set; }

        public string? Ticket { get; private set; }

        public ValueTask<AccountId?> AuthenticateSessionAsync(
            OnlineUserId onlineUserId,
            string mId,
            string ticket,
            CancellationToken cancellationToken)
        {
            OnlineUserId = onlineUserId;
            MId = mId;
            Ticket = ticket;
            return ValueTask.FromResult<AccountId?>(accountId);
        }
    }

    private sealed class PublishingRecordingCommandBus(
        UserDomainServices onlineUsers,
        AccountId accountId) : ICommandBus
    {
        public ICommand? Command { get; private set; }

        public async ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            Command = command;
            var login = Assert.IsType<RedeemGameLoginCommand>(command);
            await onlineUsers.AuthenticateForTestAsync(login.OnlineUserId);
            return (TResult)(object)new RedeemGameLoginResult(accountId);
        }
    }

    private sealed class ThrowingCommandBus : ICommandBus
    {
        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull =>
            ValueTask.FromException<TResult>(new InvalidOperationException("transaction failed"));
    }

    private sealed class ResultCommandBus(RedeemGameLoginResult result) : ICommandBus
    {
        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull =>
            ValueTask.FromResult((TResult)(object)result);
    }

    private sealed class RecordingCloser : INetworkConnectionCloser
    {
        public List<NetworkConnectionId> Closed { get; } = [];

        public ValueTask RequestCloseExactAsync(NetworkConnectionContext expected, CancellationToken ct) => RequestCloseAsync(expected.ConnectionId, ct);
        public ValueTask RequestCloseAsync(
            NetworkConnectionId connectionId,
            CancellationToken cancellationToken)
        {
            Closed.Add(connectionId);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record RecordedSend(
        NetworkConnectionId ConnectionId,
        IGameServerMessage Message,
        bool IsCmd);

    /// <summary>
    /// Records the whole ordered sequence, not just the last message.
    /// </summary>
    /// <remarks>
    /// It used to keep only the most recent send and throw from <c>SendNotiAsync</c> on the grounds
    /// that "login sends a CMD response". Login now sends a CMD response followed by two NOTIs, and
    /// their order is the point, so the sequence has to be observable.
    /// </remarks>
    private sealed class RecordingSender(int? failAtAttempt = null) : IGameMessageSender
    {
        private int attempts;

        public List<RecordedSend> Sends { get; } = [];

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId,
            IGameCmdMessage message,
            CancellationToken cancellationToken)
        {
            this.ThrowIfConfigured();
            Sends.Add(new RecordedSend(connectionId, message, IsCmd: true));
            return ValueTask.CompletedTask;
        }

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This handler does not use explicit-recipient delivery.");

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId,
            IGameNotiMessage message,
            CancellationToken cancellationToken)
        {
            this.ThrowIfConfigured();
            Sends.Add(new RecordedSend(connectionId, message, IsCmd: false));
            return ValueTask.CompletedTask;
        }

        private void ThrowIfConfigured()
        {
            this.attempts++;
            if (this.attempts == failAtAttempt)
            {
                throw new IOException("send failed");
            }
        }
    }

    private sealed class RecordingLogger : ILogger<GameLoginHandler>
    {
        private readonly List<string> messages = [];

        public string Text => string.Join('\n', this.messages);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            this.messages.Add(formatter(state, exception));
    }

    private sealed class LoginChannelPolicy(byte? environment) : IChannelEntryPolicy
    {
        public ValueTask<ChannelEntryIdentity> ResolveAsync(int listenerPort, CancellationToken cancellationToken)
        {
            Assert.Equal(10011, listenerPort);
            return ValueTask.FromResult(new ChannelEntryIdentity(200, listenerPort, ChannelEntryKind.General, environment));
        }
        public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int characterLevel) => null;
    }

    /// <remarks>
    /// <c>LoadAsync</c> throws on purpose. The real one demands a selectable roster and throws for
    /// an account that owns no character -- which is every account logging in for the first time --
    /// and login reaching for it silently cost the connection. Throwing here keeps that mistake
    /// from being reintroduced quietly.
    /// </remarks>
    private sealed class StubAccountEntryStateDomainService : IAccountEntryStateDomainService
    {
        public AccountId? RequestedAccountId { get; private set; }

        public ValueTask<AccountEntryState> LoadAsync(
            AccountId accountId,
            CancellationToken cancellationToken) =>
            throw new InvalidDataException(
                $"Account {accountId} has no selectable roster state.");

        public ValueTask<AccountSettingsEntryState> LoadSettingsAsync(
            AccountId accountId,
            CancellationToken cancellationToken)
        {
            RequestedAccountId = accountId;
            return ValueTask.FromResult(new AccountSettingsEntryState(
                [0x01], [0x02], [0x03], HotkeyKeyType: 1, AccountHotkeySlot: 0x0042));
        }
    }
}
