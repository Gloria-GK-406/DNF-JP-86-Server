using System.Net;
using System.Text;
using System.Text.Json;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Hosting;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.HttpOperations;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Data.Configuration;
using Dfo.Server.Data.Networking;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.HttpApi.Tests;

/// <summary>
/// The launcher HTTP pipeline over stubbed gameplay: the production middleware and endpoints, with the
/// command bus, credential reader, readiness flags and archive digest under the test's control.
/// </summary>
internal sealed class ApiHarness : IAsyncDisposable
{
    /// <summary>The digest the stub archive reports unless a test changes it.</summary>
    internal const string ArchiveDigest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private readonly WebApplication application;
    private readonly RequestDelegate pipeline;

    private ApiHarness(WebApplication application, RequestDelegate pipeline, StubCommandBus commands,
        StubCredentialReader credentials, HttpApiReadiness readiness, StubGameReadiness game, StubArchiveIdentity archive)
    {
        this.application = application;
        this.pipeline = pipeline;
        Commands = commands;
        Credentials = credentials;
        Readiness = readiness;
        Game = game;
        Archive = archive;
    }

    public StubCommandBus Commands { get; }
    public StubCredentialReader Credentials { get; }
    public HttpApiReadiness Readiness { get; }
    public StubGameReadiness Game { get; }
    public StubArchiveIdentity Archive { get; }
    public IServiceProvider Services => application.Services;

    /// <param name="gateToken">The configured gate token; null leaves it unset even when the process environment has one.</param>
    public static ApiHarness Create(
        Func<ICommand, object>? execute = null,
        ILoggerProvider? loggerProvider = null,
        string? gateToken = null,
        IReadOnlyDictionary<string, string?>? configuration = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = ["--hostBuilder:reloadConfigOnChange=false"],
            EnvironmentName = "Development",
        });
        builder.Logging.ClearProviders();
        if (loggerProvider is not null)
        {
            builder.Logging.AddProvider(loggerProvider);
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
        }
        // Every gate key is set, empty meaning its default, so a real LauncherAuth__* variable cannot leak in.
        var settings = new Dictionary<string, string?>
        {
            ["LauncherAuth:Enabled"] = "true",
            ["LauncherAuth:ListenUrl"] = "http://127.0.0.1:18080",
            ["LauncherAuth:GateToken"] = gateToken ?? "",
            ["LauncherAuth:GateAuthFailureLimit"] = "",
            ["LauncherAuth:GateAuthFailureWindowSeconds"] = "",
            ["LauncherAuth:Launch"] = "",
            ["LauncherAuth:AdminUrl"] = "",
            ["Networking:Enabled"] = "true",
            ["Networking:GamePorts:0"] = "10011",
            ["Networking:GamePorts:1"] = "10100",
        };
        if (configuration is not null)
        {
            foreach (var (key, value) in configuration) settings[key] = value;
        }
        builder.Configuration.AddInMemoryCollection(settings);
        var commands = new StubCommandBus(execute);
        var credentials = new StubCredentialReader();
        var game = new StubGameReadiness();
        var archive = new StubArchiveIdentity();
        builder.Services.AddSingleton<ICommandBus>(commands);
        builder.Services.AddSingleton<ILauncherCredentialReader>(credentials);
        builder.Services.AddGameplayExecution();
        builder.Services.AddSingleton<IAuthenticationDomainService, StubAuthenticationService>();
        builder.Services.AddSingleton<IPublicEndpointSource>(new StubEndpointSource());
        builder.Services.AddSingleton<IGameServerReadiness>(game);
        builder.Services.AddSingleton<IContentArchiveIdentity>(archive);
        configureServices?.Invoke(builder.Services);
        new HttpApiModule().ConfigureServices(builder.Services, builder.Configuration);
        var app = builder.Build();
        app.MapHttpApi();
        var readiness = app.Services.GetRequiredService<HttpApiReadiness>();
        readiness.SetReady(true);
        var pipelineBuilder = new ApplicationBuilder(app.Services);
        HttpApiApplicationExtensions.UseHttpApiMiddleware(pipelineBuilder);
        pipelineBuilder.UseEndpoints(HttpApiApplicationExtensions.MapHttpApiEndpoints);
        var pipeline = pipelineBuilder.Build();
        return new ApiHarness(app, pipeline, commands, credentials, readiness, game, archive);
    }

    /// <param name="bearer">Sent as <c>Authorization: Bearer {bearer}</c> when not null.</param>
    public async Task<ApiResponse> SendAsync(
        string method,
        string path,
        string? body = null,
        string scheme = "https",
        IPAddress? remoteAddress = null,
        Stream? requestBody = null,
        string? contentType = "application/json",
        string? bearer = null,
        string? forwardedFor = null)
    {
        var context = new DefaultHttpContext { RequestServices = application.Services };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Scheme = scheme;
        context.Connection.RemoteIpAddress = remoteAddress ?? IPAddress.Parse("198.51.100.20");
        context.Response.Body = new MemoryStream();
        if (bearer is not null) context.Request.Headers.Authorization = "Bearer " + bearer;
        if (forwardedFor is not null) context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        if (body is not null)
        {
            context.Request.ContentType = contentType;
            context.Request.ContentLength = Encoding.UTF8.GetByteCount(body);
            context.Request.Body = requestBody ?? new MemoryStream(Encoding.UTF8.GetBytes(body));
        }

        await pipeline(context);
        context.Response.Body.Position = 0;
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return new ApiResponse(
            context.Response.StatusCode,
            responseBody,
            responseBody.Length == 0 ? null : JsonDocument.Parse(responseBody),
            context.Response.Headers);
    }

    public async ValueTask DisposeAsync() => await application.DisposeAsync();
}

