using System.Buffers.Binary;
using System.Text.Json;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Capture;
using Dfo.Server.Networking.Framing;

namespace Dfo.Server.Networking.Tests.Capture;

public sealed class NetworkFrameRecorderTests
{
    [Fact]
    public void Records_full_large_A21_request_and_distinguishes_same_opcode_CMD_and_NOTI()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-capture-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "frames.jsonl");
        var context = new NetworkConnectionContext(new("game-one"), NetworkEndpointKind.Game, 10010);
        var request = new byte[14 + 8192];
        request[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(1), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(3), (uint)request.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(7), 0x12345678);
        request[11] = 7;
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(12), 513);
        Array.Fill(request, (byte)0xAB, 14, 8192);
        var frame = new GameClientFrameRequestInfo();
        Assert.True(frame.OnParsingHeader(request.AsSpan(0, 14)));
        Assert.True(frame.OnParsingBody(request.AsSpan(14)));
        try
        {
            using (var recorder = new NetworkFrameRecorder(path))
            {
                recorder.Receive(context, frame);
                var cmd = GameServerFrameEncoder.Encode(1, 4, [1, 2]);
                var noti = GameServerFrameEncoder.Encode(0, 4, [3, 4]);
                var id = recorder.BeginSend(context, cmd.Concat(noti).ToArray());
                recorder.EndSend(id, true);
            }
            var lines = File.ReadAllLines(path).Select(line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Equal(4, lines.Length);
                var first = lines[0].RootElement;
                Assert.Equal(Convert.ToHexString(request), first.GetProperty("hex").GetString());
                Assert.Equal(14, first.GetProperty("headerLength").GetInt32());
                Assert.Equal(513, first.GetProperty("sequence").GetInt32());
                Assert.Equal("CMD", lines[1].RootElement.GetProperty("family").GetString());
                Assert.Equal("NOTI", lines[2].RootElement.GetProperty("family").GetString());
                Assert.True(lines[3].RootElement.GetProperty("flushed").GetBoolean());
            }
            finally { foreach (var line in lines) line.Dispose(); }
            Assert.Throws<IOException>(() => new NetworkFrameRecorder(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
