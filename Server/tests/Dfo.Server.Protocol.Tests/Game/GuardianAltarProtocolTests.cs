using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.GuardianAltar;
using Dfo.Server.Protocol.Game.WeeklyAttendance;

namespace Dfo.Server.Protocol.Tests.Game;

public sealed class GuardianAltarProtocolTests
{
    private static readonly ImmutableDictionary<string, int> Attributes = ImmutableDictionary<string, int>.Empty;
    private static GuardianAltarCatalog Catalog => new([], new(.8, 3, [new(0, 10, 1000)]),
        ImmutableDictionary<int, GuardianAltarMap>.Empty,
        ImmutableDictionary<int, GuardianAltarSummon>.Empty.Add(1, new(1, "enemy", "normal", 64599, 1, 1, 1682, 265, 0)), [],
        [new(1, GuardianAltarShopKind.Unit, 1000, 0, [new(1, 2749171, 0, 100, 5, null, Attributes)]),
         new(1, GuardianAltarShopKind.Tower, 64597, 0, [new(1, 64597, 0, 100, null, null, Attributes)])],
        ImmutableDictionary<int, int>.Empty.Add(0, 500), [new(1, GuardianAltarDifficulty.Easy, 4, [new("normal", 2749211, 8)])]);
    private static GuardianAltarProgress Progress => new(0, new(2026, 10, 27), 3, 2, 0, 0, 0, 0, 0,
        ImmutableDictionary<int, int>.Empty.Add(3511, -1), [new(GuardianAltarShopKind.Unit, 1000, 0), new(GuardianAltarShopKind.Tower, 64597, 0)],
        [new(0, GuardianAltarShopKind.Unit, 1000)], []);
    private static GuardianAltarAdmission Admission => new(new(3511, false, [new(GuardianAltarDifficulty.Easy, 25081, 0, [])], [], []),
        new(25081, 64597, [new(125000, "normal", []), new(20000, "named", [])]), GuardianAltarDifficulty.Easy, 0);

