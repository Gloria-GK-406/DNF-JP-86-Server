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

    [PvfFact]
    public async Task A_conversation_opens_on_both_sides_carries_a_line_to_the_other_and_tells_who_left()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        await using var server = await CharacterizationHost.StartAsync(null, timeout.Token);
        using var a = await server.EnterWorldAsync("a", "conversation-a", "TalkerA", timeout.Token);
        using var b = await server.EnterWorldAsync("b", "conversation-b", "TalkerB", timeout.Token);

        await a.SendAsync(0x01A3, Counted("TalkerB"u8), timeout.Token);
        var opened = await a.ReadUntilAsync(frame => frame.Is(0x00, 0x016A), timeout.Token);
        var told = await b.ReadUntilAsync(frame => frame.Is(0x00, 0x016A), timeout.Token);
        var id = opened.Body.Slice(1, 4).ToArray();
        Assert.Equal([0x00, .. id, 0x02, .. Counted("TalkerB"u8), .. Counted("TalkerA"u8)], opened.Body.ToArray());
        Assert.Equal([0x00, .. id, 0x02, .. Counted("TalkerA"u8), .. Counted("TalkerB"u8)], told.Body.ToArray());

        await a.SendAsync(0x0011, [.. Line(43, "hi"u8, BinaryPrimitives.ReadUInt32LittleEndian(id)), .. Counted("TalkerA"u8), 0], timeout.Token);
        var line = await b.ReadUntilAsync(frame => frame.Is(0x00, 0x016F), timeout.Token);
        Assert.Equal([.. id, .. Counted("TalkerA"u8), .. Counted("hi"u8)], line.Body.ToArray());

        await a.SendAsync(0x01A5, id, timeout.Token);
        var left = await b.ReadUntilAsync(frame => frame.Is(0x00, 0x016E), timeout.Token);
        Assert.Equal([.. id, .. Counted("TalkerA"u8)], left.Body.ToArray());

        await a.FenceAsync(timeout.Token);
        Assert.False(a.IsClosed);
        Assert.DoesNotContain(a.Frames, frame => frame.Is(0x00, 0x016F) || frame.Is(0x01, 0x0011));
    }

    private static byte[] Counted(ReadOnlySpan<byte> bytes) => [.. BitConverter.GetBytes(bytes.Length), .. bytes];

    private static byte[] Line(byte mode, ReadOnlySpan<byte> text, uint aux = 0)
    {
        var body = new byte[1 + 2 + 4 + 4 + text.Length];
        body[0] = mode;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(3), aux);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(7), text.Length);
        text.CopyTo(body.AsSpan(11));
        return body;
    }
}
