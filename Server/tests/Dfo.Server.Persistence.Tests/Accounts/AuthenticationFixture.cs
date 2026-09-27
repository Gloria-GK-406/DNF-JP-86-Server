using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

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
        configure?.Invoke(services);
        Services = services.BuildServiceProvider();
        _ = Store;
    }

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
        await using var lease = Scope.Begin();
        await Services.GetRequiredService<IAccountTable>().InsertIfAbsentAsync(loginId, record, CancellationToken.None);
        await lease.CommitAsync(CancellationToken.None);
    }

    public async Task<Dfo.Server.Data.Accounts.AccountId?> Consume(string mId, string ticket, Dfo.Server.Data.Networking.NetworkConnectionId? connectionId = null)
    {
        await using var lease = Scope.Begin();
        var result = await Services.GetRequiredService<IAuthenticationDomainService>().BindSessionAsync(connectionId ?? new Dfo.Server.Data.Networking.NetworkConnectionId(Guid.NewGuid().ToString()), mId, ticket, CancellationToken.None);
        await lease.CommitAsync(CancellationToken.None);
        return result;
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
}
