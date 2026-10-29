using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Application;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Dfo.Server.Testing;

namespace Dfo.Server.Persistence.Tests.Accounts;

internal sealed class AuthenticationFixture : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), $"dfo-auth-{Guid.NewGuid():N}");
    public string DatabasePath => Path.Combine(DirectoryPath, "auth.db");
    public FrozenClock Clock { get; } = new();
    public ServiceProvider Services { get; }
    public CapturedLogs Logs { get; } = new();
    public ICommandBus Bus => Services.GetRequiredService<ICommandBus>();
    public IAuthenticationPersistence Store => Services.GetRequiredService<IAuthenticationPersistence>();
    public DomainTransactionScope Scope => Services.GetRequiredService<DomainTransactionScope>();

    public AuthenticationFixture(IPasswordCredentialRuleService? passwords = null, Dictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configure = null)
    {
        Directory.CreateDirectory(DirectoryPath);
        settings ??= [];
        settings["Database:Path"] = DatabasePath;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(Logs));
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IPasswordCredentialRuleService>(passwords ?? new PasswordCredentialService(100));
        new ApplicationModule().ConfigureServices(services, configuration);
        new PersistenceModule().ConfigureServices(services, configuration);
        // Without the content sources the real initialization cannot be built; registration here applies the defaults,
        // which grant nothing.
        services.AddSingleton<Dfo.Server.Abstractions.Initialization.IInitializationDomainService>(new DefaultInitialization());
        configure?.Invoke(services);
        Services = services.BuildServiceProvider();
        _ = Store;
    }

    public Task<Dfo.Server.Data.Authentication.RegistrationResult> RegisterAsync(string loginId, string password) =>
        TestCredentials.RegisterDomainAsync(Services, loginId, password);

    public Task<Dfo.Server.Data.Authentication.AuthenticationResult> LoginAsync(string loginId, string password) =>
        TestCredentials.LoginDomainAsync(Services, loginId, password);

    public async Task<object?> Scalar(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    public async Task Seed(string loginId, string record)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO accounts(m_id,password_hash) VALUES(@login,@record);";
        command.Parameters.AddWithValue("@login", loginId);
        command.Parameters.AddWithValue("@record", record);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<Dfo.Server.Data.Accounts.AccountId?> Consume(string mId, string ticket, Dfo.Server.Data.Networking.NetworkConnectionId? connectionId = null)
    {
        return await TestGameplay.RunAsync(Services, async () =>
        {
            await using var lease = Scope.Begin();
            var result = await Services.GetRequiredService<IAuthenticationDomainService>().BindSessionAsync(connectionId ?? new Dfo.Server.Data.Networking.NetworkConnectionId(Guid.NewGuid().ToString()), mId, ticket, CancellationToken.None);
            await lease.CommitAsync(CancellationToken.None);
            return result;
        });
    }

    public void Dispose()
    {
        Services.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(DirectoryPath, true);
    }

    internal sealed class FrozenClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-09-10T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class CapturedLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> messages = new();
        public string Text => string.Join('\n', messages);
        public ILogger CreateLogger(string categoryName) => new Sink(messages);
        public void Dispose() { }
        private sealed class Sink(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception) + exception);
        }
    }

    private sealed class DefaultInitialization : Dfo.Server.Abstractions.Initialization.IInitializationDomainService
    {
        public ValueTask<Dfo.Server.Data.Initialization.InitializationDocument<Dfo.Server.Data.Initialization.AccountInitialization>> AccountAsync(CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<Dfo.Server.Data.Initialization.InitializationDocument<Dfo.Server.Data.Initialization.CharacterInitialization>> CharacterAsync(CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<Dfo.Server.Data.Initialization.InitializationOptions> OptionsAsync(CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<Dfo.Server.Data.Initialization.InitializationDocument<Dfo.Server.Data.Initialization.AccountInitialization>> SaveAsync(Dfo.Server.Data.Initialization.SaveInitializationRequest<Dfo.Server.Data.Initialization.AccountInitialization> request, string actor, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<Dfo.Server.Data.Initialization.InitializationDocument<Dfo.Server.Data.Initialization.CharacterInitialization>> SaveAsync(Dfo.Server.Data.Initialization.SaveInitializationRequest<Dfo.Server.Data.Initialization.CharacterInitialization> request, string actor, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask ApplyAccountAsync(Dfo.Server.Data.Accounts.AccountId account, CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask ApplyCharacterAsync(Dfo.Server.Data.Characters.CharacterId character, CancellationToken ct) => ValueTask.CompletedTask;
    }
}
