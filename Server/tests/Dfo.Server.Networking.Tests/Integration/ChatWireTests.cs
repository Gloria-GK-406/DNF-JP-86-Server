using System.Buffers.Binary;
using Dfo.Server.Networking.Tests.Characterization;
using Dfo.Server.Testing;

namespace Dfo.Server.Networking.Tests.Integration;

/// <summary>Chat over a real socket: a refused line leaves the connection usable for the next one.</summary>
[Collection(CharacterizationCollection.Name)]
public sealed class ChatWireTests
{
    [PvfFact]
    public async Task A_refused_mode_is_answered_and_the_next_area_line_comes_back_to_the_speaker()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        await using var server = await CharacterizationHost.StartAsync(null, timeout.Token);
        using var speaker = await server.EnterWorldAsync("speaker", "chat-speaker", "ChatSpeaker", timeout.Token);
        await speaker.MoveToSharedAreaAsync(timeout.Token);

        await speaker.SendAsync(0x0011, Line(0, "server only"u8), timeout.Token);
        var refusal = await speaker.ReadUntilAsync(frame => frame.Is(0x01, 0x0011), timeout.Token);
        Assert.Equal(new byte[] { 0x00, 0x11 }, refusal.Body.ToArray());

        await speaker.SendAsync(0x0011, Line(3, "hello"u8), timeout.Token);
        var line = await speaker.ReadUntilAsync(frame => frame.Is(0x00, 0x000C), timeout.Token);
        Assert.Equal([0x03, .. BitConverter.GetBytes(speaker.WireUserId), 0x00, 5, 0, 0, 0, .. "hello"u8],
            line.Body.ToArray());

        await speaker.FenceAsync(timeout.Token);
        Assert.False(speaker.IsClosed);
        Assert.DoesNotContain(speaker.Frames, frame => frame.Is(0x01, 0x0011) && frame.Body[0] != 0);
    }

    private static byte[] Line(byte mode, ReadOnlySpan<byte> text)
    {
        var body = new byte[1 + 2 + 4 + 4 + text.Length];
        body[0] = mode;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(7), text.Length);
        text.CopyTo(body.AsSpan(11));
        return body;
    }
}
