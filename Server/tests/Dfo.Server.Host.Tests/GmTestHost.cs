using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Application;
using Dfo.Server.Data.Administration;
using Dfo.Server.GameServer;
using Dfo.Server.GmWeb;
using Dfo.Server.Host.Modules;
using Dfo.Server.HttpApi;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Host.Tests;

internal sealed class GmTestHost(IHost host, HttpClient client, string directory, GmLogCapture logs, X509Certificate2? certificate) : IAsyncDisposable
{
    internal const string Password = "gm-isolated-test-password";
    internal HttpClient Client { get; } = client;
    internal GmLogCapture Logs { get; } = logs;
    internal IServiceProvider Services => host.Services;
    internal Uri Address => Client.BaseAddress!;
    internal static string NewDirectory() { var path = Path.Combine(Path.GetTempPath(), "dfo-gm-auth-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    internal static async Task<GmTestHost> StartAsync(string? administrator = "operator", string password = Password, string permissions = "All", string? directory = null,
        CookieContainer? cookies = null, TimeProvider? clock = null, bool remoteConnection = false, bool https = false, GmAuditFault? fault = null,
        IReadOnlyDictionary<string, string?>? configuration = null, Action<IServiceCollection>? configureServices = null, int? listenerPort = null,
        bool realContent = false)
    {
        directory ??= NewDirectory();
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = listenerPort ?? ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var url = $"{(https ? "https" : "http")}://127.0.0.1:{port}";
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Path"] = Path.Combine(directory, "test.db"), ["GmWeb:Enabled"] = "true", ["GmWeb:ListenUrl"] = url,
            ["GmWeb:Bootstrap:LoginName"] = administrator, ["GmWeb:Bootstrap:Password"] = password, ["GmWeb:Bootstrap:Permissions"] = permissions,
            ["LauncherAuth:Enabled"] = "false", ["Networking:Enabled"] = "false", ["Game:EndpointHost"] = "127.0.0.1",
            ["GameLogin:ConnectionPort"] = "10128", ["GameLogin:EndpointPortA"] = "17200", ["GameLogin:EndpointPortB"] = "17201",
        });
        if (configuration is not null) config.AddInMemoryCollection(configuration);
        var logs = new GmLogCapture(); var feature = new GmWebModule(config);
        var builder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder().ConfigureLogging(logging => logging.AddProvider(logs))
            .ConfigureAppConfiguration((_, sources) => { sources.Sources.Clear(); sources.AddConfiguration(config); })
            .AddServerModules(new InfrastructureModule(), new PersistenceModule(), new ApplicationModule()).ConfigureGameServer()
            .ConfigureServices(services => { if (!realContent) GmMailTestContent.Register(services); if (clock is not null) services.AddSingleton(clock); if (fault is not null) services.AddSingleton<IGmAuditDomainService>(provider => new GmFaultAudit(provider.GetRequiredService<IGmAdministrationPersistence>(), fault)); configureServices?.Invoke(services); });
        X509Certificate2? certificate = null;
        if (https)
        {
            using var rsa = RSA.Create(2048); var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
            builder.ConfigureWebHost(web => web.ConfigureKestrel(server => server.ConfigureHttpsDefaults(tls => tls.ServerCertificate = certificate)));
        }
        builder.ConfigureHttpApi(config, remoteConnection ? new RemoteFeature(feature) : feature);
        var host = builder.Build(); await host.StartAsync();
        var handler = new HttpClientHandler { CookieContainer = cookies ?? new CookieContainer() };
        if (https) handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        return new(host, new HttpClient(handler) { BaseAddress = new Uri(url) }, directory, logs, certificate);
    }
    internal async Task<string> CsrfAsync() => (await Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/auth/csrf")).GetProperty("token").GetString()!;
    internal Task<HttpResponseMessage> LoginAsync(string csrf, string loginName = "operator", string password = Password) => PostAsync("/api/gm/v1/auth/login", csrf, new { loginName, password });
    internal Task<HttpResponseMessage> PostAsync(string path, string? csrf, object? payload = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload ?? new { }) };
        if (csrf is not null) request.Headers.Add("X-GM-CSRF", csrf);
        return Client.SendAsync(request);
    }
    internal async Task<List<string>> ReadAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "test.db")};Pooling=False"); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql; await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>(); while (await reader.ReadAsync()) values.Add(reader.GetValue(0).ToString()!); return values;
    }
    internal async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "test.db")};Pooling=False"); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
    }
    public async ValueTask DisposeAsync() { Client.Dispose(); await host.StopAsync(); host.Dispose(); certificate?.Dispose(); using var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "test.db")}"); SqliteConnection.ClearPool(connection); }
    private sealed class RemoteFeature(GmWebModule inner) : IHttpApiFeature
    {
        public bool Enabled => inner.Enabled; public Uri? ListenUrl => inner.ListenUrl;
        public void ConfigureServices(IServiceCollection services) => inner.ConfigureServices(services);
        public void ConfigureMiddleware(IApplicationBuilder app)
        {
            app.Use(next => context => { context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.8"); return next(context); });
            inner.ConfigureMiddleware(app);
        }
        public void MapEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints) => inner.MapEndpoints(endpoints);
    }
}
internal sealed class GmTestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    internal void Advance(TimeSpan duration) => now += duration;
}
internal sealed class GmAuditFault { internal bool Throw { get; set; } }
internal sealed class GmFaultAudit(IGmAdministrationPersistence persistence, GmAuditFault fault) : IGmAuditDomainService
{
    public async ValueTask AppendAsync(GmAuditRecord record, CancellationToken token) { await persistence.AppendAuditAsync(record, token); if (fault.Throw) throw new InvalidOperationException("Injected audit storage failure"); }
    public ValueTask<GmAuditPage> QueryAsync(int page, int pageSize, GmAuditFilter filter, CancellationToken token) => persistence.QueryAuditAsync(page, pageSize, filter, token);
}
internal sealed class GmLogCapture : ILoggerProvider
{
    internal System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();
    public ILogger CreateLogger(string name) => new Capture(Messages);
    public void Dispose() { }
    private sealed class Capture(System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
    }
}
