using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Tests.Support;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.Dungeons;

/// <summary>
/// The component fixture registers occupancy from the dungeon services a test supplies, so a
/// handler that locates its dungeon sees the members those services seat.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DerivedDungeonParticipationTests
{
    [Fact]
    public void Fixture_registers_a_session_seated_by_a_supplied_tower_stand_in()
    {
        var sessions = TestSessions.Directory(out var state);
        var seated = sessions.InWorld(state, new AccountId(1), new CharacterId(1), "derived-1");
        var town = sessions.InWorld(state, new AccountId(2), new CharacterId(2), "derived-2");
        var tower = new TowerDomainStub(seated);

        var participation = ProtocolAlgorithmFixture.Create<Locating>(sessions, tower).Participation;

        Assert.Equal(new DungeonParticipationOwner(DungeonParticipationMode.IllusionTower, tower.Snapshot.RunId), participation.Find(seated.CharacterId));
        Assert.Null(participation.Find(town.CharacterId));
        var place = Assert.IsType<DungeonPlace.InIllusionTower>(DungeonLocator.Locate(seated, participation,
            ProtocolAlgorithmFixture.Default<IBossTowerDomainService>(), tower));
        Assert.Same(tower.Snapshot, place.Run);
    }

    [Fact]
    public void Fixture_keeps_a_supplied_registry_and_leaves_optional_ones_unset()
    {
        var supplied = new DerivedDungeonParticipation(
            ProtocolAlgorithmFixture.Default<IBossTowerDomainService>(),
            ProtocolAlgorithmFixture.Default<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerDomainService>(),
            ProtocolAlgorithmFixture.Default<IDungeonRunDomainService>());

        Assert.Same(supplied, ProtocolAlgorithmFixture.Create<Locating>(supplied).Participation);
        Assert.Null(ProtocolAlgorithmFixture.Create<OptionallyLocating>().Participation);
    }

    public sealed class Locating(IDungeonParticipationDomainService participation)
    {
        public IDungeonParticipationDomainService Participation { get; } = participation;
    }

    public sealed class OptionallyLocating(IDungeonParticipationDomainService? participation = null)
    {
        public IDungeonParticipationDomainService? Participation { get; } = participation;
    }
}
