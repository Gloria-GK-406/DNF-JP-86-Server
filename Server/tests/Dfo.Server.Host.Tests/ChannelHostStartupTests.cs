using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Dfo.Server.Host.Tests;

public sealed class ChannelHostStartupTests
{
    [Fact]
    public async Task PackagedHost_FromUnrelatedWorkingDirectory_CompletesChannelBootstrap()
    {
        var workingDirectory = Path.Combine(
            Path.GetTempPath(), $"dfo-host-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);
        var channelPort = GetAvailablePort();
        var gamePort = GetAvailablePortExcept(channelPort);
        var output = new StringBuilder();
        using var process = StartHost(workingDirectory, channelPort, gamePort, output);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await WaitForListenerAsync(process, channelPort, output, timeout.Token);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, channelPort, timeout.Token);
            var stream = client.GetStream();

            await SendRequestAsync(stream, 0x0B, timeout.Token);
            var connect = await ReadFrameAsync(stream, timeout.Token);
            Assert.Equal(0x0C, connect.MessageNumber);
            var key = Encoding.ASCII.GetString(connect.Body, 4, 16).TrimEnd('\0');

            await SendRequestAsync(stream, 0x09, timeout.Token);
            var script = await ReadFrameAsync(stream, timeout.Token);
            Assert.Equal(0x0A, script.MessageNumber);
            var scriptPlaintext = Decrypt(script.Body, key);
            Assert.Contains((byte)'[', scriptPlaintext);

            await SendRequestAsync(stream, 0x11, timeout.Token);
            var channelInfo = await ReadFrameAsync(stream, timeout.Token);
            Assert.Equal(0x12, channelInfo.MessageNumber);
            var infoPlaintext = Decrypt(channelInfo.Body, key);
            Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(infoPlaintext.AsSpan(2, 4)));
            Assert.Equal(gamePort, BinaryPrimitives.ReadInt32LittleEndian(infoPlaintext.AsSpan(50, 4)));

            var startupOutput = Snapshot(output);
            Assert.Contains($"ContentRoot={Path.GetDirectoryName(typeof(Program).Assembly.Location)}", startupOutput);
            Assert.Contains("ChannelScript=", startupOutput);
            // ChannelSourceOptions resolves the configured path to an absolute path whose
            // separators follow the OS, so match the resolved tail instead of a literal forward-slash path.
            Assert.Contains(
                $"Resources{Path.DirectorySeparatorChar}Channel{Path.DirectorySeparatorChar}channel_info.etc",
                startupOutput);
            Assert.Contains("ChannelAddress=127.0.0.1", startupOutput);
            Assert.Contains("GameAddress=127.0.0.1", startupOutput);
            Assert.Contains($"ChannelPort={channelPort}", startupOutput);
            Assert.Contains($"GamePorts={gamePort}", startupOutput);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("unreadable")]
    public async Task InvalidRequiredChannelScript_FailsBeforeListenersStart(string kind)
    {
        var workingDirectory = Path.Combine(
            Path.GetTempPath(), $"dfo-host-invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);
        var scriptPath = Path.Combine(workingDirectory, "required-channel-script.etc");
        if (kind == "empty")
        {
            await File.WriteAllBytesAsync(scriptPath, []);
        }
        else if (kind == "unreadable")
        {
            Directory.CreateDirectory(scriptPath);
        }

        var channelPort = GetAvailablePort();
        var gamePort = GetAvailablePortExcept(channelPort);
        var output = new StringBuilder();
        using var process = StartHost(
            workingDirectory, channelPort, gamePort, output, scriptPath);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token);
            Assert.NotEqual(0, process.ExitCode);
            var startupOutput = Snapshot(output);
            Assert.Contains(scriptPath, startupOutput);
            Assert.Contains("Required channel script", startupOutput);

            using var probe = new TcpClient();
            await Assert.ThrowsAnyAsync<SocketException>(async () =>
                await probe.ConnectAsync(IPAddress.Loopback, channelPort));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    private static Process StartHost(
        string workingDirectory,
        int channelPort,
        int gamePort,
        StringBuilder output,
        string? scriptPath = null)
    {
        var assembly = typeof(Program).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet", $"\"{assembly}\"")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["Networking__Enabled"] = "true";
        startInfo.Environment["Networking__ChannelPort"] = channelPort.ToString();
        startInfo.Environment["Networking__GamePorts__0"] = gamePort.ToString();
        startInfo.Environment["Channel__PublicServerIp"] = "127.0.0.1";
        startInfo.Environment["Game__EndpointHost"] = "127.0.0.1";
        startInfo.Environment["Database__Path"] = Path.Combine(workingDirectory, "host.db");
        if (scriptPath is not null)
        {
            startInfo.Environment["Channel__ScriptPath"] = scriptPath;
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Host process could not be started.");
        process.OutputDataReceived += (_, args) => Append(output, args.Data);
        process.ErrorDataReceived += (_, args) => Append(output, args.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null) return;
        lock (output) output.AppendLine(line);
    }

    private static string Snapshot(StringBuilder output)
    {
        lock (output) return output.ToString();
    }

    private static async Task WaitForListenerAsync(
        Process process,
        int port,
        StringBuilder output,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (process.HasExited)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Host exited with code {process.ExitCode}.\n{output}");
            }

            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(20, cancellationToken);
            }
        }
    }

    private static async Task SendRequestAsync(
        NetworkStream stream,
        byte messageNumber,
        CancellationToken cancellationToken)
    {
        var frame = new byte[11];
        frame[0] = 0x7C;
        frame[1] = messageNumber;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(2, 4), (uint)frame.Length);
        frame[10] = 1;
        await stream.WriteAsync(frame, cancellationToken);
    }

    private static async Task<(byte MessageNumber, byte[] Body)> ReadFrameAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[11];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var totalLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(2, 4));
        var body = new byte[checked((int)totalLength - header.Length)];
        await stream.ReadExactlyAsync(body, cancellationToken);
        return (header[1], body);
    }

    private static byte[] Decrypt(byte[] body, string key)
    {
        using var input = new MemoryStream(body);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var compressed = new MemoryStream();
        zlib.CopyTo(compressed);
        using var aes = Aes.Create();
        aes.Key = Encoding.ASCII.GetBytes(key.PadRight(16, '\0'));
        return aes.DecryptEcb(compressed.ToArray(), PaddingMode.None);
    }

    private static int GetAvailablePortExcept(int excluded)
    {
        int port;
        do port = GetAvailablePort(); while (port == excluded);
        return port;
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
