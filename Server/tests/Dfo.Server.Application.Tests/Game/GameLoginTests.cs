using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Protocol.Game.Login;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Application.Tests.Support;

namespace Dfo.Server.Application.Tests.Game;

public sealed class GameLoginTests
{
    [Fact]
    public async Task CommandHandler_DelegatesLoginToAccountService()
    {
        var onlineUserId = OnlineUserId.New();
        var accounts = new StubAccountService(new AccountId(42));
        var handler = new LoginGameUserCommandHandler(accounts);
        var command = new LoginGameUserCommand(onlineUserId, "10038", "hash");
        var context = new CommandExecutionContext(
            Guid.NewGuid(),
            command.GetType(),
            handler.GetType(),
            CommandExecutionKind.Direct,
            UserAccessMode.Exclusive);

        var result = await handler.HandleAsync(context, command, CancellationToken.None);

        var typed = Assert.IsType<LoginGameUserResult>(result);
        Assert.Equal(new AccountId(42), typed.AccountId);
        Assert.Equal(onlineUserId, accounts.OnlineUserId);
        Assert.Equal("10038", accounts.LoginId);
        Assert.Equal("hash", accounts.PasswordHash);
    }

    [Fact]
    public async Task MessageHandler_ResolvesConnectionAndSendsSuccessToSameConnection()
    {
        var repository = new FixedAccountRepository(new AccountId(7));
        var onlineUsers = new UserService(() => repository);
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:login-message"),
            NetworkEndpointKind.Game,
            10011);
        var anonymous = onlineUsers.RegisterConnection(connection);
        var sender = new RecordingSender();
        var bus = new RecordingCommandBus(new LoginGameUserResult(new AccountId(7)));
        var accountEntryState = new StubAccountEntryStateService();
        var handler = new GameLoginHandler(
            onlineUsers, sender, bus, new StubServerConfigService(), accountEntryState);

        await handler.HandleAsync(
            new NetworkDispatchContext(connection),
            new GameLoginRequest(GameMessageMetadata.Empty, "login", "password-hash"),
            CancellationToken.None);

        var command = Assert.IsType<LoginGameUserCommand>(bus.Command);
        Assert.Equal(anonymous.OnlineUserId, command.OnlineUserId);
        Assert.Equal("login", command.LoginId);
        Assert.Equal("password-hash", command.PasswordHash);

        // The login response followed by the whole account-state batch, all to the connection the
        // request arrived on. The batch is asserted as a unit because the client treats it as one:
        // sending part of it left the roster screen unable to open character creation, and which
        // member is actually required has never been isolated.
        Assert.Equal(7, sender.Sends.Count);
        Assert.All(sender.Sends, send => Assert.Equal(connection.ConnectionId, send.ConnectionId));

        Assert.True(sender.Sends[0].IsCmd);
        Assert.Equal(
            new GameLoginResponse(true, 1, 10128, "192.0.2.20", 17200, 17200),
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
    }

    private sealed class StubAccountService(AccountId accountId) : IAccountService
    {
        public OnlineUserId OnlineUserId { get; private set; }

        public string? LoginId { get; private set; }

        public string? PasswordHash { get; private set; }

        public ValueTask<AccountId> LoginAsync(
            OnlineUserId onlineUserId,
            string loginId,
            string passwordHash,
            CancellationToken cancellationToken)
        {
            OnlineUserId = onlineUserId;
            LoginId = loginId;
            PasswordHash = passwordHash;
            return ValueTask.FromResult(accountId);
        }
    }

    private sealed class FixedAccountRepository(AccountId accountId) : IAccountRepository
    {
        public ValueTask<AccountRecord> GetOrCreateAsync(
            string loginId,
            string passwordHash,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AccountRecord(
                accountId,
                loginId,
                passwordHash,
                DateTimeOffset.UtcNow));
    }

    private sealed class RecordingCommandBus(LoginGameUserResult result) : ICommandBus
    {
        public ICommand? Command { get; private set; }

        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            Command = command;
            return ValueTask.FromResult((TResult)(object)result);
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
    private sealed class RecordingSender : IGameMessageSender
    {
        public List<RecordedSend> Sends { get; } = [];

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId,
            IGameCmdMessage message,
            CancellationToken cancellationToken)
        {
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
            Sends.Add(new RecordedSend(connectionId, message, IsCmd: false));
            return ValueTask.CompletedTask;
        }
    }

    /// <remarks>
    /// <c>LoadAsync</c> throws on purpose. The real one demands a selectable roster and throws for
    /// an account that owns no character -- which is every account logging in for the first time --
    /// and login reaching for it silently cost the connection. Throwing here keeps that mistake
    /// from being reintroduced quietly.
    /// </remarks>
    private sealed class StubAccountEntryStateService : IAccountEntryStateService
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
