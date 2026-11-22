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
