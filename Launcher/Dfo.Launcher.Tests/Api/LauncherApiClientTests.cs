using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Dfo.Launcher.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Launcher.Tests.Api;

public sealed class LauncherApiClientTests
{
    [Fact]
    public async Task Bootstrap_uses_the_versioned_relative_path_and_parses_the_contract()
    {
        var handler = new RecordingHandler(_ => Json(
            HttpStatusCode.OK,
            """{"apiVersion":"1","status":"ready","registrationEnabled":true}"""));
        using var client = CreateClient(handler);

        var result = await client.GetBootstrapAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("1", result.Value!.ApiVersion);
        Assert.True(result.Value.RegistrationEnabled);
        Assert.Equal(HttpMethod.Get, handler.Requests.Single().Method);
        Assert.Equal("https://server.example/root/api/launcher/v1/bootstrap", handler.Requests.Single().Uri);
    }

    [Fact]
    public async Task Register_posts_only_credentials_and_preserves_field_errors()
    {
        const string secret = "password-secret-sentinel";
        var calls = 0;
        var handler = new RecordingHandler(request =>
        {
            calls++;
            return calls == 1
                ? Json(HttpStatusCode.Created, """{"loginId":"player"}""")
                : Json(
                    HttpStatusCode.BadRequest,
                    """{"code":"invalid_request","message":"invalid","fieldErrors":{"loginId":"bad login"}}""");
        });
        using var client = CreateClient(handler);

        var success = await client.RegisterAsync("player", secret, CancellationToken.None);
        var failure = await client.RegisterAsync("bad", secret, CancellationToken.None);

        Assert.Equal("player", success.Value);
        Assert.Equal("invalid_request", failure.ErrorCode);
        Assert.Equal("bad login", failure.FieldErrors!["loginId"]);
        foreach (var request in handler.Requests)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://server.example/root/api/launcher/v1/register", request.Uri);
            using var body = JsonDocument.Parse(request.Body!);
            Assert.Equal(["loginId", "password"], body.RootElement.EnumerateObject()
                .Select(property => property.Name).Order().ToArray());
        }
    }

    [Fact]
    public async Task Login_parses_ticket_expiry_and_channel_without_additional_endpoint_inputs()
    {
        var expiry = DateTimeOffset.Parse("2026-09-10T12:34:56Z");
        var handler = new RecordingHandler(_ => Json(
            HttpStatusCode.OK,
            """{"loginId":"player","ticket":"safe_ticket-1","expiresAtUtc":"2026-09-10T12:34:56Z","channel":{"host":"203.0.113.10","port":7001}}"""));
        using var client = CreateClient(handler);

        var result = await client.LoginAsync("player", "password", CancellationToken.None);

        Assert.Equal("safe_ticket-1", result.Value!.Ticket);
        Assert.Equal(expiry, result.Value.ExpiresAtUtc);
        Assert.Equal(new LauncherChannelEndpoint("203.0.113.10", 7001), result.Value.Channel);
    }

    [Fact]
    public async Task Malformed_success_and_error_bodies_return_stable_safe_errors()
    {
        var calls = 0;
        var handler = new RecordingHandler(_ => ++calls == 1
            ? Json(HttpStatusCode.OK, "{")
            : Json(HttpStatusCode.InternalServerError, "<html>secret internals</html>", "text/html"));
        using var client = CreateClient(handler);

        var malformedSuccess = await client.GetBootstrapAsync(CancellationToken.None);
        var malformedError = await client.GetBootstrapAsync(CancellationToken.None);

        Assert.Equal("network_error", malformedSuccess.ErrorCode);
        Assert.Equal("request_failed", malformedError.ErrorCode);
        Assert.DoesNotContain("secret internals", malformedError.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_a_later_request_can_recover()
    {
        var calls = 0;
        var handler = new AsyncRecordingHandler(async (request, cancellationToken) =>
        {
            if (++calls == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return Json(HttpStatusCode.OK, """{"apiVersion":"1","status":"ready","registrationEnabled":false}""");
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://server.example/") };
        using var client = new LauncherApiClient(http);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetBootstrapAsync(cancellation.Token));
        var recovered = await client.GetBootstrapAsync(CancellationToken.None);

        Assert.True(recovered.Succeeded);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Timeout_and_network_failure_are_distinguished_and_retryable()
    {
        var calls = 0;
        var handler = new AsyncRecordingHandler((_, _) =>
        {
            calls++;
            return calls switch
            {
                1 => throw new TaskCanceledException("timeout"),
                2 => throw new HttpRequestException("network"),
                _ => Task.FromResult(Json(
                    HttpStatusCode.OK,
                    """{"apiVersion":"1","status":"ready","registrationEnabled":false}""")),
            };
        });
        using var client = CreateClient(handler);

        var timeout = await client.GetBootstrapAsync(CancellationToken.None);
        var network = await client.GetBootstrapAsync(CancellationToken.None);
        var recovered = await client.GetBootstrapAsync(CancellationToken.None);

        Assert.Equal("request_timeout", timeout.ErrorCode);
        Assert.Equal("network_error", network.ErrorCode);
        Assert.True(recovered.Succeeded);
    }

    [Theory]
    [InlineData(StatusCodes.Status307TemporaryRedirect)]
    [InlineData(StatusCodes.Status308PermanentRedirect)]
    public async Task Default_factory_never_forwards_credentials_across_redirects(int redirectStatus)
    {
        const string secret = "redirect-password-sentinel";
        var captureCalls = 0;
        string? capturedBody = null;
        await using var capture = await KestrelServer.StartAsync(app =>
            app.MapPost("/capture", async context =>
            {
                Interlocked.Increment(ref captureCalls);
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
                capturedBody = await reader.ReadToEndAsync(context.RequestAborted);
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            }));
        var firstHopCalls = 0;
        await using var redirect = await KestrelServer.StartAsync(app =>
        {
            app.MapPost("/api/launcher/v1/login", context =>
            {
                Interlocked.Increment(ref firstHopCalls);
                context.Response.StatusCode = redirectStatus;
                context.Response.Headers.Location = new Uri(capture.BaseAddress, "capture").AbsoluteUri;
                return Task.CompletedTask;
            });
            app.MapGet(
                "/api/launcher/v1/bootstrap",
                () => Results.Json(new
                {
                    apiVersion = "1",
                    status = "ready",
                    registrationEnabled = false,
                }));
        });
        using var client = new LauncherApiClientFactory(TimeSpan.FromSeconds(2))
            .Create(redirect.BaseAddress);

        var result = await client.LoginAsync("player", secret, CancellationToken.None);
        var normalLoopback = await client.GetBootstrapAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("redirect_not_allowed", result.ErrorCode);
        Assert.DoesNotContain(secret, result.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(1, Volatile.Read(ref firstHopCalls));
        Assert.Equal(0, Volatile.Read(ref captureCalls));
        Assert.Null(capturedBody);
        Assert.True(normalLoopback.Succeeded);
    }

    [Theory]
    [InlineData(StatusCodes.Status200OK)]
    [InlineData(StatusCodes.Status503ServiceUnavailable)]
    public async Task Operation_timeout_covers_stalled_success_and_error_bodies_and_retry_recovers(
        int stalledStatus)
    {
        var handler = new StalledBodyHandler((HttpStatusCode)stalledStatus);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://server.example/"),
            Timeout = TimeSpan.FromMilliseconds(150),
        };
        using var client = new LauncherApiClient(httpClient);
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var stopwatch = Stopwatch.StartNew();

        var timeout = await client.GetBootstrapAsync(watchdog.Token);
        stopwatch.Stop();
        var recovered = await client.GetBootstrapAsync(CancellationToken.None);

        Assert.False(watchdog.IsCancellationRequested);
        Assert.Equal("request_timeout", timeout.ErrorCode);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(1));
        Assert.True(recovered.Succeeded);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Caller_cancellation_during_a_stalled_body_propagates_and_retry_recovers()
    {
        var handler = new StalledBodyHandler(HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://server.example/"),
            Timeout = TimeSpan.FromSeconds(5),
        };
        using var client = new LauncherApiClient(httpClient);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetBootstrapAsync(cancellation.Token));
        var recovered = await client.GetBootstrapAsync(CancellationToken.None);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(recovered.Succeeded);
        Assert.Equal(2, handler.RequestCount);
    }

    private static LauncherApiClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://server.example/root/"),
            Timeout = TimeSpan.FromSeconds(5),
        });

    private static HttpResponseMessage Json(
        HttpStatusCode status,
        string body,
        string mediaType = "application/json") =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        };

    private sealed record CapturedRequest(HttpMethod Method, string Uri, string? Body);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.Requests.Add(new(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken)));
            return response(request);
        }
    }

    private sealed class AsyncRecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => response(request, cancellationToken);
    }

    private sealed class StalledBodyHandler(HttpStatusCode stalledStatus) : HttpMessageHandler
    {
        private int requestCount;

        public int RequestCount => Volatile.Read(ref this.requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref this.requestCount) == 1)
            {
                var stalled = new HttpResponseMessage(stalledStatus)
                {
                    Content = new StreamContent(new CancellationAwareStalledStream()),
                };
                stalled.Content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                return Task.FromResult(stalled);
            }

            return Task.FromResult(Json(
                HttpStatusCode.OK,
                """{"apiVersion":"1","status":"ready","registrationEnabled":false}"""));
        }
    }

    private sealed class CancellationAwareStalledStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class KestrelServer(WebApplication application, Uri baseAddress)
        : IAsyncDisposable
    {
        public Uri BaseAddress { get; } = baseAddress;

        public static async Task<KestrelServer> StartAsync(Action<WebApplication> configure)
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing",
            });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var application = builder.Build();
            configure(application);
            await application.StartAsync();
            var addresses = application.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses;
            var address = Assert.Single(addresses);
            return new KestrelServer(application, new Uri(address));
        }

        public async ValueTask DisposeAsync()
        {
            await application.StopAsync();
            await application.DisposeAsync();
        }
    }
}
