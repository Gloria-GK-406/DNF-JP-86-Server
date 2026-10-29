using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Protocol.Tests.Support;

/// <summary>The occupancy registry agreeing with a stub illusion tower: its members sit in its run.</summary>
internal sealed class TowerParticipation(TowerDomainStub? tower) : IDungeonParticipationDomainService
{
    public DungeonParticipationOwner? Find(CharacterId character) =>
        tower is not null && tower.IsInTower(character)
            ? new(DungeonParticipationMode.IllusionTower, tower.Snapshot.RunId) : null;
    public bool TryClaim(IReadOnlyList<CharacterId> characters, DungeonParticipationOwner owner) => throw new NotSupportedException();
    public void Release(CharacterId character, DungeonParticipationOwner expected) => throw new NotSupportedException();
    public void ReleaseCommitted(CharacterId character, DungeonParticipationOwner expected) => throw new NotSupportedException();
}
