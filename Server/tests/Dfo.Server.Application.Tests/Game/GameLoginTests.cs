using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Game;
using Dfo.Server.Application.Game;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Application.Tests.Game;

public sealed class GameLoginTests
{
    [Fact]
    public async Task CommandHandler_ResolvesAccountAndAuthenticatesIndexedOnlineUser()
    {
        var onlineUsers = new OnlineUserService();
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:login-command"),
            NetworkEndpointKind.Game,
            10011);
        var anonymous = onlineUsers.RegisterConnection(connection);
        var repository = new StubAccountRepository(
            new AccountRecord(new AccountId(42), "10038", "hash", DateTimeOffset.UtcNow));
        var handler = new LoginGameUserCommandHandler(repository, onlineUsers);
        var command = new LoginGameUserCommand(anonymous.OnlineUserId, "10038", "hash");
        var context = new CommandExecutionContext(
            Guid.NewGuid(),
            command.CommandType,
            handler.GetType(),
            CommandExecutionKind.Direct,
            OnlineUserAccessMode.Exclusive);

        var result = await handler.HandleAsync(context, command, CancellationToken.None);

        var typed = Assert.IsType<LoginGameUserResult>(result);
        Assert.Equal(new AccountId(42), typed.AccountId);
        Assert.Equal("10038", repository.LoginId);
        Assert.Equal("hash", repository.PasswordHash);
        var authenticated = onlineUsers.GetRequired(anonymous.OnlineUserId);
        Assert.Equal(new AccountId(42), authenticated.AccountId);
        Assert.Equal(OnlineUserState.Authenticated, authenticated.State);
    }

    [Fact]
    public async Task MessageHandler_ResolvesConnectionAndSendsSuccessToSameConnection()
    {
        var onlineUsers = new OnlineUserService();
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:login-message"),
            NetworkEndpointKind.Game,
            10011);
        var anonymous = onlineUsers.RegisterConnection(connection);
        var sender = new RecordingSender();
        var bus = new RecordingCommandBus(new LoginGameUserResult(
            anonymous.OnlineUserId,
            new AccountId(7)));
        var handler = new GameLoginHandler(onlineUsers, sender, bus);

        await handler.HandleAsync(
            new NetworkDispatchContext(connection),
            new GameLoginRequest(GameMessageMetadata.Empty, "login", "password-hash"),
            CancellationToken.None);

        var command = Assert.IsType<LoginGameUserCommand>(bus.Command);
        Assert.Equal(anonymous.OnlineUserId, command.OnlineUserId);
        Assert.Equal("login", command.LoginId);
        Assert.Equal("password-hash", command.PasswordHash);
        Assert.Equal(connection.ConnectionId, sender.ConnectionId);
        Assert.Equal(new GameLoginResponse(true), sender.Message);
    }

    private sealed class StubAccountRepository(AccountRecord account) : IAccountRepository
    {
        public string? LoginId { get; private set; }

        public string? PasswordHash { get; private set; }

        public ValueTask<AccountRecord> GetOrCreateAsync(
            string loginId,
            string passwordHash,
            CancellationToken cancellationToken)
        {
            LoginId = loginId;
            PasswordHash = passwordHash;
            return ValueTask.FromResult(account);
        }
    }

    private sealed class RecordingCommandBus(LoginGameUserResult result) : ICommandBus
    {
        public ICommand? Command { get; private set; }

        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
        {
            Command = command;
            return ValueTask.FromResult((TResult)(object)result);
        }
    }

    private sealed class RecordingSender : INetworkMessageSender
    {
        public NetworkConnectionId ConnectionId { get; private set; }

        public IServerNetworkMessage? Message { get; private set; }

        public ValueTask SendAsync(
            NetworkConnectionId connectionId,
            IServerNetworkMessage message,
            CancellationToken cancellationToken)
        {
            ConnectionId = connectionId;
            Message = message;
            return ValueTask.CompletedTask;
        }
    }
}