    [Fact]
    public void Open_reads_no_body_and_every_action_rejects_short_or_long_packed_body()
    {
        foreach (var (operation, length) in new[] { (GuardianAltarOperation.Open, 0), (GuardianAltarOperation.Buy, 30),
            (GuardianAltarOperation.SetSlot, 28), (GuardianAltarOperation.UpgradeEnergy, 22), (GuardianAltarOperation.Summon, 23),
            (GuardianAltarOperation.Exchange, 37), (GuardianAltarOperation.Pause, 15), (GuardianAltarOperation.Achievement, 18), (GuardianAltarOperation.Reset, 18) })
        {
            Assert.NotNull(GuardianAltarRequestCodec.Decode(GameMessageMetadata.Empty, operation, new byte[length]));
            if (length > 0) Assert.Null(GuardianAltarRequestCodec.Decode(GameMessageMetadata.Empty, operation, new byte[length - 1]));
            Assert.Null(GuardianAltarRequestCodec.Decode(GameMessageMetadata.Empty, operation, new byte[length + 1]));
        }
    }
    [Fact]
    public void Summon_and_slot_fields_use_A21_packed_offsets_instead_of_70_alignment()
    {
        var body = new byte[23]; body[14] = 5;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(15), 2749171);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(19), 3);
        var summon = GuardianAltarRequestCodec.Decode(GameMessageMetadata.Empty, GuardianAltarOperation.Summon, body)!;
        Assert.Equal((byte)5, summon.Slot); Assert.Equal(2749171, summon.ItemId); Assert.Equal(3, summon.EnergyLevel);
        var slot = new byte[28]; BinaryPrimitives.WriteInt32LittleEndian(slot.AsSpan(14), 1); slot[18] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(slot.AsSpan(19), 2); slot[23] = 4;
        BinaryPrimitives.WriteInt32LittleEndian(slot.AsSpan(24), 4001);
        var equip = GuardianAltarRequestCodec.Decode(GameMessageMetadata.Empty, GuardianAltarOperation.SetSlot, slot)!;
        Assert.Equal(1, equip.RidableId); Assert.True(equip.Attach); Assert.Equal(2, equip.Kind);
        Assert.Equal((byte)4, equip.Slot); Assert.Equal(4001, equip.ItemId);
    }
    [Fact]
    public void Profile_has_a_33_byte_header_and_all_seven_counted_groups_without_unearned_achievements()
    {
        var body = GuardianAltarPayloadCodec.Open(Progress, Catalog);
        using var reader = new BinaryReader(new MemoryStream(body));
        Assert.Equal(1, reader.ReadByte()); Assert.Equal(0, reader.ReadInt32()); Assert.Equal(1u, reader.ReadUInt32());
        Assert.Equal(1, reader.ReadInt32()); Assert.Equal(3, reader.ReadInt32()); Assert.Equal(2, reader.ReadInt32());
        Assert.Equal(new byte[21], reader.ReadBytes(21));
        var expectedCounts = new[] { 1, 1, 0, 1, 6, 6, 0 }; var widths = new[] { 8, 12, 12, 12, 10, 10, 5 };
        for (var group = 0; group < widths.Length; group++)
        { Assert.Equal((uint)expectedCounts[group], reader.ReadUInt32()); Assert.Equal(expectedCounts[group] * widths[group], reader.ReadBytes(expectedCounts[group] * widths[group]).Length); }
        Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        Assert.Equal(new byte[] { 1, 1, 0, 0, 0 }, GuardianAltarPayloadCodec.Open(null, Catalog));
    }
    [Fact]
    public void Stage_map_includes_the_initial_monster_group_after_phase_summaries()
    {
        var cell = new MazeCell(0, 0); var a = Admission;
        var layout = new DungeonLayout(3511, 0, 1, 1, cell, cell, 25081, 42,
            new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(25081) });
        var run = new DungeonRunState(layout, cell, 25081, new HashSet<MazeCell> { cell }, DateTimeOffset.UnixEpoch,
            CurrentCellState: new(cell, 25081, 0, 1, [new(1, new(64597, 1, 0) { IsHostile = false }, false, false, [])], []));
        var body = GuardianAltarPayloadCodec.Map(run, a);
        Assert.Equal(65, body.Length);
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(4)));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(16)));
        Assert.Equal(25081, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(8)));
        Assert.Equal(145000, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(12)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(20)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(48)));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(56)));
        Assert.Equal((ushort)64597, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(58)));
    }
    [Fact]
    public void Enemy_spawn_contains_the_native_36_byte_declaration_after_its_sequence()
    {
        var body = GuardianAltarPayloadCodec.Spawn(17, new(0, 64599, 1, 0, 1682, 265, 0), Catalog);
        Assert.Equal(38, body.Length); Assert.Equal((ushort)17, BinaryPrimitives.ReadUInt16LittleEndian(body));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(2))); Assert.Equal(1, body[6]);
        Assert.Equal(64599, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(14)));
        Assert.Equal((short)1, BinaryPrimitives.ReadInt16LittleEndian(body.AsSpan(18)));
        Assert.Equal(1682, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(26)));
    }
    [Fact]
    public void Clear_result_uses_the_packed_25_byte_header_and_profile_update_reuses_the_complete_profile()
    {
        var admission = Admission with { Stage = Admission.Stage with
            { Maps = [new(GuardianAltarDifficulty.Easy, 25081, 0, [new("normal", 2749211, 4)])] } };
        var state = new GuardianAltarRunState(new(Guid.NewGuid()),
            SelectedSessionIdentity.Create(new(1), new(71), new("guardian-codec"), 1, 10011), admission,
            new(2026, 10, 27), GuardianAltarStatus.Won, 1, 2, DateTimeOffset.UnixEpoch, null,
            2, 0, 10000, 0, 1, [], 0, ImmutableDictionary<byte, DateTimeOffset>.Empty);
        var body = GuardianAltarPayloadCodec.Result(new(state) { StageEnded = true, StarDelta = 3,
            UnlockedPurchases = [new(GuardianAltarShopKind.Unit, 1000, 0)] }, Progress, Catalog);
        Assert.Equal(61, body.Length); Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(21)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(25)));
        Assert.Equal(2749211, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(33)));
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(37)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(41)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(57)));
        Assert.Equal(GuardianAltarPayloadCodec.Open(Progress, Catalog).AsSpan(9).ToArray(),
            GuardianAltarPayloadCodec.ProfileUpdate(Progress, Catalog).AsSpan(1).ToArray());
        Assert.Equal(new byte[] { 3, 0, 0, 0, 1 }, GuardianAltarPayloadCodec.Eplp(state, true));
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0 }, GuardianAltarPayloadCodec.Eplp(state with { Status = GuardianAltarStatus.Lost }, false));
    }
    [Theory]
    [InlineData(GuardianAltarOperation.Buy, 25)] [InlineData(GuardianAltarOperation.SetSlot, 19)]
    [InlineData(GuardianAltarOperation.UpgradeEnergy, 17)] [InlineData(GuardianAltarOperation.Summon, 14)]
    [InlineData(GuardianAltarOperation.Exchange, 27)] [InlineData(GuardianAltarOperation.Pause, 6)]
    [InlineData(GuardianAltarOperation.Achievement, 9)] [InlineData(GuardianAltarOperation.Reset, 9)]
    public void Action_responses_include_one_public_success_byte_then_the_exact_A21_struct(GuardianAltarOperation operation, int length) =>
        Assert.Equal(length, GuardianAltarPayloadCodec.Response(new(GameMessageMetadata.Empty, operation), 2, null, null).Length);

    [Fact]
    public void Entrance_uses_common_schedule_category_four_and_content_ten()
    {
        Assert.Equal(new byte[] { 4, 0, 0, 0, 1, 0, 0, 0, 10, 0, 0, 0, 0, 0, 0, 0 },
            new ScheduledActivityStateMessage(new(4, 10, true)).EncodeBody());
    }
}
