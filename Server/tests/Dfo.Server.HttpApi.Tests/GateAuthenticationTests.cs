using System.Net;
using Dfo.Server.Testing;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Dfo.Server.HttpApi.Tests;

public sealed class GateAuthenticationTests
{
    private const string GatePath = "/test/gate";
    private const string OpenPath = "/test/open";
    private static readonly IPAddress Source = IPAddress.Parse("198.51.100.20");
    private static readonly IPAddress OtherSource = IPAddress.Parse("198.51.100.21");

    [Fact]
    public async Task UnconfiguredTokenAnswersNotFoundWithoutReachingTheEndpointOrCounting()
    {
        await using var harness = GateHarness.Create(token: null);

        var bare = await harness.SendAsync(GatePath);
        var authorized = await harness.SendAsync(GatePath, ["Bearer " + GateTestToken.New()]);

        foreach (var response in new[] { bare, authorized })
        {
            Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
            Assert.Equal("not_found", response.Code);
            Assert.False(response.Headers.ContainsKey("WWW-Authenticate"));
        }

        Assert.Equal(0, harness.GateCalls);
        Assert.False(harness.Limiter.IsLimited(Source));
        Assert.Equal(StatusCodes.Status200OK, (await harness.SendAsync(OpenPath)).StatusCode);
    }

    public enum Refusal
    {
        MissingHeader,
        BasicScheme,
        ShorterToken,
        LongerToken,
        SameLengthDifferentToken,
        EmptyToken,
        SchemeOnly,
        /// <summary>
        /// The middleware's own parsing only: <c>DefaultHttpContext</c> hands it the raw value. In
        /// production Kestrel strips the field value's surrounding whitespace before the middleware runs,
        /// so the same request passes there (<c>GateHttpHostTests</c> covers that path).
        /// </summary>
        TrailingSpaceReachingTheMiddleware,
        TwoHeaderValues,
        TokenWithoutScheme,
    }

