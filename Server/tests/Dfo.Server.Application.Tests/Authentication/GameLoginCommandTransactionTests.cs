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
            _ = await TestCredentials.RegisterAsync(services, "player", "test password");
            var issued = await TestCredentials.LoginAsync(services, "player", "test password");
            Assert.NotNull(issued.Ticket);
            var first = Register(onlineUsers, "Game:cancelled-redemption");

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await commands.SendInternalAsync(
                    new RedemptionProbe(
                        first.OnlineUserId,
                        issued.MId!,
                        issued.Ticket),
                    Origin(), cancellation.Token));

            TestGameplay.Run(services, () =>
            {
                var anonymous = onlineUsers.GetRequired(first.OnlineUserId);
                Assert.Equal(OnlineUserState.Connected, anonymous.State);
                Assert.Null(anonymous.AccountId);
                Assert.True(onlineUsers.TryDisconnectConnection(first.Connection.ConnectionId));
            });
            var second = Register(onlineUsers, "Game:retry-after-cancel");
            var accepted = await commands.SendInternalAsync(
                new RedemptionProbe(second.OnlineUserId, issued.MId!, issued.Ticket),
                Origin(), CancellationToken.None);

            Assert.True(accepted.Succeeded);
            TestGameplay.Run(services, () =>
            {
                Assert.Equal(accepted.AccountId, onlineUsers.GetRequired(second.OnlineUserId).AccountId);
            });
        }
        finally
        {
            SqliteTestPools.ClearOwned(databasePath);
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
            await commands.SendInternalAsync(new RedemptionProbe(
                first.OnlineUserId,
                "player",
                CommitFailOnceAuthenticationDomainService.Ticket), Origin()));

        Assert.Equal("database commit failed", failure.Message);
        Assert.True(authentication.Available);
        TestGameplay.Run(services, () =>
        {
            Assert.Equal(OnlineUserState.Connected, onlineUsers.GetRequired(first.OnlineUserId).State);
            Assert.True(onlineUsers.TryDisconnectConnection(first.Connection.ConnectionId));
        });

        var second = Register(onlineUsers, "Game:retry-after-commit-failure");
        var accepted = await commands.SendInternalAsync(new RedemptionProbe(
            second.OnlineUserId,
            "player",
            CommitFailOnceAuthenticationDomainService.Ticket), Origin());

        Assert.True(accepted.Succeeded);
        Assert.False(authentication.Available);
        TestGameplay.Run(services, () =>
        {
            Assert.Equal(new AccountId(73), onlineUsers.GetRequired(second.OnlineUserId).AccountId);
        });
    }

    private static InternalCommandOrigin Origin() => new(Dfo.Server.Abstractions.Execution.GameplayCallSource.StartupRecovery,
        Dfo.Server.Abstractions.Execution.GameplayCallChain.NewRoot(Dfo.Server.Abstractions.Execution.GameplayCallSource.StartupRecovery), "test-game-login-redemption");

    public sealed record RedemptionProbe(OnlineUserId OnlineUserId, string MId, string Ticket) : ICommand<RedemptionResult>;
    public readonly record struct RedemptionResult(AccountId? AccountId) { public bool Succeeded => AccountId is not null; }
    public sealed class RedemptionProbeHandler(IGameLoginAuthenticationDomainService authentication, ICommandWorkUnits units)
        : ICommandHandler<RedemptionProbe, RedemptionResult>
    {
        public ValueTask<RedemptionResult> HandleAsync(CommandExecutionContext context, RedemptionProbe command, CancellationToken ct) =>
            units.WriteAsync(context, new("test-game-login-redemption", []),
                async (_, token) => new RedemptionResult(await authentication.AuthenticateSessionAsync(
                    command.OnlineUserId, command.MId, command.Ticket, token)), ct);
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
        services.AddInternalCommandHandler<RedemptionProbe, RedemptionResult, RedemptionProbeHandler>();
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
            PreparedRegistration registration,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<AuthenticationResult> AuthenticateAndIssueTicketAsync(
            VerifiedLogin login,
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
