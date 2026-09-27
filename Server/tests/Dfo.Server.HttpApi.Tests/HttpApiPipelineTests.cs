using System.Net;
using System.Text;
using System.Text.Json;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Data.Configuration;
using Dfo.Server.Data.OnlineUsers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.HttpApi.Tests;

public sealed class HttpApiPipelineTests
{
    [Theory]
    [InlineData(true, StatusCodes.Status200OK, "ready")]
    [InlineData(false, StatusCodes.Status503ServiceUnavailable, "service_unavailable")]
    public async Task BootstrapUsesStableV1ReadinessJson(bool ready, int status, string marker)
    {
        await using var harness = ApiHarness.Create();
        harness.Readiness.SetReady(ready);

        var response = await harness.SendAsync("GET", "/api/launcher/v1/bootstrap");

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(marker, response.Json.RootElement.GetProperty(
            ready ? "status" : "code").GetString());
        if (ready)
        {
            Assert.Equal("1", response.Json.RootElement.GetProperty("apiVersion").GetString());
            Assert.True(response.Json.RootElement.GetProperty("registrationEnabled").GetBoolean());
        }
    }

    [Theory]
    [InlineData(AuthenticationStatus.Success, StatusCodes.Status201Created, null)]
    [InlineData(AuthenticationStatus.InvalidRequest, StatusCodes.Status400BadRequest, "invalid_request")]
    [InlineData(AuthenticationStatus.AccountExists, StatusCodes.Status409Conflict, "account_exists")]
    public async Task RegisterMapsApplicationResultsToStableHttpContract(
        AuthenticationStatus commandStatus,
        int expectedStatus,
        string? expectedCode)
    {
        var fieldErrors = commandStatus == AuthenticationStatus.InvalidRequest
            ? new Dictionary<string, string> { ["loginId"] = "bad login" }
            : null;
        await using var harness = ApiHarness.Create(command => command switch
        {
            RegisterAccountCommand => new RegistrationResult(
                commandStatus,
                commandStatus == AuthenticationStatus.Success ? "player" : null,
                fieldErrors),
            _ => throw new InvalidOperationException("Unexpected command."),
        });

        var response = await harness.SendAsync(
            "POST",
            "/api/launcher/v1/register",
            "{\"loginId\":\"player\",\"password\":\"password-1\"}");

        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedCode is null)
        {
            Assert.Equal("player", response.Json.RootElement.GetProperty("loginId").GetString());
        }
        else
        {
            Assert.Equal(expectedCode, response.Json.RootElement.GetProperty("code").GetString());
            if (commandStatus == AuthenticationStatus.InvalidRequest)
            {
                Assert.Equal(
                    "bad login",
                    response.Json.RootElement.GetProperty("fieldErrors").GetProperty("loginId").GetString());
            }
            else
            {
                Assert.False(response.Json.RootElement.TryGetProperty("fieldErrors", out _));
            }
        }
    }

    [Theory]
    [InlineData(AuthenticationStatus.InvalidRequest, StatusCodes.Status400BadRequest, "invalid_request")]
    [InlineData(AuthenticationStatus.InvalidCredentials, StatusCodes.Status401Unauthorized, "invalid_credentials")]
    public async Task LoginMapsRefusalsWithoutExposingCredentialDetails(
        AuthenticationStatus commandStatus,
        int expectedStatus,
        string expectedCode)
    {
        await using var harness = ApiHarness.Create(command => command switch
        {
            AuthenticateAndIssueTicketCommand => new AuthenticationResult(
                commandStatus,
                FieldErrors: commandStatus == AuthenticationStatus.InvalidRequest
                    ? new Dictionary<string, string> { ["password"] = "bad password" }
                    : null),
            _ => throw new InvalidOperationException("Unexpected command."),
        });

        var response = await harness.SendAsync(
            "POST",
            "/api/launcher/v1/login",
            "{\"loginId\":\"unknown\",\"password\":\"secret-sentinel\"}");

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedCode, response.Json.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("secret-sentinel", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown", response.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginSuccessProjectsTicketExpiryAndTheConfiguredChannelBootstrap()
    {
        var expiry = new DateTimeOffset(2026, 9, 10, 12, 34, 56, TimeSpan.Zero);
        await using var harness = ApiHarness.Create(command => command switch
        {
            AuthenticateAndIssueTicketCommand login when login.LoginId == "player" =>
                new AuthenticationResult(
                    AuthenticationStatus.Success,
                    "123456",
                    "0123456789ABCDEF0123456789ABCDEF",
                    expiry),
            _ => throw new InvalidOperationException("Unexpected command."),
        });

        var response = await harness.SendAsync(
            "POST",
            "/api/launcher/v1/login",
            "{\"loginId\":\"player\",\"password\":\"password-1\"}");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("123456", response.Json.RootElement.GetProperty("mId").GetString());
        Assert.False(response.Json.RootElement.TryGetProperty("loginId", out _));
        Assert.Equal("0123456789ABCDEF0123456789ABCDEF", response.Json.RootElement.GetProperty("ticket").GetString());
        Assert.Equal(expiry, response.Json.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset());
        var channel = response.Json.RootElement.GetProperty("channel");
        Assert.Equal("203.0.113.10", channel.GetProperty("host").GetString());
        Assert.Equal(7001, channel.GetProperty("port").GetInt32());
    }

    [Theory]
    [InlineData("/api/launcher/v1/register", "{\"loginId\":\"player\",\"password\":\"password-1\"}")]
    [InlineData("/api/launcher/v1/register", "{")]
    [InlineData("/api/launcher/v1/register/", "{\"loginId\":\"player\",\"password\":\"password-1\"}")]
    [InlineData("/api/launcher/v1/register/", "{")]
    [InlineData("/api/launcher/v1/login", "{\"loginId\":\"player\",\"password\":\"password-1\"}")]
    [InlineData("/api/launcher/v1/login", "{")]
    [InlineData("/api/launcher/v1/login/", "{\"loginId\":\"player\",\"password\":\"password-1\"}")]
    [InlineData("/api/launcher/v1/login/", "{")]
    public async Task RemotePlaintextCredentialRequestsAreRejectedBeforeAnyBodyRead(
        string path,
        string body)
    {
        await using var harness = ApiHarness.Create();
        var trackingBody = new ReadTrackingStream(Encoding.UTF8.GetBytes(body));

        var response = await harness.SendAsync(
            "POST",
            path,
            body,
            scheme: "http",
            remoteAddress: IPAddress.Parse("198.51.100.20"),
            requestBody: trackingBody);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal(
            "secure_transport_required",
            response.Json.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, trackingBody.ReadCount);
        Assert.Equal(0, harness.Commands.CallCount);
    }

    [Theory]
    [InlineData("/api/launcher/v1/register", null)]
    [InlineData("/api/launcher/v1/register", "text/plain")]
    [InlineData("/api/launcher/v1/login", null)]
    [InlineData("/api/launcher/v1/login", "text/plain")]
    public async Task MissingOrUnsupportedCredentialContentTypeUsesStableInvalidRequest(
        string path,
        string? contentType)
    {
        const string secret = "content-type-secret-sentinel";
        await using var harness = ApiHarness.Create();
        var body = $"{{\"loginId\":\"player\",\"password\":\"{secret}\"}}";
        var trackingBody = new ReadTrackingStream(Encoding.UTF8.GetBytes(body));

        var response = await harness.SendAsync(
            "POST",
            path,
            body,
            requestBody: trackingBody,
            contentType: contentType);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", response.Json.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, harness.Commands.CallCount);
        Assert.Equal(0, trackingBody.ReadCount);
        Assert.DoesNotContain(secret, response.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/launcher/v1/register", "application/json")]
    [InlineData("/api/launcher/v1/register", "application/problem+json")]
    [InlineData("/api/launcher/v1/login", "application/json")]
    [InlineData("/api/launcher/v1/login", "application/problem+json")]
    public async Task SupportedJsonContentTypesStillMapMalformedBodyToInvalidRequest(
        string path,
        string contentType)
    {
        await using var harness = ApiHarness.Create();

        var response = await harness.SendAsync("POST", path, "{", contentType: contentType);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", response.Json.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, harness.Commands.CallCount);
    }

    [Theory]
    [InlineData("/api/launcher/v1/register/", "http", "127.0.0.1", StatusCodes.Status201Created)]
    [InlineData("/api/launcher/v1/register/", "https", "198.51.100.20", StatusCodes.Status201Created)]
    [InlineData("/api/launcher/v1/login/", "http", "127.0.0.1", StatusCodes.Status200OK)]
    [InlineData("/api/launcher/v1/login/", "https", "198.51.100.20", StatusCodes.Status200OK)]
    public async Task RoutedCredentialEndpointsRemainAvailableOverAllowedTransports(
        string path,
        string scheme,
        string remoteAddress,
        int expectedStatus)
    {
        await using var harness = ApiHarness.Create(command => command switch
        {
            RegisterAccountCommand => new RegistrationResult(
                AuthenticationStatus.Success,
                "player"),
            AuthenticateAndIssueTicketCommand => new AuthenticationResult(
                AuthenticationStatus.Success,
                "player",
                "ticket_safe-123",
                new DateTimeOffset(2026, 9, 10, 12, 34, 56, TimeSpan.Zero)),
            _ => throw new InvalidOperationException("Unexpected command."),
        });

        var response = await harness.SendAsync(
            "POST",
            path,
            "{\"loginId\":\"player\",\"password\":\"password-1\"}",
            scheme: scheme,
            remoteAddress: IPAddress.Parse(remoteAddress));

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(1, harness.Commands.CallCount);
    }

    [Theory]
    [InlineData("/api/launcher/v1/register", "null")]
    [InlineData("/api/launcher/v1/register", "{}")]
    [InlineData("/api/launcher/v1/register", "{")]
    [InlineData("/api/launcher/v1/login", "null")]
    [InlineData("/api/launcher/v1/login", "{}")]
    [InlineData("/api/launcher/v1/login", "{")]
    public async Task NullMissingAndMalformedCredentialJsonUsesTheSameStableError(
        string path,
        string body)
    {
        await using var harness = ApiHarness.Create();

        var response = await harness.SendAsync("POST", path, body);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", response.Json.RootElement.GetProperty("code").GetString());
        Assert.False(response.Json.RootElement.TryGetProperty("fieldErrors", out _));
        Assert.Equal(0, harness.Commands.CallCount);
    }

    [Theory]
    [InlineData("/api/launcher/v1/register")]
    [InlineData("/api/launcher/v1/login")]
    public async Task CredentialEndpointsDoNotReadOrDispatchWhileServiceIsUnavailable(string path)
    {
        await using var harness = ApiHarness.Create();
        harness.Readiness.SetReady(false);

        var response = await harness.SendAsync(
            "POST",
            path,
            "{\"loginId\":\"player\",\"password\":\"password-1\"}");

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Equal("service_unavailable", response.Json.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, harness.Commands.CallCount);
    }

    [Fact]
    public async Task MissingAccountAndWrongPasswordHaveByteIdenticalExternalSemantics()
    {
        await using var harness = ApiHarness.Create(command => command switch
        {
            AuthenticateAndIssueTicketCommand =>
                new AuthenticationResult(AuthenticationStatus.InvalidCredentials),
            _ => throw new InvalidOperationException("Unexpected command."),
        });

        var wrong = await harness.SendAsync(
            "POST",
            "/api/launcher/v1/login",
            "{\"loginId\":\"known\",\"password\":\"wrong-password\"}");
        var missing = await harness.SendAsync(
            "POST",
            "/api/launcher/v1/login",
            "{\"loginId\":\"missing\",\"password\":\"wrong-password\"}");

        Assert.Equal(StatusCodes.Status401Unauthorized, wrong.StatusCode);
        Assert.Equal(wrong.Body, missing.Body);
    }

    [Fact]
    public async Task LoopbackPlaintextReachesBindingAndMalformedJsonGetsStableError()
    {
        await using var harness = ApiHarness.Create();

        var response = await harness.SendAsync(
            "POST",
            "/api/launcher/v1/register",
            "{",
            scheme: "http",
            remoteAddress: IPAddress.Loopback);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", response.Json.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, harness.Commands.CallCount);
    }

    [Fact]
    public async Task UnexpectedCommandFailureReturnsSafeInternalError()
    {
        var logs = new CaptureLoggerProvider();
        await using var harness = ApiHarness.Create(
            _ => throw new InvalidOperationException("SQL stack secret-sentinel"),
            logs);

        var response = await harness.SendAsync(
            "POST",
            "/api/launcher/v1/login",
            "{\"loginId\":\"player\",\"password\":\"secret-sentinel\"}");

        Assert.Equal(StatusCodes.Status500InternalServerError, response.StatusCode);
        Assert.Equal("internal_error", response.Json.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("secret-sentinel", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("SQL", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-sentinel", string.Join("\n", logs.Messages), StringComparison.Ordinal);
    }

    private sealed class ApiHarness : IAsyncDisposable
    {
        private readonly WebApplication application;
        private readonly RequestDelegate pipeline;

        private ApiHarness(
            WebApplication application,
            RequestDelegate pipeline,
            StubCommandBus commands,
            HttpApiReadiness readiness)
        {
            this.application = application;
            this.pipeline = pipeline;
            Commands = commands;
            Readiness = readiness;
        }

        public StubCommandBus Commands { get; }
        public HttpApiReadiness Readiness { get; }

        public static ApiHarness Create(
            Func<ICommand, object>? execute = null,
            ILoggerProvider? loggerProvider = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Development",
            });
            builder.Logging.ClearProviders();
            if (loggerProvider is not null)
            {
                builder.Logging.AddProvider(loggerProvider);
            }
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LauncherAuth:Enabled"] = "true",
                ["LauncherAuth:ListenUrl"] = "http://127.0.0.1:18080",
                ["Networking:Enabled"] = "true",
            });
            var commands = new StubCommandBus(execute);
            builder.Services.AddSingleton<ICommandBus>(commands);
            builder.Services.AddSingleton<IAuthenticationDomainService, StubAuthenticationService>();
            builder.Services.AddSingleton<IPublicEndpointSource>(new StubEndpointSource());
            builder.Services.AddSingleton<Dfo.Server.Abstractions.Hosting.IGameServerReadiness>(new ReadyGame());
            new HttpApiModule().ConfigureServices(builder.Services, builder.Configuration);
            var app = builder.Build();
            app.MapHttpApi();
            var readiness = app.Services.GetRequiredService<HttpApiReadiness>();
            readiness.SetReady(true);
            var pipelineBuilder = new ApplicationBuilder(app.Services);
            HttpApiApplicationExtensions.UseHttpApiMiddleware(pipelineBuilder);
            pipelineBuilder.UseEndpoints(HttpApiApplicationExtensions.MapHttpApiEndpoints);
            var pipeline = pipelineBuilder.Build();
            return new ApiHarness(app, pipeline, commands, readiness);
        }

        public async Task<ApiResponse> SendAsync(
            string method,
            string path,
            string? body = null,
            string scheme = "https",
            IPAddress? remoteAddress = null,
            Stream? requestBody = null,
            string? contentType = "application/json")
        {
            var context = new DefaultHttpContext { RequestServices = application.Services };
            context.Request.Method = method;
            context.Request.Path = path;
            context.Request.Scheme = scheme;
            context.Connection.RemoteIpAddress = remoteAddress ?? IPAddress.Parse("198.51.100.20");
            context.Response.Body = new MemoryStream();
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
                JsonDocument.Parse(responseBody));
        }

        public async ValueTask DisposeAsync() => await application.DisposeAsync();
    }

    private sealed class StubCommandBus(Func<ICommand, object>? execute) : ICommandBus
    {
        public int CallCount { get; private set; }

        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            var result = execute?.Invoke(command) ?? throw new InvalidOperationException("No result arranged.");
            return ValueTask.FromResult((TResult)result);
        }
    }

    private sealed class StubEndpointSource : IPublicEndpointSource
    {
        public PublicEndpointSnapshot Snapshot { get; } = new(
            new PublicNetworkEndpoint("203.0.113.10", 7001),
            [new PublicNetworkEndpoint("198.51.100.11", 10011)],
            new PublicGameLoginEndpoints("192.0.2.20", 10128, 17200, 17201));
    }

    private sealed class ReadyGame : Dfo.Server.Abstractions.Hosting.IGameServerReadiness
    {
        public bool IsReady => true;
    }

    private sealed class StubAuthenticationService : IAuthenticationDomainService
    {
        public ValueTask<RegistrationResult> RegisterAsync(
            string loginId,
            string password,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<AuthenticationResult> AuthenticateAndIssueTicketAsync(
            string loginId,
            string password,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<AccountId?> ConsumeTicketAsync(
            string loginId,
            string ticket,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ReadTrackingStream(byte[] content) : MemoryStream(content)
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

    private sealed class CaptureLoggerProvider : ILoggerProvider
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
                lock (messages) messages.Add(formatter(state, exception));
            }
        }
    }

    private sealed record ApiResponse(int StatusCode, string Body, JsonDocument Json) : IDisposable
    {
        public void Dispose() => Json.Dispose();
    }
}