    [Theory]
    [InlineData(Refusal.MissingHeader)]
    [InlineData(Refusal.BasicScheme)]
    [InlineData(Refusal.ShorterToken)]
    [InlineData(Refusal.LongerToken)]
    [InlineData(Refusal.SameLengthDifferentToken)]
    [InlineData(Refusal.EmptyToken)]
    [InlineData(Refusal.SchemeOnly)]
    [InlineData(Refusal.TrailingSpaceReachingTheMiddleware)]
    [InlineData(Refusal.TwoHeaderValues)]
    [InlineData(Refusal.TokenWithoutScheme)]
    public async Task EveryOtherAuthorizationIsATokenFailure(Refusal refusal)
    {
        var token = GateTestToken.New();
        var other = GateTestToken.New();
        await using var harness = GateHarness.Create(token);
        string[]? authorization = refusal switch
        {
            Refusal.MissingHeader => null,
            Refusal.BasicScheme => ["Basic " + token],
            Refusal.ShorterToken => ["Bearer " + token[..^1]],
            Refusal.LongerToken => ["Bearer " + token + "A"],
            Refusal.SameLengthDifferentToken => ["Bearer " + other],
            Refusal.EmptyToken => ["Bearer "],
            Refusal.SchemeOnly => ["Bearer"],
            Refusal.TrailingSpaceReachingTheMiddleware => ["Bearer " + token + " "],
            Refusal.TwoHeaderValues => ["Bearer " + token, "Bearer " + token],
            Refusal.TokenWithoutScheme => [token],
            _ => throw new ArgumentOutOfRangeException(nameof(refusal)),
        };

        var response = await harness.SendAsync(GatePath, authorization);

        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", response.Code);
        Assert.Equal("Bearer", response.Headers["WWW-Authenticate"].ToString());
        Assert.Equal(0, harness.GateCalls);
        Assert.DoesNotContain(token, response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(other, response.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Bearer ")]
    [InlineData("bearer ")]
    [InlineData("BEARER    ")]
    public async Task BearerSchemeIsCaseInsensitiveAndMaySpanSeveralSpaces(string prefix)
    {
        var token = GateTestToken.New();
        await using var harness = GateHarness.Create(token);

        var response = await harness.SendAsync(GatePath, [prefix + token]);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(1, harness.GateCalls);
    }

    [Fact]
    public async Task EndpointsWithoutGateMetadataAreUntouchedAndUncounted()
    {
        var token = GateTestToken.New();
        await using var harness = GateHarness.Create(token);

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(StatusCodes.Status200OK, (await harness.SendAsync(OpenPath, ["Bearer wrong"])).StatusCode);
        }

        Assert.Equal(20, harness.OpenCalls);
        Assert.False(harness.Limiter.IsLimited(Source));
        Assert.Equal(StatusCodes.Status401Unauthorized, (await harness.SendAsync(GatePath)).StatusCode);
    }

    [Fact]
    public async Task TheEleventhFailureInAWindowIsTooManyRequestsWhileTheValidTokenStillPasses()
    {
        var token = GateTestToken.New();
        await using var harness = GateHarness.Create(token);
        var wrong = "Bearer " + GateTestToken.New();

        for (var i = 1; i <= 10; i++)
        {
            Assert.Equal(StatusCodes.Status401Unauthorized, (await harness.SendAsync(GatePath, [wrong])).StatusCode);
        }

        var limited = await harness.SendAsync(GatePath, [wrong]);
        Assert.Equal(StatusCodes.Status429TooManyRequests, limited.StatusCode);
        Assert.Equal("too_many_requests", limited.Code);
        Assert.Equal(StatusCodes.Status429TooManyRequests, (await harness.SendAsync(GatePath)).StatusCode);

        Assert.Equal(StatusCodes.Status200OK, (await harness.SendAsync(GatePath, ["Bearer " + token])).StatusCode);
        Assert.Equal(1, harness.GateCalls);
        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            (await harness.SendAsync(GatePath, [wrong], remote: OtherSource)).StatusCode);

        harness.Clock.Advance(TimeSpan.FromSeconds(899));
        Assert.Equal(StatusCodes.Status429TooManyRequests, (await harness.SendAsync(GatePath, [wrong])).StatusCode);
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(StatusCodes.Status401Unauthorized, (await harness.SendAsync(GatePath, [wrong])).StatusCode);
    }

    [Fact]
    public async Task ConfiguredLimitAndWindowShapeTheThrottle()
    {
        var token = GateTestToken.New();
        await using var harness = GateHarness.Create(token, new Dictionary<string, string?>
        {
            [GateOptions.AuthFailureLimitKey] = "2",
            [GateOptions.AuthFailureWindowKey] = "60",
        });

        Assert.Equal(StatusCodes.Status401Unauthorized, (await harness.SendAsync(GatePath)).StatusCode);
        Assert.Equal(StatusCodes.Status401Unauthorized, (await harness.SendAsync(GatePath)).StatusCode);
        Assert.Equal(StatusCodes.Status429TooManyRequests, (await harness.SendAsync(GatePath)).StatusCode);
        harness.Clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(StatusCodes.Status429TooManyRequests, (await harness.SendAsync(GatePath)).StatusCode);
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(StatusCodes.Status401Unauthorized, (await harness.SendAsync(GatePath)).StatusCode);
    }

    [Fact]
    public async Task MissingConnectionAddressIsCountedUnderOneUnknownSource()
    {
        await using var harness = GateHarness.Create(GateTestToken.New());

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(StatusCodes.Status401Unauthorized, (await harness.SendAsync(GatePath, noRemote: true)).StatusCode);
        }

        Assert.Equal(StatusCodes.Status429TooManyRequests, (await harness.SendAsync(GatePath, noRemote: true)).StatusCode);
        Assert.Equal(StatusCodes.Status401Unauthorized, (await harness.SendAsync(GatePath)).StatusCode);
    }

