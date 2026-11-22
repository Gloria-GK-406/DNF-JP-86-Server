using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Raids;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.AntonRaid;
using Xunit;

namespace Dfo.Server.Protocol.Tests.Game.AntonRaid;

[Trait("Category", "Unit")]
public sealed class AntonRaidCombatFramesTests
{
    [Fact]
    public void Buff_targets_encode_squad_and_raid_scope_with_the_affected_character()
    {
        var now = DateTimeOffset.UnixEpoch;
        var snapshot = new AntonRaidCombatSnapshot(Guid.NewGuid(), 1, 1, now, 0,
            new Dictionary<int, AntonRaidMapStatus>(), new Dictionary<uint, uint>(), [], [])
        {
            Buffs =
            [
                new(0, [new(3, new(0x1234), now.AddSeconds(4), now.AddSeconds(9))]),
                new(1, [new(null, new(0x2345), now.AddSeconds(5), now.AddSeconds(10))]),
            ],
        };
        var frame = AntonRaidCombatFrames.Snapshot(snapshot, now).OfType<AntonRaidRuntimeNotice>()
            .Single(notice => notice.MessageType == ServerMessageType.GameAntonRaidBuffStatus);

        Assert.Equal<byte>([2, 0, 1, 3, 0, 0x34, 0x12, 4, 0, 0, 0, 9, 0, 0, 0,
            1, 1, 0xFF, 0xFF, 0x45, 0x23, 5, 0, 0, 0, 10, 0, 0, 0], frame.Body.ToArray());
    }

    [Fact]
    public void Tickets_encode_rejoining_members_and_saturate_the_wire_count()
    {
        var notice = AntonRaidRuntimeNotice.Tickets([new(new(0x1234), 65_536, true), new(null, 0, false)]);

        Assert.Equal(ServerMessageType.GameAntonRaidTickets, notice.MessageType);
        Assert.Equal<byte>([2, 0, 0, 0, 0x34, 0x12, 1, 0xFF, 0xFF, 0, 0, 0, 0, 0], notice.Body.ToArray());
    }

    [Theory]
    [InlineData(AntonRaidLifeState.Dead, (byte)0)]
    [InlineData(AntonRaidLifeState.Revived, (byte)1)]
    public void Combat_life_change_encodes_character_identity_and_state(AntonRaidLifeState state, byte wireState)
    {
        var now = DateTimeOffset.UnixEpoch;
        var snapshot = new AntonRaidCombatSnapshot(Guid.NewGuid(), 1, 1, now, 0,
            new Dictionary<int, AntonRaidMapStatus>(), new Dictionary<uint, uint>(), [], []);
        var change = new AntonRaidCombatChanged(snapshot, [], [], [], [], [])
        {
            LifeChange = new(new(0x1234), state),
        };

        var frame = Assert.IsType<AntonRaidRuntimeNotice>(Assert.Single(AntonRaidCombatFrames.Change(change, now)));
        Assert.Equal(ServerMessageType.GameAntonRaidMemberLife, frame.MessageType);
        Assert.Equal<byte>([0x34, 0x12, wireState], frame.Body.ToArray());
    }

    [Fact]
    public void Participation_uses_reset_before_ready_and_before_restoring_a_clear()
    {
        var now = DateTimeOffset.UnixEpoch;
        var snapshot = new AntonRaidCombatSnapshot(Guid.NewGuid(), 1, 1, now, 0,
            new Dictionary<int, AntonRaidMapStatus>(), new Dictionary<uint, uint>(), [],
            [new(Guid.NewGuid(), 221, 1, [new(0x1234)], true, 0, [])]);
        var change = new AntonRaidCombatChanged(snapshot, [], [], [], [], [], Participation: true)
        {
            ParticipationChanges =
            [
                new(210, AntonRaidParticipationKind.Departed, [new(0x1234)]),
                new(218, AntonRaidParticipationKind.Ready, [new(0x1234)]),
                new(221, AntonRaidParticipationKind.Cleared, [new(0x1234)]),
            ],
        };

        Assert.Equal<byte>(
            [4, 210, 0, 0, 0, 0, 1, 0x34, 0x12,
                218, 0, 0, 0, 0, 1, 0x34, 0x12,
                218, 0, 0, 0, 1, 1, 0x34, 0x12,
                221, 0, 0, 0, 4, 1, 0x34, 0x12],
            Participation(AntonRaidCombatFrames.Change(change, now)));
        Assert.Equal<byte>(
            [2, 221, 0, 0, 0, 0, 1, 0x34, 0x12, 221, 0, 0, 0, 4, 1, 0x34, 0x12],
            Participation(AntonRaidCombatFrames.Snapshot(snapshot, now)));

        static byte[] Participation(IEnumerable<IGameNotiMessage> frames) => frames.OfType<AntonRaidRuntimeNotice>()
            .Single(frame => frame.MessageType == ServerMessageType.GameAntonRaidParticipation).Body.ToArray();
    }

    [Fact]
    public void CombatSnapshotRestoresShieldWithoutReplayingPastAnimations()
    {
        var now = DateTimeOffset.UnixEpoch;
        var symbols = Enumerable.Range(121, 8).ToDictionary(key => (uint)key, _ => 1u);
        symbols.Add(110, 3000);
        var snapshot = new AntonRaidCombatSnapshot(Guid.NewGuid(), 1, 1, now, 0,
            new Dictionary<int, AntonRaidMapStatus>(), symbols, [], []);

        var replay = AntonRaidCombatFrames.Snapshot(snapshot, now)
            .OfType<AntonRaidRuntimeNotice>()
            .Where(frame => frame.MessageType == ServerMessageType.GameAntonRaidSymbols);
        Assert.Collection(replay,
            reset => Assert.Equal<byte>([1, 0, 0, 0, 0, 0, 0, 0, 0], reset.Body.ToArray()),
            restored => Assert.Equal<byte>([1, 110, 0, 0, 0, 0xB8, 0x0B, 0, 0], restored.Body.ToArray()));

        var phaseStart = new AntonRaidCombatChanged(snapshot, [], [], [], [], [], Full: true);
        var initialSymbols = AntonRaidCombatFrames.Change(phaseStart, now).OfType<AntonRaidRuntimeNotice>()
            .Last(frame => frame.MessageType == ServerMessageType.GameAntonRaidSymbols);
        Assert.Equal((byte)9, initialSymbols.Body.Span[0]);
    }
}
