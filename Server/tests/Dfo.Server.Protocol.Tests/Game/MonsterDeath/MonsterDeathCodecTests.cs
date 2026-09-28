using System.Buffers.Binary;
using Dfo.Server.Abstractions.World;
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
        var body = MonsterDeathPayloadCodec.EncodeMonsterDeath(
            MonsterDeathNotification.Empty(30946));

        Assert.Equal(7, body.Length);
        Assert.Equal(30946, BinaryPrimitives.ReadUInt16LittleEndian(body));
        Assert.Equal(0, body[2]);
        Assert.Equal([0x00, 0x00, 0xFF, 0x00], body[3..]);
    }

    /// <summary>
    /// A kill that paid gold, byte for byte against a captured frame.
    /// </summary>
    /// <remarks>
    /// <c>packet_log.upstream-killexp-20260827.txt</c>, the first kill of the run: actor 38088
    /// dropped 38 gold as piece 1, owned by user 3. The whole 55-byte body is transcribed, so this
    /// fails on any change to any offset rather than on the ones somebody thought to assert.
    /// <para>
    /// Gold is identifier zero and its amount rides in the count field. That is not this server's
    /// invention and not only the reference server's: the client decides a piece is gold by
    /// exactly that test.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_kill_that_paid_gold_is_the_captured_frame()
    {
        var body = MonsterDeathPayloadCodec.EncodeMonsterDeath(
            new MonsterDeathNotification(
                38088,
                [new MonsterDrop(1, new RolledDrop(0, 38, DropRarity.Common, DropItemKind.Gold))],
                Owner: 3,
                Instance: 0x6A904F8E));

        Assert.Equal(Bytes(
            "C8 94 01 01 00 00 00 00 00 00 26 00 00 00 00 00 00 00 00 8E 4F 90 6A 00 00 00 00 "
                + "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 03 00 00 00 00 00 "
                + "FF 00"),
            body);
    }

    /// <summary>
    /// A kill that paid gold and an item together, byte for byte against a captured frame.
    /// </summary>
    /// <remarks>
    /// The same capture, actor 32543: 44 gold as piece 3 and item 10088620 as piece 4, both under
    /// one instance. It is the frame that proves a kill may pay more than once -- which is the
    /// whole reason the five rates are rolled separately rather than chosen between.
    /// <para>
    /// 103 bytes: three, then two records of forty-eight, then four. A record of any other width
    /// cannot produce this length with two pieces in it, which is what makes forty-eight measured
    /// rather than transcribed.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_kill_that_paid_twice_is_the_captured_frame()
    {
        var body = MonsterDeathPayloadCodec.EncodeMonsterDeath(
            new MonsterDeathNotification(
                32543,
                [
                    new MonsterDrop(3, new RolledDrop(0, 44, DropRarity.Common, DropItemKind.Gold)),
                    new MonsterDrop(
                        4,
                        new RolledDrop(10088620, 1, DropRarity.Common, DropItemKind.Stackable)),
                ],
                Owner: 3,
                Instance: 0x6A904F9C));

        Assert.Equal(103, body.Length);
        Assert.Equal(Bytes(
            "1F 7F 02 03 00 00 00 00 00 00 2C 00 00 00 00 00 00 00 00 9C 4F 90 6A 00 00 00 00 "
                + "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 03 00 00 00 04 00 "
                + "AC F0 99 00 00 01 00 00 00 00 00 00 00 00 9C 4F 90 6A 00 00 00 00 00 00 00 00 "
                + "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 03 00 00 00 00 00 FF 00"),
            body);
    }

    [Fact]
    public void Each_drop_record_can_name_its_own_party_owner()
    {
        var body = MonsterDeathPayloadCodec.EncodeMonsterDeath(
            new MonsterDeathNotification(
                32543,
                [
                    new MonsterDrop(3, new RolledDrop(10, 1, DropRarity.Common, DropItemKind.Stackable)),
                    new MonsterDrop(4, new RolledDrop(20, 1, DropRarity.Common, DropItemKind.Stackable)),
                ],
                Owner: 3,
                DropOwners: new Dictionary<ushort, ushort> { [3] = 7, [4] = 9 }));

        Assert.Equal(7, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(3 + 44)));
        Assert.Equal(9, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(3 + 48 + 44)));
    }

    private static byte[] Bytes(string hex) =>
        [.. hex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => Convert.ToByte(pair, 16))];

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
            ExperienceBonusLines.None);

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

    /// <summary>
    /// The declared lines are, pair for pair, the ones the measurement produced.
    /// </summary>
    /// <remarks>
    /// Pinned against a transcription of <c>Docs/ExperienceNoticeSlots.md</c> §4 held here, not
    /// against the production table, and pinned as whole triples rather than as separate sets of
    /// kinds and offsets. Comparing the sets separately passes a table whose rows were transposed
    /// among themselves -- every kind present, every offset present, every pairing wrong -- and a
    /// transposed pairing is exactly this task's defect: the bonus is drawn under another line's
    /// name and the breakdown still adds up, so nothing anywhere reports it.
    /// <para>
    /// The client's own name for each line is part of the comparison because it is the only half a
    /// human can check. An offset is unfalsifiable by inspection; 赛丽亚的祝福奖励 next to
    /// <c>+0x1A</c> can be read back against the measurement, or against the screen.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_declared_lines_are_pair_for_pair_the_measured_ones()
    {
        Assert.Equal(Measured.Length, MonsterDeathPayloadCodec.NamedLines.Count);
        foreach (var (kind, offset, clientName) in Measured)
        {
            var declared = Assert.Single(
                MonsterDeathPayloadCodec.NamedLines, line => line.Kind == kind);
            Assert.Equal(offset, declared.Offset);
            Assert.Equal(clientName, declared.ClientName);
        }

        // Every kind but Unlisted has a line, so no kind can be paid and silently undeclared.
        var declarable = Enum.GetValues<ExperienceBonusKind>()
            .Where(kind => kind is not ExperienceBonusKind.Unlisted);
        Assert.Equal(
            declarable.Order(),
            MonsterDeathPayloadCodec.NamedLines.Select(line => line.Kind).Order());

        // And neither of the two slots the client handles asymmetrically is among them.
        Assert.DoesNotContain(0x2F, MonsterDeathPayloadCodec.NamedLines.Select(line => line.Offset));
        Assert.DoesNotContain(0x33, MonsterDeathPayloadCodec.NamedLines.Select(line => line.Offset));
    }

    /// <summary>Each line lands on the offset the client draws that name from.</summary>
    /// <remarks>
    /// Driven from the measurement transcribed here rather than from the production table, so the
    /// writer is checked against what was observed rather than against another copy of itself.
    /// </remarks>
    [Fact]
    public void Every_named_line_is_written_at_the_offset_the_client_draws_it_from()
    {
        foreach (var (kind, offset, _) in Measured)
        {
            var body = MonsterDeathPayloadCodec.EncodeExperience(
                level: 7, totalExperience: 1234, SkillPoints, new AccountHonorLevel(3, 456),
                LinesOf((kind, 77u)));

            Assert.Equal(77u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(offset)));
            foreach (var other in AllBonusOffsets.Where(at => at != offset))
                Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(other)));
        }
    }

    /// <summary>Two reasons sharing a line are drawn as one amount.</summary>
    /// <remarks>
    /// Nothing stops two entitlements landing on the same line, and dropping either would leave
    /// the base reading long by that much -- the total is unaffected, so only the player's own
    /// arithmetic would catch it.
    /// </remarks>
    [Fact]
    public void Two_reasons_on_one_line_are_added_together()
    {
        var body = MonsterDeathPayloadCodec.EncodeExperience(
            level: 7, totalExperience: 1234, SkillPoints, new AccountHonorLevel(3, 456),
            LinesOf(
                (ExperienceBonusKind.SeriaBlessing, 3u),
                (ExperienceBonusKind.SeriaBlessing, 4u)));

        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x1A)));
    }

    /// <summary>A bonus with no identified line is left to the base, not forced into a slot.</summary>
    [Fact]
    public void A_bonus_belonging_to_no_line_writes_nothing()
    {
        var body = MonsterDeathPayloadCodec.EncodeExperience(
            level: 7, totalExperience: 1234, SkillPoints, new AccountHonorLevel(3, 456),
            LinesOf((ExperienceBonusKind.Unlisted, 9u)));

        foreach (var offset in AllBonusOffsets)
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(offset)));
    }

    /// <summary>
    /// The two slots the client handles asymmetrically stay at zero whatever is paid.
    /// </summary>
    [Fact]
    public void The_slots_that_do_not_balance_are_never_written()
    {
        foreach (var kind in Enum.GetValues<ExperienceBonusKind>())
        {
            var body = MonsterDeathPayloadCodec.EncodeExperience(
                level: 7, totalExperience: 1234, SkillPoints, new AccountHonorLevel(3, 456),
                LinesOf((kind, 55u)));

            Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x2F)));
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x33)));
        }
    }

    /// <summary>The measurement switch still fills all fourteen slots with its own amounts.</summary>
    /// <remarks>
    /// This is how every name above was learned, and it has to keep working: the three inert slots
    /// have no other known way in, and re-running it is the only way to re-check the table.
    /// </remarks>
    [Fact]
    public void The_label_probe_still_pays_a_distinct_amount_into_every_slot()
    {
        var body = MonsterDeathPayloadCodec.EncodeExperience(
            level: 7, totalExperience: 1234, SkillPoints, new AccountHonorLevel(3, 456),
            LinesOf((ExperienceBonusKind.SeriaBlessing, 3u)), labelProbe: true);

        var written = AllBonusOffsets
            .Select(offset => BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(offset)));
        Assert.Equal(ExperienceDiagnostics.ProbeAmounts, written);
    }

    /// <summary>
    /// The nine drawn-and-subtracted lines, transcribed from <c>Docs/ExperienceNoticeSlots.md</c>.
    /// </summary>
    /// <remarks>
    /// A second copy of the production table on purpose: it is the anchor these tests compare
    /// against, so changing the server's table alone can never make them agree. Edit it only from
    /// a fresh measurement, never to make a test pass.
    /// </remarks>
    private static readonly (ExperienceBonusKind Kind, int Offset, string ClientName)[] Measured =
    [
        (ExperienceBonusKind.PartyReward, 0x05, "队伍奖励"),
        (ExperienceBonusKind.FatigueBuff, 0x15, "疲劳值Buff"),
        (ExperienceBonusKind.SeriaBlessing, 0x1A, "赛丽亚的祝福奖励"),
        (ExperienceBonusKind.GrowthContract, 0x1E, "成长之契约奖励"),
        (ExperienceBonusKind.FatigueBurn, 0x26, "疲劳值燃烧奖励"),
        (ExperienceBonusKind.InternetCafe, 0x2A, "网吧奖励"),
        (ExperienceBonusKind.RapidGrowthBuff, 0x43, "极速成长Buff奖励"),
        (ExperienceBonusKind.SeriaWelcome, 0x4B, "赛丽亚的欢迎"),
        (ExperienceBonusKind.GuildReward, 0x4F, "公会奖励"),
    ];

    /// <summary>The fourteen bonus slots, in the order the frame writes them.</summary>
    private static readonly int[] AllBonusOffsets =
        [0x05, 0x15, 0x1A, 0x1E, 0x22, 0x26, 0x2A, 0x2F, 0x33, 0x37, 0x43, 0x47, 0x4B, 0x4F];

    /// <summary>The lines an award of these contributions declares.</summary>
    /// <remarks>
    /// Built through a real award rather than by hand, so these tests exercise the same gathering
    /// the handler does.
    /// </remarks>
    private static ExperienceBonusLines LinesOf(params (ExperienceBonusKind Kind, uint Amount)[] paid) =>
        ExperienceBonusLines.Of(new ExperienceAward(
            Breakdown: paid.Aggregate(new ExperienceBreakdown(Base: 1),
                (result, one) => result.Add(one.Kind, one.Amount)),
            Level: 7,
            Total: 1234,
            LeveledUp: false));

    [Fact]
    public void A_body_shorter_than_the_actor_number_and_the_user_is_refused()
    {
        var decoder = new MonsterDeathDecoder();

        Assert.Throws<InvalidDataException>(
            () => decoder.Decode(default!, new byte[3]));
    }
}