    [Theory]
    [InlineData("203.0.113.9, 198.51.100.7", "198.51.100.7")]
    [InlineData("198.51.100.7", "198.51.100.7")]
    [InlineData(" 2001:db8::7 ", "2001:db8::7")]
    [InlineData("203.0.113.9, not-an-address", "198.51.100.20")]
    [InlineData("203.0.113.9,", "198.51.100.20")]
    [InlineData("", "198.51.100.20")]
    [InlineData(null, "198.51.100.20")]
    public async Task ValidRequestsCarryTheLastForwardedHopOrTheConnectionAddress(
        string? forwardedFor,
        string expected)
    {
        var token = GateTestToken.New();
        await using var harness = GateHarness.Create(token);

        var response = await harness.SendAsync(GatePath, ["Bearer " + token], forwardedFor: forwardedFor);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(IPAddress.Parse(expected), Assert.IsType<GateClientAddress>(response.ClientAddress).Address);
    }

    [Fact]
    public async Task MissingConnectionAddressWithoutForwardingIsNone()
    {
        var token = GateTestToken.New();
        await using var harness = GateHarness.Create(token);

        var response = await harness.SendAsync(GatePath, ["Bearer " + token], noRemote: true);

        Assert.Equal(IPAddress.None, Assert.IsType<GateClientAddress>(response.ClientAddress).Address);
    }

