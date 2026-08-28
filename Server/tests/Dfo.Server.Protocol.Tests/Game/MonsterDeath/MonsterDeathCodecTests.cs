using System.Buffers.Binary;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game.MonsterDeath;

namespace Dfo.Server.Protocol.Tests.Game.MonsterDeath;

/// <summary>
/// The two frames that answer a kill.
/// </summary>
/// <remarks>
/// Both are pinned against a working A21 session's own bytes: its empty-handed kill answers with
/// seven bytes and eighty-three, and the offsets below are the reference builder's.
/// </remarks>
public sealed class MonsterDeathCodecTests
{
    private static readonly CharacterSkillProtocolMirrorState SkillPoints = new(11, 22, 33, 44);

    [Fact]
    public void A_kill_with_no_drops_is_the_actor_number_a_zero_count_and_four_closing_bytes()
    {
        var body = MonsterDeathPayloadCodec.EncodeMonsterDeath(30946);

        Assert.Equal(7, body.Length);
        Assert.Equal(30946, BinaryPrimitives.ReadUInt16LittleEndian(body));
        Assert.Equal(0, body[2]);
        Assert.Equal([0x00, 0x00, 0xFF, 0x00], body[3..]);
    }

    /// <remarks>
    /// Every bonus line this server does not award is zero. That is asserted rather than left
    /// implicit: a stray non-zero would put a line in the player's experience popup claiming a
    /// bonus nobody paid, and -- because the client reads the base as the gain minus every line --
    /// it would also make the base it draws read short by that much.
    /// </remarks>
    [Fact]
    public void The_experience_frame_carries_the_four_fields_that_have_a_value_and_zero_elsewhere()
    {
        var body = MonsterDeathPayloadCodec.EncodeExperience(
            level: 7, totalExperience: 1234, SkillPoints, new AccountHonorLevel(3, 456),
            seriaBlessing: 0);

        Assert.Equal(83, body.Length);
        Assert.Equal(7, body[0x00]);
        Assert.Equal(1234u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x01)));
        Assert.Equal(11, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0x09)));
        Assert.Equal(22, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0x0B)));
        Assert.Equal(33, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0x0D)));
        Assert.Equal(44, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0x0F)));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x3B)));
        Assert.Equal(456u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x3F)));

        int[] valued = [0x00, 0x01, 0x02, 0x03, 0x04, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
                        0x10, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42];
        for (var offset = 0; offset < body.Length; offset++)
        {
            if (!valued.Contains(offset))
                Assert.Equal(0, body[offset]);
        }
    }

    /// <summary>The channel's line is written where the client reads it.</summary>
    /// <remarks>
    /// <c>+0x4B</c>, and it has to be declared rather than merely paid: the client works out the
    /// base line by subtracting every named line from the gain, so an amount paid into the total
    /// and left at zero here is drawn as though the kill itself were worth that much more. This
    /// pins the offset, because a bonus written one slot over would be drawn under another name
    /// and still add up.
    /// </remarks>
    [Fact]
    public void The_channel_bonus_is_written_in_the_slot_the_client_draws_it_from()
    {
        var body = MonsterDeathPayloadCodec.EncodeExperience(
            level: 7, totalExperience: 1234, SkillPoints, new AccountHonorLevel(3, 456),
            seriaBlessing: 3);

        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x1A)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x1E)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x4B)));
        Assert.Equal(83, body.Length);
    }

    [Fact]
    public void A_body_shorter_than_the_actor_number_and_the_user_is_refused()
    {
        var decoder = new MonsterDeathDecoder();

        Assert.Throws<InvalidDataException>(
            () => decoder.Decode(default!, new byte[3]));
    }
}
