using System.Buffers.Binary;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Networking.Tests.Characterization.CharacterizationSteps;

namespace Dfo.Server.Networking.Tests.Characterization;

[Collection(CharacterizationCollection.Name)]
public sealed class HonorProgressionTests
{
    [PvfFact]
    public async Task Existing_account_honor_is_sent_on_login_roster_entry_and_return()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        var ct = timeout.Token;
        await using var server = await CharacterizationHost.StartAsync(null, ct);
        var (mId, ticket) = await server.RegisterAndIssueTicketAsync("honor-owner", ct);
        // Seed the legacy cumulative value without replaying several hundred million XP.
        await server.ExecuteSqlAsync("UPDATE accounts SET honor_exp = 450000001", ct);

        using var player = await server.ConnectAsync("honor-owner", ct);
        await player.LoginAsync(mId, ticket, ct);
        AssertHonor(Assert.Single(player.Frames, frame => frame.Is(0, 0x0289)), 10, 1);

        await player.CreateCharacterAsync("HonorOwner", ct);
        await player.CreateCharacterAsync("HonorAlt", ct);
        var before = player.Frames.Count();
        await player.SendAsync(0x0008, [0xFF, 0xFF, 0x02], ct);
        await player.FenceAsync(ct);
        AssertHonor(Assert.Single(player.Frames.Skip(before), frame => frame.Is(0, 0x0289)), 10, 1);

        before = player.Frames.Count();
        await player.SelectCharacterAsync(ct);
        AssertHonor(Assert.Single(player.Frames.Skip(before), frame => frame.Is(0, 0x0289)), 10, 1);
        AssertUserInfo(player.Frames.Skip(before), player.WireUserId, 10, 1);

        before = player.Frames.Count();
        await player.SendAsync(0x0007, [], ct);
        await player.FenceAsync(ct);
        AssertHonor(Assert.Single(player.Frames.Skip(before), frame => frame.Is(0, 0x0289)), 10, 1);

        before = player.Frames.Count();
        var original = player.WireUserId;
        await player.SendAsync(0x0004, [1, 0], ct);
        var selection = await player.ReadUntilAsync(frame => frame.Is(1, 0x0004), ct);
        Assert.Equal(1, selection.Body[0]);
        player.WireUserId = BinaryPrimitives.ReadUInt16LittleEndian(selection.Body[9..]);
        Assert.NotEqual(original, player.WireUserId);
        await player.FenceAsync(ct);
        AssertHonor(Assert.Single(player.Frames.Skip(before), frame => frame.Is(0, 0x0289)), 10, 1);
        AssertUserInfo(player.Frames.Skip(before), player.WireUserId, 10, 1);