    [Fact]
    public async Task InvalidRequestsNeitherSetTheClientAddressNorCountOrLogTheForwardedHop()
    {
        var token = GateTestToken.New();
        await using var harness = GateHarness.Create(token);

        for (var i = 0; i < 10; i++)
        {
            var refused = await harness.SendAsync(GatePath, ["Bearer wrong"], forwardedFor: "198.51.100.7");
            Assert.Equal(StatusCodes.Status401Unauthorized, refused.StatusCode);
            Assert.Null(refused.ClientAddress);
        }

        Assert.Equal(StatusCodes.Status429TooManyRequests, (await harness.SendAsync(GatePath)).StatusCode);
        Assert.False(harness.Limiter.IsLimited(IPAddress.Parse("198.51.100.7")));
        Assert.All(harness.Logs.Messages, message =>
            Assert.DoesNotContain("198.51.100.7", message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefusalsKeepBothTheGateTokenAndThePresentedTokenOutOfLogsAndBodies()
    {
        var token = GateTestToken.New();
        var presented = GateTestToken.New();
        await using var harness = GateHarness.Create(token);
        var bodies = new List<string>();

        for (var i = 0; i < 12; i++)
        {
            bodies.Add((await harness.SendAsync(GatePath, ["Bearer " + presented])).Body);
            bodies.Add((await harness.SendAsync(GatePath, ["Bearer " + token[..^1]])).Body);
            bodies.Add((await harness.SendAsync(GatePath, ["Basic " + token])).Body);
        }

        var logs = harness.Logs.Messages;
        Assert.Contains(logs, message => message.Contains("401", StringComparison.Ordinal)
            && message.Contains(Source.ToString(), StringComparison.Ordinal));
        Assert.Contains(logs, message => message.Contains("429", StringComparison.Ordinal)
            && message.Contains(Source.ToString(), StringComparison.Ordinal));
        foreach (var text in logs.Concat(bodies))
        {
            Assert.DoesNotContain(token[..^1], text, StringComparison.Ordinal);
            Assert.DoesNotContain(presented, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LimiterKeysMappedAddressesAsTheirIPv4Form()
    {
        var clock = new ManualTimeProvider();
        var limiter = new GateAuthFailureLimiter(clock, GateTestOptions.Create(null, new Dictionary<string, string?>
        {
            [GateOptions.AuthFailureLimitKey] = "1",
        }));

        limiter.RecordFailure(IPAddress.Parse("::ffff:198.51.100.7"));

        Assert.True(limiter.IsLimited(IPAddress.Parse("198.51.100.7")));
        Assert.False(limiter.IsLimited(IPAddress.Parse("198.51.100.8")));
        Assert.False(limiter.IsLimited(null));
    }

    [Fact]
    public void FullLimiterEvictsTheEarliestWindowInsteadOfRefusingANewSource()
    {
        var clock = new ManualTimeProvider();
        var limiter = new GateAuthFailureLimiter(clock, GateTestOptions.Create(null, new Dictionary<string, string?>
        {
            [GateOptions.AuthFailureLimitKey] = "1",
        }));
        var earliest = IPAddress.Parse("192.0.2.1");
        var second = IPAddress.Parse("192.0.2.2");
        var newcomer = IPAddress.Parse("192.0.2.3");

        limiter.RecordFailure(earliest);
        clock.Advance(TimeSpan.FromSeconds(1));
        limiter.RecordFailure(second);
        clock.Advance(TimeSpan.FromSeconds(1));
        for (var i = 2; i < GateAuthFailureLimiter.Capacity; i++)
        {
            limiter.RecordFailure(new IPAddress(0x0A000000u + (uint)i));
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(limiter.IsLimited(earliest));
        limiter.RecordFailure(newcomer);

        Assert.False(limiter.IsLimited(earliest));
        Assert.True(limiter.IsLimited(second));
        Assert.True(limiter.IsLimited(newcomer));
    }

    /// <summary>
    /// The first key inserted is not the earliest window once it expired and restarted in place:
    /// eviction follows window start, not insertion order.
    /// </summary>
    [Fact]
    public void FullLimiterEvictsByWindowStartNotByInsertionOrder()
    {
        var clock = new ManualTimeProvider();
        var limiter = new GateAuthFailureLimiter(clock, GateTestOptions.Create(null, new Dictionary<string, string?>
        {
            [GateOptions.AuthFailureLimitKey] = "1",
            [GateOptions.AuthFailureWindowKey] = "60",
        }));
        var restarted = IPAddress.Parse("192.0.2.1");
        var earliest = IPAddress.Parse("192.0.2.2");
        var newcomer = IPAddress.Parse("192.0.2.3");

        limiter.RecordFailure(restarted);
        clock.Advance(TimeSpan.FromSeconds(10));
        limiter.RecordFailure(earliest);
        clock.Advance(TimeSpan.FromSeconds(50));
        limiter.RecordFailure(restarted);
        clock.Advance(TimeSpan.FromSeconds(1));
        for (var i = 2; i < GateAuthFailureLimiter.Capacity; i++)
        {
            limiter.RecordFailure(new IPAddress(0x0A000000u + (uint)i));
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(limiter.IsLimited(earliest));
        limiter.RecordFailure(newcomer);

        Assert.True(limiter.IsLimited(restarted));
        Assert.False(limiter.IsLimited(earliest));
        Assert.True(limiter.IsLimited(newcomer));
    }

    /// <summary>The earliest window here has also expired; it goes, and the live ones stay.</summary>
    [Fact]
    public void FullLimiterDropsAnExpiredEarliestWindowAndKeepsTheLiveOnes()
    {
        var clock = new ManualTimeProvider();
        var limiter = new GateAuthFailureLimiter(clock, GateTestOptions.Create(null, new Dictionary<string, string?>
        {
            [GateOptions.AuthFailureLimitKey] = "1",
            [GateOptions.AuthFailureWindowKey] = "60",
        }));
        var earliest = IPAddress.Parse("192.0.2.1");

        limiter.RecordFailure(earliest);
        clock.Advance(TimeSpan.FromSeconds(30));
        for (var i = 1; i < GateAuthFailureLimiter.Capacity; i++)
        {
            limiter.RecordFailure(new IPAddress(0x0A000000u + (uint)i));
        }

        clock.Advance(TimeSpan.FromSeconds(30));
        limiter.RecordFailure(IPAddress.Parse("192.0.2.3"));

        Assert.False(limiter.IsLimited(earliest));
        Assert.True(limiter.IsLimited(new IPAddress(0x0A000001u)));
        Assert.True(limiter.IsLimited(IPAddress.Parse("192.0.2.3")));
    }

    private sealed class GateHarness : IAsyncDisposable
    {
        private readonly WebApplication application;
        private readonly RequestDelegate pipeline;
        private int gateCalls;
        private int openCalls;

        private GateHarness(WebApplication application, ManualTimeProvider clock, GateLogCapture logs)
        {
            this.application = application;
            Clock = clock;
            Logs = logs;
            Limiter = application.Services.GetRequiredService<GateAuthFailureLimiter>();
            var builder = new ApplicationBuilder(application.Services);
            HttpApiApplicationExtensions.UseHttpApiMiddleware(builder);
            builder.UseEndpoints(endpoints =>
            {
                endpoints.MapGet(GatePath, () =>
                {
                    Interlocked.Increment(ref gateCalls);
                    return Results.Ok();
                }).WithMetadata(GateEndpointMetadata.Instance);
                endpoints.MapGet(OpenPath, () =>
                {
                    Interlocked.Increment(ref openCalls);
                    return Results.Ok();
                });
            });
            pipeline = builder.Build();
        }

        public ManualTimeProvider Clock { get; }
        public GateLogCapture Logs { get; }
        public GateAuthFailureLimiter Limiter { get; }
        public int GateCalls => Volatile.Read(ref gateCalls);
        public int OpenCalls => Volatile.Read(ref openCalls);

        public static GateHarness Create(string? token, IDictionary<string, string?>? extra = null)
        {
            // An empty builder keeps the process environment (and any real LauncherAuth__GateToken) out.
            var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
            builder.WebHost.UseKestrelCore();
            var logs = new GateLogCapture();
            builder.Logging.AddProvider(logs);
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
            var values = new Dictionary<string, string?> { [GateOptions.TokenKey] = token };
            if (extra is not null)
            {
                foreach (var (key, value) in extra) values[key] = value;
            }

            builder.Configuration.AddInMemoryCollection(values);
            builder.Services.AddRouting();
            var clock = new ManualTimeProvider();
            builder.Services.AddSingleton<TimeProvider>(clock);
            new HttpApiModule().ConfigureServices(builder.Services, builder.Configuration);
            return new GateHarness(builder.Build(), clock, logs);
        }

        public async Task<GateResponse> SendAsync(
            string path,
            string[]? authorization = null,
            string? forwardedFor = null,
            IPAddress? remote = null,
            bool noRemote = false)
        {
            var context = new DefaultHttpContext { RequestServices = application.Services };
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = path;
            context.Request.Scheme = "http";
            context.Connection.RemoteIpAddress = noRemote ? null : remote ?? Source;
            context.Response.Body = new MemoryStream();
            if (authorization is not null) context.Request.Headers.Authorization = new StringValues(authorization);
            if (forwardedFor is not null) context.Request.Headers["X-Forwarded-For"] = forwardedFor;

            await pipeline(context);

            context.Response.Body.Position = 0;
            var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
            string? code = null;
            if (body.Length > 0 && context.Response.ContentType?.StartsWith("application/json", StringComparison.Ordinal) == true)
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.ValueKind == JsonValueKind.Object
                    && json.RootElement.TryGetProperty("code", out var codeElement))
                {
                    code = codeElement.GetString();
                }
            }

            return new GateResponse(
                context.Response.StatusCode,
                body,
                code,
                context.Response.Headers,
                context.Features.Get<GateClientAddress>());
        }

        public async ValueTask DisposeAsync() => await application.DisposeAsync();
    }

    private sealed record GateResponse(
        int StatusCode,
        string Body,
        string? Code,
        IHeaderDictionary Headers,
        GateClientAddress? ClientAddress);
}
