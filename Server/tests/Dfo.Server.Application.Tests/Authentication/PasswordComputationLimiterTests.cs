using System.Reflection;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Execution;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Authentication;

/// <summary>RQ-007 / AC-005: password hashing runs off the executor through one bounded queue.</summary>
public sealed class PasswordComputationLimiterTests
{
    /// <summary>H2: beyond the limit callers queue instead of failing, and the executor stays free meanwhile.</summary>
    [Fact]
    public async Task Hashing_beyond_the_limit_queues_without_failing_and_never_occupies_the_executor()
    {
        using var passwords = new BlockingPasswords();
        var reader = new LauncherCredentialReader(new OneAccount(), passwords,
            AuthenticationRuleService.From(new ConfigurationBuilder().Build()), new PasswordComputationLimiter(2));
        var logins = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(() => reader.VerifyLoginAsync("player", "player password", default).AsTask()))
            .ToArray();

        await passwords.TwoInFlight.Task.WaitAsync(ExecutionTest.Patience);
        await Task.Delay(200);
        Assert.Equal(2, passwords.Peak);
        Assert.All(logins, login => Assert.False(login.IsCompleted));
        var (executor, _) = ExecutionTest.Executor();
        await using (executor)
            await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask, "test:marker"), default)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(1));

        passwords.Release.Set();
        var results = await Task.WhenAll(logins).WaitAsync(ExecutionTest.Patience);
        Assert.All(results, result =>
        {
            Assert.Equal(AuthenticationStatus.Success, result.Status);
            Assert.NotNull(result.Login);
        });
        Assert.Equal(2, passwords.Peak);
        Assert.Equal(5, passwords.Verifications);
    }

    /// <summary>H3: calling the limiter from a job is a program error that sticks to the job (AC-005's criterion).</summary>
    [Fact]
    public async Task Hashing_inside_a_job_fails_at_once_as_a_program_error()
    {
        var limiter = new PasswordComputationLimiter(1);
        var (executor, _) = ExecutionTest.Executor();
        await using var running = executor;
        var hashed = false;

        var job = executor.RunAsync(ExecutionTest.Work(async ct =>
        {
            try
            {
                await limiter.RunAsync(() => hashed = true, ct);
            }
            catch (GameplaySelfWaitException)
            {
                // Swallowed on purpose: the job must fail anyway.
            }
        }, "test:hash-in-job"), default).AsTask();

        await Assert.ThrowsAsync<GameplaySelfWaitException>(() => job.WaitAsync(ExecutionTest.Patience));
        Assert.False(hashed);
    }

    /// <summary>H4: one limiter per process, as wide as the machine, shared by both credential readers.</summary>
    [Fact]
    public void The_default_limiter_has_one_slot_per_processor_and_both_readers_share_it()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationPersistence>(new OneAccount());
        services.AddSingleton<IGmAdministrationPersistence>(new NoAdministrators());
        new ApplicationModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var limiter = provider.GetRequiredService<PasswordComputationLimiter>();
        Assert.Equal(Environment.ProcessorCount, limiter.Capacity);
        Assert.Same(limiter, LimiterOf(provider.GetRequiredService<ILauncherCredentialReader>()));
        Assert.Same(limiter, LimiterOf(provider.GetRequiredService<IGmCredentialReader>()));
    }

    /// <summary>H5: no domain service or command handler can reach the hasher or the limiter.</summary>
    [Fact]
    public void Domain_services_and_command_handlers_take_neither_the_hasher_nor_the_limiter()
    {
        var candidates = typeof(RegisterAccountCommand).Assembly.GetTypes().Where(IsDomainServiceOrCommandHandler).ToArray();

        Assert.Contains(candidates, type => type.Name == "AuthenticationDomainService");
        Assert.Contains(candidates, type => type.Name == "GmAccessDomainService");
        Assert.Empty(Violations(candidates));
    }

    /// <summary>H5's counter-example: the rule refuses a domain service that takes the hasher (RQ-072).</summary>
    [Fact]
    public void A_domain_service_that_takes_the_hasher_is_refused()
    {
        Assert.True(IsDomainServiceOrCommandHandler(typeof(HashingDomainService)));
        Assert.Equal([nameof(HashingDomainService)], Violations([typeof(HashingDomainService)]));
    }

    /// <summary>TASK-30 section 7.3: the other two branches of H5, a command handler and the limiter.</summary>
    [Fact]
    public void A_command_handler_that_takes_the_limiter_is_refused()
    {
        Assert.True(IsDomainServiceOrCommandHandler(typeof(LimitingCommandHandler)));
        Assert.Equal([nameof(LimitingCommandHandler)], Violations([typeof(LimitingCommandHandler)]));
    }

    private static bool IsDomainServiceOrCommandHandler(Type type) =>
        type is { IsClass: true, IsAbstract: false }
        && type.GetInterfaces().Any(contract =>
            contract.Name.EndsWith("DomainService", StringComparison.Ordinal)
            || (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)));

    private static string[] Violations(IEnumerable<Type> types) => types
        .Where(type => type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Any(parameter => parameter.ParameterType == typeof(IPasswordCredentialRuleService)
                || parameter.ParameterType == typeof(PasswordComputationLimiter)))
        .Select(type => type.Name)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static object? LimiterOf(object reader) => reader.GetType()
        .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(field => field.FieldType == typeof(PasswordComputationLimiter))
        .GetValue(reader);

    public interface IHashingProbeDomainService;

    private sealed class HashingDomainService(IPasswordCredentialRuleService passwords) : IHashingProbeDomainService
    {
        public string Hash(string password) => passwords.Hash(password);
    }

    private sealed record LimitingCommand : ICommand<bool>;

    private sealed class LimitingCommandHandler(PasswordComputationLimiter limiter) : ICommandHandler<LimitingCommand, bool>
    {
        public ValueTask<bool> HandleAsync(CommandExecutionContext context, LimitingCommand command, CancellationToken cancellationToken) =>
            ValueTask.FromResult(limiter.Capacity > 0);
    }

    private sealed class BlockingPasswords : IPasswordCredentialRuleService, IDisposable
    {
        private int inFlight;
        private int peak;
        private int verifications;

        public ManualResetEventSlim Release { get; } = new();
        public TaskCompletionSource TwoInFlight { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Peak => Volatile.Read(ref peak);
        public int Verifications => Volatile.Read(ref verifications);

        public string Hash(string password) => "record";

        public void VerifyDummy(string password) => throw new InvalidOperationException("The account exists.");

        public PasswordCheck Verify(string record, string password)
        {
            var now = Interlocked.Increment(ref inFlight);
            int seen;
            while ((seen = Volatile.Read(ref peak)) < now && Interlocked.CompareExchange(ref peak, now, seen) != seen) { }
            if (now >= 2) TwoInFlight.TrySetResult();
            try
            {
                if (!Release.Wait(ExecutionTest.Patience)) throw new TimeoutException("The test never released the hashes.");
                Interlocked.Increment(ref verifications);
                return PasswordCheck.Verified;
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        }

        public void Dispose() => Release.Dispose();
    }

    private sealed class OneAccount : IAuthenticationPersistence
    {
        public ValueTask AcquireWriteIntentAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<AccountRecord?> FindAsync(string loginId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<AccountRecord?>(new(new AccountId(7), loginId, "record", DateTimeOffset.UnixEpoch));

        public ValueTask<AccountId?> TryCreateAsync(string loginId, string passwordRecord, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<bool> TryReplaceCredentialAsync(AccountId accountId, string expected, string replacement,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoAdministrators : IGmAdministrationPersistence
    {
        public ValueTask AcquireWriteIntentAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<bool> HasAdministratorAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);
        public ValueTask<GmAdministrator?> FindAdministratorAsync(string loginName, CancellationToken cancellationToken) =>
            ValueTask.FromResult<GmAdministrator?>(null);
        public ValueTask<GmAdministrator?> FindAdministratorAsync(long id, CancellationToken cancellationToken) =>
            ValueTask.FromResult<GmAdministrator?>(null);
        public ValueTask CreateAdministratorAsync(string loginName, string passwordRecord, GmPermission permissions,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask SaveSessionAsync(GmSession session, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<GmSession?> FindSessionAsync(string hash, CancellationToken cancellationToken) =>
            ValueTask.FromResult<GmSession?>(null);
        public ValueTask DeleteSessionAsync(string hash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask AppendAuditAsync(GmAuditRecord record, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<GmAuditPage> QueryAuditAsync(int page, int pageSize, GmAuditFilter filter,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
