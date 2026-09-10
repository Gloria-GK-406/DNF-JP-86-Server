using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Persistence;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.Login;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Authentication;

public sealed class GameLoginCommandTransactionTests
{
    [Fact]
    public async Task CancellationBeforeCommitRollsBackTheRealTicketAndASecondConnectionCanRedeemIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-game-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "game-login.db");
        try
        {
            using var cancellation = new CancellationTokenSource();
            using var services = BuildServices(
                databasePath,
                collection =>
                {
                    collection.AddSingleton(cancellation);
                    collection.AddSingleton<IGameLoginAuthenticationDomainService,
                        CancelOnceGameLoginAuthenticationDomainService>();
                },
                includePersistence: true);
            var commands = services.GetRequiredService<ICommandBus>();
            var onlineUsers = services.GetRequiredService<IOnlineUserDomainService>();
            _ = await commands.SendAsync(new RegisterAccountCommand("player", "test password"));
            var issued = await commands.SendAsync(
                new AuthenticateAndIssueTicketCommand("player", "test password"));
            Assert.NotNull(issued.Ticket);
            var first = Register(onlineUsers, "Game:cancelled-redemption");

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await commands.SendAsync(
                    new RedeemGameLoginCommand(
                        first.OnlineUserId,
                        "player",
                        issued.Ticket),
                    cancellation.Token));

            var anonymous = onlineUsers.GetRequired(first.OnlineUserId);
            Assert.Equal(OnlineUserState.Connected, anonymous.State);
            Assert.Null(anonymous.AccountId);
            Assert.Equal(
                0L,
                await ScalarAsync(
                    databasePath,
                    "SELECT COUNT(*) FROM login_tickets WHERE consumed_at IS NOT NULL;"));

            Assert.True(onlineUsers.TryDisconnectConnection(first.Connection.ConnectionId));
            var second = Register(onlineUsers, "Game:retry-after-cancel");
            var accepted = await commands.SendAsync(
                new RedeemGameLoginCommand(second.OnlineUserId, "player", issued.Ticket),
                CancellationToken.None);

            Assert.True(accepted.Succeeded);
            Assert.Equal(accepted.AccountId, onlineUsers.GetRequired(second.OnlineUserId).AccountId);
            Assert.Equal(
                1L,
                await ScalarAsync(
                    databasePath,
                    "SELECT COUNT(*) FROM login_tickets WHERE consumed_at IS NOT NULL;"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DurableCommitFailureLeavesTheTicketAndIdentityReusableByASecondConnection()
    {
        using var services = BuildServices(
            databasePath: null,
            collection =>
            {
                collection.AddSingleton<CommitFailOnceAuthenticationDomainService>();
                collection.AddSingleton<IAuthenticationDomainService>(provider =>
                    provider.GetRequiredService<CommitFailOnceAuthenticationDomainService>());
            },
            includePersistence: false);
        var authentication = services.GetRequiredService<CommitFailOnceAuthenticationDomainService>();
        var commands = services.GetRequiredService<ICommandBus>();
        var onlineUsers = services.GetRequiredService<IOnlineUserDomainService>();
        var first = Register(onlineUsers, "Game:commit-failed");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await commands.SendAsync(new RedeemGameLoginCommand(
                first.OnlineUserId,
                "player",
                CommitFailOnceAuthenticationDomainService.Ticket)));

        Assert.Equal("database commit failed", failure.Message);
        Assert.True(authentication.Available);
        Assert.Equal(OnlineUserState.Connected, onlineUsers.GetRequired(first.OnlineUserId).State);
        Assert.True(onlineUsers.TryDisconnectConnection(first.Connection.ConnectionId));

        var second = Register(onlineUsers, "Game:retry-after-commit-failure");
        var accepted = await commands.SendAsync(new RedeemGameLoginCommand(
            second.OnlineUserId,
            "player",
            CommitFailOnceAuthenticationDomainService.Ticket));

        Assert.True(accepted.Succeeded);
        Assert.False(authentication.Available);
        Assert.Equal(new AccountId(73), onlineUsers.GetRequired(second.OnlineUserId).AccountId);
    }

    [Fact]
    public async Task TargetLostAfterDurableCommitLeavesTheTicketConsumedAndImpossibleToReplay()
    {
        using var services = BuildServices(
            databasePath: null,
            collection =>
            {
                collection.AddSingleton<CommitThenDisconnectAuthenticationDomainService>();
                collection.AddSingleton<IAuthenticationDomainService>(provider =>
                    provider.GetRequiredService<CommitThenDisconnectAuthenticationDomainService>());
            },
            includePersistence: false);
        var authentication = services.GetRequiredService<CommitThenDisconnectAuthenticationDomainService>();
        var commands = services.GetRequiredService<ICommandBus>();
        var onlineUsers = services.GetRequiredService<IOnlineUserDomainService>();
        var first = Register(onlineUsers, "Game:lost-after-durable-commit");
        authentication.TargetConnectionId = first.Connection.ConnectionId;

        var committed = await commands.SendAsync(new RedeemGameLoginCommand(
            first.OnlineUserId,
            "player",
            CommitThenDisconnectAuthenticationDomainService.Ticket));

        Assert.True(committed.Succeeded);
        Assert.False(authentication.Available);
        Assert.False(onlineUsers.TryGetByConnection(first.Connection.ConnectionId, out _));

        var second = Register(onlineUsers, "Game:replay-after-target-lost");
        var replay = await commands.SendAsync(new RedeemGameLoginCommand(
            second.OnlineUserId,
            "player",
            CommitThenDisconnectAuthenticationDomainService.Ticket));

        Assert.False(replay.Succeeded);
        Assert.Equal(OnlineUserState.Connected, onlineUsers.GetRequired(second.OnlineUserId).State);
    }

    private static ServiceProvider BuildServices(
        string? databasePath,
        Action<IServiceCollection> configure,
        bool includePersistence)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            databasePath is null
                ? []
                : new Dictionary<string, string?> { ["Database:Path"] = databasePath }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        configure(services);
        new ApplicationModule().ConfigureServices(services, configuration);
        if (includePersistence)
        {
            new PersistenceModule().ConfigureServices(services, configuration);
        }
        new ProtocolModule().ConfigureServices(services, configuration);
        return services.BuildServiceProvider();
    }

    private static OnlineUserSnapshot Register(IOnlineUserDomainService onlineUsers, string id) =>
        onlineUsers.RegisterConnection(new NetworkConnectionContext(
            new NetworkConnectionId(id),
            NetworkEndpointKind.Game,
            10011));

    private static async Task<long> ScalarAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed class CancelOnceGameLoginAuthenticationDomainService(
        IAuthenticationDomainService authentication,
        OnlineStateRegistry onlineUsers,
        CancellationTokenSource cancellation) : IGameLoginAuthenticationDomainService
    {
        private int cancelNext = 1;

        public async ValueTask<AccountId?> RedeemTicketAsync(
            OnlineUserId onlineUserId,
            string loginId,
            string ticket,
            CancellationToken cancellationToken)
        {
            var accountId = await authentication.ConsumeTicketAsync(
                loginId,
                ticket,
                cancellationToken);
            if (accountId is null)
            {
                return null;
            }

            onlineUsers.PrepareAuthentication(onlineUserId, accountId.Value);
            if (Interlocked.Exchange(ref this.cancelNext, 0) == 1)
            {
                cancellation.Cancel();
            }
            return accountId;
        }
    }

    private sealed class CommitFailOnceAuthenticationDomainService(
        IDomainTransactionAccessor transactions)
        : IAuthenticationDomainService
    {
        internal const string Ticket = "test-ticket-that-is-exactly-forty-three-chars";
        private bool failNext = true;

        public bool Available { get; private set; } = true;

        public ValueTask<RegistrationResult> RegisterAsync(
            string loginId,
            string password,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<AuthenticationResult> AuthenticateAndIssueTicketAsync(
            string loginId,
            string password,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<AccountId?> ConsumeTicketAsync(
            string loginId,
            string ticket,
            CancellationToken cancellationToken)
        {
            if (!this.Available
                || loginId != "player"
                || ticket != Ticket)
            {
                return ValueTask.FromResult<AccountId?>(null);
            }

            var transaction = transactions.Current
                ?? throw new InvalidOperationException("Test transaction was not supplied.");
            transaction.Enlist(
                this,
                () => new TicketParticipant(this));
            return ValueTask.FromResult<AccountId?>(new AccountId(73));
        }

        private sealed class TicketParticipant(CommitFailOnceAuthenticationDomainService owner)
            : ITransactionParticipant
        {
            public bool CommitCanFail => true;

            public ValueTask PrepareAsync(CancellationToken cancellationToken) =>
                ValueTask.CompletedTask;

            public ValueTask CommitAsync(CancellationToken cancellationToken)
            {
                if (owner.failNext)
                {
                    owner.failNext = false;
                    throw new InvalidOperationException("database commit failed");
                }
                owner.Available = false;
                return ValueTask.CompletedTask;
            }

            public ValueTask RollbackAsync(CancellationToken cancellationToken) =>
                ValueTask.CompletedTask;
        }
    }

    private sealed class CommitThenDisconnectAuthenticationDomainService(
        IDomainTransactionAccessor transactions,
        OnlineStateRegistry onlineUsers) : IAuthenticationDomainService
    {
        internal const string Ticket = "committed-ticket-cannot-be-replayed-00000000";

        public bool Available { get; private set; } = true;

        public NetworkConnectionId TargetConnectionId { get; set; }

        public ValueTask<RegistrationResult> RegisterAsync(
            string loginId,
            string password,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<AuthenticationResult> AuthenticateAndIssueTicketAsync(
            string loginId,
            string password,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<AccountId?> ConsumeTicketAsync(
            string loginId,
            string ticket,
            CancellationToken cancellationToken)
        {
            if (!this.Available || loginId != "player" || ticket != Ticket)
            {
                return ValueTask.FromResult<AccountId?>(null);
            }

            var transaction = transactions.Current
                ?? throw new InvalidOperationException("Test transaction was not supplied.");
            transaction.Enlist(this, () => new TicketParticipant(this, onlineUsers));
            return ValueTask.FromResult<AccountId?>(new AccountId(74));
        }

        private sealed class TicketParticipant(
            CommitThenDisconnectAuthenticationDomainService owner,
            OnlineStateRegistry onlineUsers) : ITransactionParticipant
        {
            public bool CommitCanFail => true;

            public ValueTask PrepareAsync(CancellationToken cancellationToken) =>
                ValueTask.CompletedTask;

            public ValueTask CommitAsync(CancellationToken cancellationToken)
            {
                owner.Available = false;
                _ = onlineUsers.TryDisconnectConnection(owner.TargetConnectionId);
                return ValueTask.CompletedTask;
            }

            public ValueTask RollbackAsync(CancellationToken cancellationToken) =>
                ValueTask.CompletedTask;
        }
    }
}
