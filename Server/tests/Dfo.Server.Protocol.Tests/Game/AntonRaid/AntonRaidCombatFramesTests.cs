using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Raids;
using Dfo.Server.Protocol.Game.AntonRaid;
using Xunit;

namespace Dfo.Server.Protocol.Tests.Game.AntonRaid;

[Trait("Category", "Unit")]
public sealed class AntonRaidCombatFramesTests
{
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