        using var peer = await server.EnterWorldAsync("peer", "honor-peer", "HonorPeer", ct,
            () => server.ExecuteSqlAsync("UPDATE accounts SET honor_exp = 10000007 WHERE account_id = 2", ct));
        AssertUserInfo(peer.Frames, peer.WireUserId, 2, 7);
        await player.MoveToSharedAreaAsync(ct);
        before = player.Frames.Count();
        await peer.MoveToSharedAreaAsync(ct);
        await player.FenceAsync(ct);
        AssertBasicUserInfo(player.Frames.Skip(before), peer.WireUserId, 2, 7);
    }

    [PvfFact]
    public async Task A_committed_kill_crosses_the_honor_grade_without_resetting_other_members()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        var ct = timeout.Token;
        await using var server = await CharacterizationHost.StartAsync(null, ct);
        const uint maxCharacterExperience = 1_046_561_640;
        // Set up an existing account at the last point of grade 1. The kill itself is through TCP.
        var (leader, member) = await TwoPlayersInTownAsync(server, ct, () => server.ExecuteSqlAsync(
            $"UPDATE characters SET exp = {maxCharacterExperience} WHERE character_id = 1; "
            + "UPDATE accounts SET honor_exp = 449999999 WHERE account_id = 1; "
            + "UPDATE accounts SET honor_exp = 10000007 WHERE account_id = 2", ct));
        using var owner = leader;
        using var peer = member;
        await FormPartyAsync(leader, member, ct);
        await OpenDungeonListAsync(leader, member, ct);
        await EnterDungeonAsync(leader, member, 144, frame => frame.Is(0, 0x001D), ct);
        foreach (var client in new[] { leader, member })
        {
            await client.SendAsync(0x0025, new byte[8], ct);
            await client.FenceAsync(ct);
        }
        var runs = server.Services.GetRequiredService<IDungeonRunQueryDomainService>();
        var run = Assert.IsType<DungeonRunState>(runs.Find(new CharacterId(leader.WireUserId)));
        var next = run.Layout.Cells.Keys.First(cell =>
            Math.Abs(cell.X - run.CurrentCell.X) + Math.Abs(cell.Y - run.CurrentCell.Y) == 1);
        await leader.SendAsync(0x002D, [next.X, next.Y, 0, 0, 0, 0, 0, 0, 0, 0], ct);
        await FenceAllAsync(ct, leader, member);
        run = Assert.IsType<DungeonRunState>(runs.Find(new CharacterId(leader.WireUserId)));
        var actor = run.CurrentCellState.Actors.First(value => value.IsMonster && !value.IsDead);
        var death = new byte[62];
        BinaryPrimitives.WriteUInt32LittleEndian(death, actor.Sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(death.AsSpan(4), leader.WireUserId);
        var ownerBefore = leader.Frames.Count();
        var peerBefore = member.Frames.Count();
        await leader.SendAsync(0x0027, death, ct);
        await FenceAllAsync(ct, leader, member);

        var reward = Assert.Single(leader.Frames.Skip(ownerBefore), frame => frame.Is(0, 0x0025));
        var earned = checked(BinaryPrimitives.ReadUInt32LittleEndian(reward.Body[1..]) - maxCharacterExperience);
        Assert.True(earned > 0);
        AssertExperienceHonor(reward, 10, earned - 1);
        Assert.Equal(449_999_999L + earned, await ReadAccumulatedHonorAsync(server, 1, ct));
        var peerReward = Assert.Single(member.Frames.Skip(peerBefore), frame => frame.Is(0, 0x0025));
        AssertExperienceHonor(peerReward, 2, 7);
        Assert.Equal(10_000_007L, await ReadAccumulatedHonorAsync(server, 2, ct));

        await leader.SendAsync(0x0027, death, ct);
        await FenceAllAsync(ct, leader, member);
        Assert.Equal(449_999_999L + earned, await ReadAccumulatedHonorAsync(server, 1, ct));

        leader.Dispose();
        var (mId, ticket) = await server.IssueTicketAsync("leader-player", ct);
        using var reconnected = await server.ConnectAsync("honor-relogin", ct);
        await reconnected.LoginAsync(mId, ticket, ct);
        AssertHonor(Assert.Single(reconnected.Frames, frame => frame.Is(0, 0x0289)), 10, earned - 1);
    }

    private static void AssertExperienceHonor(WireFrame frame, uint level, uint experience)
    {
        var offset = 8 * frame.Body[0x2E];
        Assert.Equal(level, BinaryPrimitives.ReadUInt32LittleEndian(frame.Body[(0x3B + offset)..]));
        Assert.Equal(experience, BinaryPrimitives.ReadUInt32LittleEndian(frame.Body[(0x3F + offset)..]));
    }

    private static async Task<long> ReadAccumulatedHonorAsync(CharacterizationHost server, int account, CancellationToken ct)
    {
        await using var connection = new SqliteConnection($"Data Source={server.DatabasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT honor_exp FROM accounts WHERE account_id = @account";
        command.Parameters.AddWithValue("@account", account);
        return Assert.IsType<long>(await command.ExecuteScalarAsync(ct));
    }

    private static void AssertHonor(WireFrame frame, uint level, uint experience)
    {
        Assert.Equal(8, frame.Body.Length);
        Assert.Equal(level, BinaryPrimitives.ReadUInt32LittleEndian(frame.Body));
        Assert.Equal(experience, BinaryPrimitives.ReadUInt32LittleEndian(frame.Body[4..]));
    }

    private static void AssertUserInfo(IEnumerable<WireFrame> frames, ushort user, uint level, uint experience)
    {
        AssertBasicUserInfo(frames, user, level, experience);
        var detail = frames.First(frame => IsUserInfo(frame, 1, 18, user));
        Assert.Equal(level, BinaryPrimitives.ReadUInt32LittleEndian(detail.Body[9..]));
        Assert.Equal(experience, BinaryPrimitives.ReadUInt32LittleEndian(detail.Body[13..]));
    }

    private static void AssertBasicUserInfo(IEnumerable<WireFrame> frames, ushort user, uint level, uint experience)
    {
        var basic = frames.First(frame => IsUserInfo(frame, 0, 41, user));
        var tail = basic.Body[^64..];
        Assert.Equal(level, BinaryPrimitives.ReadUInt32LittleEndian(tail[39..]));
        Assert.Equal(experience, BinaryPrimitives.ReadUInt32LittleEndian(tail[43..]));
    }

    private static bool IsUserInfo(WireFrame frame, byte subtype, int userOffset, ushort user) =>
        frame.Is(0, 0x0002) && frame.Body.Length >= userOffset + 2 && frame.Body[0] == subtype
        && BinaryPrimitives.ReadUInt16LittleEndian(frame.Body[userOffset..]) == user;
}
