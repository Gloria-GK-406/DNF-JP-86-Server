using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Data.Channel;
using Microsoft.Extensions.Logging;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Dfo.Server.Application;
using Dfo.Server.Infrastructure;
using Dfo.Server.Networking;
using Dfo.Server.Persistence;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Networking.Tests.Integration;

public sealed class ChannelControlMessagesIntegrationTests
{
    [Fact]
    public async Task ControlMessages_TraverseTypedCommandsAndPreserveLegacyPayloads()
    {
        var scriptPath = Path.GetTempFileName();
        await File.WriteAllBytesAsync(scriptPath, [0x10, 0x20, 0x30]);
        try
        {
            var channelPort = GetAvailablePort();
            var gamePort = GetAvailablePort();
            while (gamePort == channelPort)
            {
                gamePort = GetAvailablePort();
            }

            var builder = Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Networking:Enabled"] = "true",
                ["Networking:ChannelPort"] = channelPort.ToString(),
                ["Networking:GamePorts:0"] = gamePort.ToString(),
                ["Channel:ScriptVersion"] = "66",
                ["Channel:ScriptPath"] = scriptPath,
                ["Channel:PublicServerIp"] = "192.0.2.10",
                ["Channel:Channels:0:Id"] = "11",
                ["Channel:Channels:0:Name"] = "#ch.11",
                ["Channel:Channels:0:MaximumUsers"] = "500",
                ["Channel:Channels:0:Port"] = gamePort.ToString(),
            });
            builder.Services.AddSingleton<TimeProvider>(
                new FixedTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(8))));
            new InfrastructureModule().ConfigureServices(builder.Services, builder.Configuration);
            new PersistenceModule().ConfigureServices(builder.Services, builder.Configuration);
            new ApplicationModule().ConfigureServices(builder.Services, builder.Configuration);
            new NetworkingModule().ConfigureServices(builder.Services, builder.Configuration);
            new ProtocolModule().ConfigureServices(builder.Services, builder.Configuration);

            using var host = builder.Build();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await host.StartAsync(timeout.Token);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, channelPort, timeout.Token);
            var stream = client.GetStream();

            await SendRequestAsync(stream, 0x05, timeout.Token);
            var version = await ReadFrameAsync(stream, timeout.Token);
            Assert.Equal(0x06, version.MessageNumber);
            Assert.Equal(
                Convert.FromHexString("0EE2E44B83907B0A7C7CDFC60E4547C7306401405749FB5325EE8EA058F3F253"),
                version.Body);

            await SendRequestAsync(stream, 0x09, timeout.Token);
            var script = await ReadFrameAsync(stream, timeout.Token);
            Assert.Equal(0x0A, script.MessageNumber);
            var scriptPlaintext = Decrypt(script.Body, compressed: true);
            Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, scriptPlaintext[..3]);
            Assert.All(scriptPlaintext[3..], value => Assert.Equal(0, value));

            await SendRequestAsync(stream, 0x11, timeout.Token);
            var info = await ReadFrameAsync(stream, timeout.Token);
            Assert.Equal(0x12, info.MessageNumber);
            var infoPlaintext = Decrypt(info.Body, compressed: true);
            Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(infoPlaintext.AsSpan(2, 4)));
            Assert.Equal("#ch.11", ReadFixedAscii(infoPlaintext.AsSpan(6, 20)));
            Assert.Equal(500, BinaryPrimitives.ReadInt32LittleEndian(infoPlaintext.AsSpan(26, 4)));
            Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(infoPlaintext.AsSpan(30, 4)));
            Assert.Equal("192.0.2.10", ReadFixedAscii(infoPlaintext.AsSpan(34, 16)));
            Assert.Equal(gamePort, BinaryPrimitives.ReadInt32LittleEndian(infoPlaintext.AsSpan(50, 4)));

            await host.StopAsync(timeout.Token);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(scriptPath);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Protocol_business_failure_is_downgraded_but_a_refused_job_is_still_an_error(bool refuseJob)
    {
        var scriptPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(scriptPath, [1, 2, 3]);
        var channelPort = GetAvailablePort(); var gamePort = GetAvailablePort();
        while (gamePort == channelPort) gamePort = GetAvailablePort();
        var failure = new IOException("script version business probe");
        var capture = new FailureLogs();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Channel:ScriptVersion"] = "66", ["Channel:ScriptPath"] = scriptPath,
            ["Networking:Enabled"] = "true", ["Networking:ChannelPort"] = channelPort.ToString(),
            ["Networking:GamePorts:0"] = gamePort.ToString(), ["Logging:LogLevel:Default"] = "Trace",
        });
        builder.Logging.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddProvider(capture);
        new InfrastructureModule().ConfigureServices(builder.Services, builder.Configuration);
        new PersistenceModule().ConfigureServices(builder.Services, builder.Configuration);
        new ApplicationModule().ConfigureServices(builder.Services, builder.Configuration);
        new NetworkingModule().ConfigureServices(builder.Services, builder.Configuration);
        new ProtocolModule().ConfigureServices(builder.Services, builder.Configuration);
        builder.Services.AddSingleton<IChannelDirectoryDomainService>(new FailingChannel(failure));
        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, channelPort, timeout.Token);
        if (refuseJob) await host.Services.GetRequiredService<Dfo.Server.Application.Execution.GameplayExecutor>().StopAsync(timeout.Token);
        await SendRequestAsync(client.GetStream(), 5, timeout.Token);
        while (!capture.Entries.Any(entry => entry.Category.EndsWith(".ChannelNetworkPlugin", StringComparison.Ordinal)))
            await Task.Delay(10, timeout.Token);
        var line = Assert.Single(capture.Entries, entry => entry.Category.EndsWith(".ChannelNetworkPlugin", StringComparison.Ordinal));
        Assert.Equal(refuseJob ? LogLevel.Error : LogLevel.Debug, line.Level);
        if (refuseJob) Assert.IsType<GameplayExecutorStoppedException>(line.Error);
        else
        {
            Assert.Same(failure, line.Error);
            Assert.True(GameplayJob.EndedAJob(failure));
            Assert.Single(capture.Entries, entry => entry.Category.EndsWith(".GameplayExecutor", StringComparison.Ordinal)
                && ReferenceEquals(entry.Error, failure) && entry.Level == LogLevel.Error);
        }
        client.Close();
        await host.StopAsync(timeout.Token);
        }
        finally { File.Delete(scriptPath); }
    }

    private sealed class FailingChannel(Exception failure) : IChannelDirectoryDomainService
    {
        public string ScriptVersion => throw failure;
        public ValueTask<IReadOnlyList<ChannelServerInfo>> ChannelsAsync(CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<byte[]> ReadScriptAsync(CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed record FailureLog(string Category, LogLevel Level, Exception Error);
    private sealed class FailureLogs : ILoggerProvider
    {
        public ConcurrentQueue<FailureLog> Entries { get; } = new();
        public ILogger CreateLogger(string category) => new Capture(category, Entries);
        public void Dispose() { }
        private sealed class Capture(string category, ConcurrentQueue<FailureLog> entries) : ILogger
        {
            public bool IsEnabled(LogLevel level) => true;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
            { if (error is not null) entries.Enqueue(new(category, level, error)); }
        }
    }

    private static async Task SendRequestAsync(
        NetworkStream stream,
        byte messageNumber,
        CancellationToken cancellationToken)
    {
        var frame = ChannelFrameEncoder.Encode(0x7C, messageNumber, 0, 1, ReadOnlySpan<byte>.Empty);
        await stream.WriteAsync(frame, cancellationToken);
    }

    private static async Task<(byte MessageNumber, byte[] Body)> ReadFrameAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[ChannelFrameRequestInfo.HeaderLength];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var totalLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(2, 4));
        var body = new byte[checked((int)totalLength - header.Length)];
        await stream.ReadExactlyAsync(body, cancellationToken);
        return (header[1], body);
    }

    private static byte[] Decrypt(byte[] body, bool compressed)
    {
        var ciphertext = body;
        if (compressed)
        {
            using var input = new MemoryStream(body);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            ciphertext = output.ToArray();
        }

        using var aes = Aes.Create();
        aes.Key = Encoding.ASCII.GetBytes("20260810000006\0\0");
        return aes.DecryptEcb(ciphertext, PaddingMode.None);
    }

    private static string ReadFixedAscii(ReadOnlySpan<byte> bytes)
    {
        var terminator = bytes.IndexOf((byte)0);
        return Encoding.ASCII.GetString(terminator < 0 ? bytes : bytes[..terminator]);
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.CreateCustomTimeZone("Test/UTC+8", TimeSpan.FromHours(8), "Test/UTC+8", "Test/UTC+8");
    }
}
