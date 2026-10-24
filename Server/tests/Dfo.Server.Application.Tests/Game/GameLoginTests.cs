using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Data.Channel;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Configuration;
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
    /// <summary>The real registration under the handler; what it registered is read back through the old doubles' members.</summary>
    private readonly RecordedOutbound outbound = new();

    [Fact]
    public async Task CommandHandler_DelegatesTicketRedemptionToAuthenticationDomainService()
    {
        var onlineUserId = OnlineUserId.New();
        var authentication = new StubGameLoginAuthenticationDomainService(new AccountId(42));
        var onlineUsers = new UserDomainServices(new AccountId(42));
        var connection = new NetworkConnectionContext(new("delegation"), NetworkEndpointKind.Game, 10011);
        await TestGameplay.RunAsync(async () =>
        {
            var registered = onlineUsers.RegisterConnection(connection);
            var handler = CreateHandler(onlineUsers, new RecordingSender(outbound), authentication,
                new StubAccountEntryStateDomainService(), new RecordingCloser(outbound));
            await handler.HandleAsync(new(connection), new GameLoginRequest(GameMessageMetadata.Empty,
                "123456", "0123456789ABCDEF0123456789ABCDEF"), default);
            Assert.Equal(registered.OnlineUserId, authentication.OnlineUserId);
            Assert.Equal("123456", authentication.MId);
            Assert.Equal("0123456789ABCDEF0123456789ABCDEF", authentication.Ticket);
        });
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
            var sender = new RecordingSender(outbound);
            var bus = new PublishingAuthentication(onlineUsers, new AccountId(7));
            var accountEntryState = new StubAccountEntryStateDomainService();
            var closer = new RecordingCloser(outbound);
            var handler = CreateHandler(
                onlineUsers,
                sender,
                bus,
                accountEntryState,
                closer,
                channels: new LoginChannelDomain(channelEnvironment));

            await handler.HandleAsync(
                new NetworkDispatchContext(connection),
                new GameLoginRequest(
                    GameMessageMetadata.Empty,
                    "123456",
                    "0123456789ABCDEF0123456789ABCDEF"),
                CancellationToken.None);

            Assert.Equal(anonymous.OnlineUserId, bus.OnlineUserId);
            Assert.Equal("123456", bus.MId);
            Assert.Equal("0123456789ABCDEF0123456789ABCDEF", bus.Ticket);

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
            var sender = new RecordingSender(outbound);
            var closer = new RecordingCloser(outbound);
            var handler = CreateHandler(
                onlineUsers,
                sender,
                new ThrowingAuthentication(),
                new StubAccountEntryStateDomainService(),
                closer);

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
            var sender = new RecordingSender(outbound);
            var closer = new RecordingCloser(outbound);
            var logger = new RecordingLogger();
            var handler = CreateHandler(
                onlineUsers,
                sender,
                new StubGameLoginAuthenticationDomainService(null),
                new StubAccountEntryStateDomainService(),
                closer,
                logger: logger);
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
            var sender = new RecordingSender(outbound, failAtAttempt: 2);
            var closer = new RecordingCloser(outbound);
            var handler = CreateHandler(
                onlineUsers,
                sender,
                new PublishingAuthentication(onlineUsers, new AccountId(7)),
                new StubAccountEntryStateDomainService(),
                closer);

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
            var sender = new RecordingSender(outbound);
            var closer = new RecordingCloser(outbound);
            var handler = CreateHandler(
                onlineUsers,
                sender,
                new StubGameLoginAuthenticationDomainService(new AccountId(7)),
                new StubAccountEntryStateDomainService(),
                closer);

            await handler.HandleAsync(
                new NetworkDispatchContext(connection),
                new GameLoginRequest(GameMessageMetadata.Empty, "player", new string('t', 43)),
                CancellationToken.None);

            Assert.Empty(sender.Sends);
            Assert.False(onlineUsers.TryGetByConnection(connection.ConnectionId, out _));
            Assert.Equal([connection.ConnectionId], closer.Closed);
        });
    }

    private sealed class StubGameLoginAuthenticationDomainService(AccountId? accountId)
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

    private sealed class PublishingAuthentication(UserDomainServices onlineUsers, AccountId accountId)
        : IGameLoginAuthenticationDomainService
    {
        public OnlineUserId OnlineUserId { get; private set; }
        public string? MId { get; private set; }
        public string? Ticket { get; private set; }
        public async ValueTask<AccountId?> AuthenticateSessionAsync(OnlineUserId id, string mId, string ticket, CancellationToken ct)
        {
            OnlineUserId = id; MId = mId; Ticket = ticket;
            await onlineUsers.AuthenticateForTestAsync(id);
            return accountId;
        }
    }

    private sealed class ThrowingAuthentication : IGameLoginAuthenticationDomainService
    {
        public ValueTask<AccountId?> AuthenticateSessionAsync(OnlineUserId id, string mId, string ticket, CancellationToken ct) =>
            ValueTask.FromException<AccountId?>(new InvalidOperationException("transaction failed"));
    }

    // These unit drivers retain the original observable handler assertions. Real transaction/identity
    // rollback and ticket reuse are separately exercised through the real bus and TCP integration.
    internal static GameLoginHandler CreateHandler(IOnlineUserDomainService users, IGameMessageSender sender,
        IGameLoginAuthenticationDomainService authentication, IAccountEntryStateDomainService settings,
        INetworkConnectionCloser closer, ILogger<GameLoginCommandHandler>? logger = null,
        IChannelEntryDomainService? channels = null) => new(new DirectLoginBus(new GameLoginCommandHandler(
            users, authentication, settings, channels ?? new LoginChannelDomain(null), new Settings(),
            new DirectWorkUnits(), new Outbox(sender), new Closures(closer),
            logger ?? NullLogger<GameLoginCommandHandler>.Instance), sender));

    private sealed class DirectLoginBus(GameLoginCommandHandler handler, IGameMessageSender sender) : ICommandBus
    {
        public async ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            var login = Assert.IsType<GameLoginCommand>(command);
            if (sender is RecordingSender recording) recording.RegisterConnection(login.Dispatch.Connection);
            return (TResult)(object)await handler.HandleAsync(new CommandExecutionContext(Guid.NewGuid(),
                command.GetType(), handler.GetType(), CommandExecutionKind.Direct), login, cancellationToken);
        }
    }
    private sealed class DirectWorkUnits : ICommandWorkUnits
    {
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
            Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
            Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull
        {
            Assert.Equal(CommandWorkUnitKind.Ordinary, declaration.Kind);
            Assert.Empty(declaration.Participants);
            return action(null!, ct);
        }
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d,
            Func<CancellationToken,ValueTask<T>> action, CancellationToken ct=default) where T:notnull => throw new NotSupportedException();
    }
    private sealed class Outbox(IGameMessageSender sender) : IGameCommandOutbox
    {
        public ValueTask RegisterCmdAsync(NetworkConnectionId id, IGameCmdMessage m, CancellationToken ct) => sender.SendCmdAsync(id,m,ct);
        public ValueTask RegisterNotiAsync(NetworkConnectionId id, IGameNotiMessage m, CancellationToken ct) => sender.SendNotiAsync(id,m,ct);
        public ValueTask RegisterBatchAsync(NetworkConnectionContext c,IReadOnlyList<IGameServerMessage> m,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask RegisterExplicitAsync(NetworkConnectionId id,GameExplicitMessageDelivery d,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Closures(INetworkConnectionCloser closer) : ICommandConnectionClosures
    {
        public ValueTask RequestCloseAsync(NetworkConnectionContext c,CancellationToken ct) => closer.RequestCloseExactAsync(c,ct);
    }
    private sealed class Settings : IClientSettingsDomainService
    {
        public bool ActivateHotkeyProfile(ClientSettingsOrigin origin,byte keyType)=>true;
        public ClientHotkeyUploadContext? CaptureHotkeyUpload(ClientSettingsOrigin origin)=>throw new NotSupportedException();
        public void ForgetConnection(NetworkConnectionId id)=>throw new NotSupportedException();
        public void ForgetConnection(GameSessionIdentity id)=>throw new NotSupportedException();
        public ValueTask<ClientAppearanceOptions> ReadAppearanceAsync(AccountId id,byte b,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<bool> SaveMainAsync(ClientSettingsOrigin o,byte[] p,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<bool> SaveQuickChatAsync(ClientSettingsOrigin o,byte b,byte[] p,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<bool> SaveHotkeysAsync(ClientHotkeyUploadContext o,byte[] p,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<IReadOnlyList<HotkeyProfileState>> ReadHotkeysAsync(AccountId id,CancellationToken ct)=>throw new NotSupportedException();
    }

    /// <summary>The closes the handler registered, read from the outbox (TASK-25: no longer a recording double).</summary>
    private sealed class RecordingCloser(RecordedOutbound outbound) : INetworkConnectionCloser
    {
        public List<NetworkConnectionId> Closed => [.. outbound.Closes];

        public ValueTask RequestCloseExactAsync(NetworkConnectionContext expected, CancellationToken ct) =>
            outbound.Closer.RequestCloseExactAsync(expected, ct);

        public ValueTask RequestCloseAsync(NetworkConnectionId connectionId, CancellationToken cancellationToken) =>
            outbound.Closer.RequestCloseAsync(connectionId, cancellationToken);
    }

    private sealed record RecordedSend(
        NetworkConnectionId ConnectionId,
        IGameServerMessage Message,
        bool IsCmd);

    /// <summary>
    /// The whole ordered sequence the handler registered, read from the outbox; every call goes to the real protocol
    /// sender over the registration adapter.
    /// </summary>
    /// <remarks>
    /// <paramref name="failAtAttempt"/> makes that send's message fail to encode where it is registered (TASK-25 §6.1),
    /// with the exception the old double threw; an explicit delivery fails there too, as the old double refused one.
    /// </remarks>
    private sealed class RecordingSender : IGameMessageSender
    {
        private readonly RecordedOutbound outbound;
        private readonly int? failAtAttempt;
        private int attempts;
        private bool failing;

        public RecordingSender(RecordedOutbound outbound, int? failAtAttempt = null)
        {
            this.outbound = outbound;
            this.failAtAttempt = failAtAttempt;
            outbound.FailAny(_ => failing, () => new IOException("send failed"));
            outbound.RefuseExplicit(() => new InvalidOperationException("This handler does not use explicit-recipient delivery."));
        }

        public void RegisterConnection(NetworkConnectionContext connection) => outbound.Connections.Add(connection);

        public List<RecordedSend> Sends =>
        [
            .. outbound.Entries.Where(entry => !entry.IsClose).SelectMany(entry => entry.Messages.Select(message =>
                new RecordedSend(entry.Connection.ConnectionId, (IGameServerMessage)message, message is IGameCmdMessage))),
        ];

        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) =>
            AttemptAsync(() => outbound.Game.SendCmdAsync(connectionId, message, cancellationToken));

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken) =>
            outbound.Game.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);

        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) =>
            AttemptAsync(() => outbound.Game.SendNotiAsync(connectionId, message, cancellationToken));

        private async ValueTask AttemptAsync(Func<ValueTask> send)
        {
            failing = ++attempts == failAtAttempt;
            try
            {
                await send();
            }
            finally
            {
                failing = false;
            }
        }
    }

    private sealed class RecordingLogger : ILogger<GameLoginCommandHandler>
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

    private sealed class LoginChannelDomain(byte? environment) : IChannelEntryDomainService
    {
        public ValueTask<ChannelLoginEntry> ResolveLoginAsync(int listenerPort, CancellationToken ct)
        {
            Assert.Equal(10011, listenerPort);
            return ValueTask.FromResult(new ChannelLoginEntry(environment ?? 1, new PublicGameLoginEndpoints("192.0.2.20",10128,17200,17200)));
        }
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