internal sealed record ApiResponse(int StatusCode, string Body, JsonDocument? Document, IHeaderDictionary Headers) : IDisposable
{
    public JsonDocument Json => Document ?? throw new InvalidOperationException($"Status {StatusCode} came without a body.");

    public void Dispose() => Document?.Dispose();
}

internal sealed class StubCommandBus(Func<ICommand, object>? execute) : ICommandBus
{
    private int callCount;

    public int CallCount => Volatile.Read(ref callCount);

    public ValueTask<TResult> SendInternalAsync<TResult>(ICommand<TResult> command, InternalCommandOrigin origin, CancellationToken cancellationToken = default) where TResult : notnull
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref callCount);
        var result = execute?.Invoke(command) ?? throw new InvalidOperationException("No result arranged.");
        return ValueTask.FromResult((TResult)(object)new HttpRouteReply(result));
    }

    public ValueTask<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default)
        where TResult : notnull
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref callCount);
        var result = execute?.Invoke(command) ?? throw new InvalidOperationException("No result arranged.");
        return ValueTask.FromResult((TResult)result);
    }
}

/// <summary>Stands in for the hashing half of the two-phase credential routes; accepts every well-formed request.</summary>
internal sealed class StubCredentialReader : ILauncherCredentialReader
{
    private int registrations;
    private int loginChecks;

    public int Registrations => Volatile.Read(ref registrations);
    public int LoginChecks => Volatile.Read(ref loginChecks);

    public ValueTask<RegistrationPreparation> PrepareRegistrationAsync(
        string loginId,
        string password,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref registrations);
        return ValueTask.FromResult(new RegistrationPreparation(new PreparedRegistration(loginId, "stub-record")));
    }

    public ValueTask<LoginVerification> VerifyLoginAsync(
        string loginId,
        string password,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref loginChecks);
        return ValueTask.FromResult(new LoginVerification(
            AuthenticationStatus.Success,
            new VerifiedLogin(loginId, new AccountId(1), "stub-record", null)));
    }
}

internal sealed class StubEndpointSource : IPublicEndpointSource
{
    public PublicEndpointSnapshot Snapshot { get; } = new(
        new PublicNetworkEndpoint("203.0.113.10", 7001),
        [new PublicNetworkEndpoint("198.51.100.11", 10011)],
        new PublicGameLoginEndpoints("192.0.2.20", 10128, 17200, 17201));
}

internal sealed class StubGameReadiness : IGameServerReadiness
{
    private volatile bool ready = true;

    public bool NetworkEnabled => true;
    public bool IsReady => ready;

    public void SetReady(bool value) => ready = value;
}

internal sealed class StubArchiveIdentity : IContentArchiveIdentity
{
    private int calls;

    public Func<string?> Digest { get; set; } = () => ApiHarness.ArchiveDigest;
    public int Calls => Volatile.Read(ref calls);

    public ValueTask<string?> Sha256Async(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        return ValueTask.FromResult(Digest());
    }
}

internal sealed class StubAuthenticationService : IAuthenticationDomainService
{
    public ValueTask<RegistrationResult> RegisterAsync(
        PreparedRegistration registration,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<AuthenticationResult> AuthenticateAndIssueTicketAsync(
        VerifiedLogin login,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<AccountId?> BindSessionAsync(
        NetworkConnectionId connectionId,
        string loginId,
        string ticket,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class ReadTrackingStream(byte[] content) : MemoryStream(content)
{
    public int ReadCount { get; private set; }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ReadCount++;
        return base.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        ReadCount++;
        return base.Read(buffer);
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ReadCount++;
        return base.ReadAsync(buffer, cancellationToken);
    }
}

internal sealed class CaptureLoggerProvider : ILoggerProvider
{
    private readonly List<string> messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (messages) return messages.ToArray();
        }
    }

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(messages);
    public void Dispose() { }

    private sealed class CaptureLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // The exception is kept too, as a console sink would print it, so "never logged" covers it.
            var text = $"{logLevel}: {formatter(state, exception)}";
            if (exception is not null) text += Environment.NewLine + exception;
            lock (messages) messages.Add(text);
        }
    }
}
