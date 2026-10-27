using System.Buffers.Binary;

namespace Dfo.Server.Networking.Tests.Characterization;

/// <summary>Wire steps several scenarios share. Each ends with every involved connection fenced.</summary>
internal static class CharacterizationSteps
{
    /// <summary>Fences the actor first, then the others, so cross-connection frames are bounded too.</summary>
    public static async Task FenceAllAsync(CancellationToken cancellationToken, params WireClient[] clients)
    {
        foreach (var client in clients) await client.FenceAsync(cancellationToken);
    }

    public static void StepAll(string name, params WireClient[] clients)
    {
        foreach (var client in clients) client.Step(name);
    }

    /// <summary>
    /// Two players in the same channel, both moved off the private town gate into area 1:1, where
    /// they see each other. A party needs that: an invitation from the gate is silently refused.
    /// </summary>
    public static async Task<(WireClient Leader, WireClient Member)> TwoPlayersInTownAsync(
        CharacterizationHost server,
        CancellationToken cancellationToken,
        Func<Task>? beforeSelect = null)
    {
        var leader = await server.EnterWorldAsync("leader", "leader-player", "Leader", cancellationToken, beforeSelect);
        var member = await server.EnterWorldAsync("member", "member-player", "Member", cancellationToken, beforeSelect);
        await leader.MoveToSharedAreaAsync(cancellationToken);
        member.Step("set user area 1:1");
        await member.SendAsync(0x0024, Convert.FromHexString("01013E07FF00050100000000000000"), cancellationToken);
        await member.FenceAsync(cancellationToken);
        leader.Step("member arrives in 1:1");
        await leader.FenceAsync(cancellationToken);
        return (leader, member);
    }

    /// <summary>REQUEST_PEER type 0 from the leader, RESPONSE_PEER type 0 from the invitee.</summary>
    public static async Task FormPartyAsync(WireClient leader, WireClient member, CancellationToken cancellationToken)
    {
        StepAll("leader invites", leader, member);
        await leader.SendAsync(0x000A, [.. U16(member.WireUserId), 0x00, 0x40, 0, 0, 0, 0, 0], cancellationToken);
        await FenceAllAsync(cancellationToken, leader, member);
        Assert.Contains(member.Frames, frame => frame.Is(0x00, 0x0007));

        StepAll("member accepts", leader, member);
        await member.SendAsync(0x000B, [.. U16(leader.WireUserId), 0x00, 0, 0, 0, 0], cancellationToken);
        await FenceAllAsync(cancellationToken, member, leader);
        Assert.Contains(leader.Frames, frame => frame.Is(0x00, 0x0008));
    }

    /// <summary>
    /// ENTER_SELECT_DUNGEON from the leader with selection 0 (the interactive list), as the A21
    /// client sends when the player opens the dungeon list.
    /// </summary>
    public static async Task OpenDungeonListAsync(WireClient leader, WireClient member, CancellationToken cancellationToken)
    {
        StepAll("leader opens dungeon selection", leader, member);
        await leader.SendAsync(0x000F, [0, 0, 0, 0], cancellationToken);
        await FenceAllAsync(cancellationToken, leader, member);
    }

    /// <summary>SELECT_DUNGEON from the leader, read up to its entry frame, then both connections fenced.</summary>
    public static async Task EnterDungeonAsync(
        WireClient leader,
        WireClient member,
        int dungeonId,
        Func<WireFrame, bool> entryFrame,
        CancellationToken cancellationToken)
    {
        StepAll("leader selects dungeon " + dungeonId, leader, member);
        byte[] body = [.. U32((uint)dungeonId), 0, 0, 0, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0];
        await leader.SendAsync(0x0010, body, cancellationToken);
        var read = await leader.ReadUntilAsync(
            frame => entryFrame(frame) || frame.Is(0x01, 0x0010), cancellationToken);
        Assert.False(read.Is(0x01, 0x0010),
            $"SELECT_DUNGEON {dungeonId} was refused: {Convert.ToHexString(read.Body)}");
        await FenceAllAsync(cancellationToken, leader, member);
    }

    public static byte[] U16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        return bytes;
    }

    public static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }
}
