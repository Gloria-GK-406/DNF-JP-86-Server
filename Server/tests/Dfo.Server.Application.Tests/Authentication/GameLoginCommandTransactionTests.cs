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
using Dfo.Server.Testing;

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
                        issued.MId!,
                        issued.Ticket),
                    cancellation.Token));

            TestGameplay.Run(services, () =>
            {
                var anonymous = onlineUsers.GetRequired(first.OnlineUserId);
                Assert.Equal(OnlineUserState.Connected, anonymous.State);
                Assert.Null(anonymous.AccountId);
                Assert.True(onlineUsers.TryDisconnectConnection(first.Connection.ConnectionId));
            });
            var second = Register(onlineUsers, "Game:retry-after-cancel");
            var accepted = await commands.SendAsync(
                new RedeemGameLoginCommand(second.OnlineUserId, issued.MId!, issued.Ticket),
                CancellationToken.None);

            Assert.True(accepted.Succeeded);
            TestGameplay.Run(services, () =>
            {
                Assert.Equal(accepted.AccountId, onlineUsers.GetRequired(second.OnlineUserId).AccountId);
            });
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
        TestGameplay.Run(services, () =>
        {
            Assert.Equal(OnlineUserState.Connected, onlineUsers.GetRequired(first.OnlineUserId).State);
            Assert.True(onlineUsers.TryDisconnectConnection(first.Connection.ConnectionId));
        });

        var second = Register(onlineUsers, "Game:retry-after-commit-failure");
        var accepted = await commands.SendAsync(new RedeemGameLoginCommand(
            second.OnlineUserId,
            "player",
            CommitFailOnceAuthenticationDomainService.Ticket));

        Assert.True(accepted.Succeeded);
        Assert.False(authentication.Available);
        TestGameplay.Run(services, () =>
        {
            Assert.Equal(new AccountId(73), onlineUsers.GetRequired(second.OnlineUserId).AccountId);
        });
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
        TestGameplay.Run(() =>
        onlineUsers.RegisterConnection(new NetworkConnectionContext(
            new NetworkConnectionId(id),
            NetworkEndpointKind.Game,
            10011)));

    private sealed class CancelOnceGameLoginAuthenticationDomainService(
        IAuthenticationDomainService authentication,
        OnlineStateRegistry onlineUsers,
        CancellationTokenSource cancellation) : IGameLoginAuthenticationDomainService
    {
        private int cancelNext = 1;

        public async ValueTask<AccountId?> AuthenticateSessionAsync(
            OnlineUserId onlineUserId,
            string loginId,
            string ticket,
            CancellationToken cancellationToken)
        {
            var accountId = await authentication.BindSessionAsync(
                onlineUsers.GetRequiredUser(onlineUserId).Connection.ConnectionId,
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

        public ValueTask<AccountId?> BindSessionAsync(
            NetworkConnectionId connectionId,
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

}
