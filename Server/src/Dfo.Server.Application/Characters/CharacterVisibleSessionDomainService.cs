using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterVisibleSessionDomainService(ISelectedSessionDomainService sessions,
    ITownAreaDomainService towns, IDungeonRunDomainService runs, IIllusionTowerDomainService? towers = null,
    Dfo.Server.Abstractions.World.BossTower.IBossTowerDomainService? bossTower = null)
    : ICharacterVisibleSessionDomainService
{
    public IReadOnlyList<SelectedSessionIdentity> Resolve(SelectedSessionIdentity owner)
    {
        if (sessions.FindByCharacter(owner.CharacterId) != owner
            || sessions.FindByConnection(owner.ConnectionId) != owner) return [];
        var result = new HashSet<SelectedSessionIdentity> { owner };
        if (bossTower?.Find(owner) is { } boss)
        {
            foreach (var member in boss.Members)
                if (sessions.FindByCharacter(member.Session.CharacterId) == member.Session
                    && bossTower.Find(member.Session)?.RunId == boss.RunId) result.Add(member.Session);
        }
        else if (towers?.Find(owner) is { } tower)
        {
            foreach (var member in tower.Members)
                if (sessions.FindByCharacter(member.Session.CharacterId) == member.Session
                    && towers.Find(member.Session)?.RunId == tower.RunId) result.Add(member.Session);
        }
        else if (runs.Find(owner.CharacterId) is { } run)
        {
            foreach (var member in run.Participants)
                if (sessions.FindByCharacter(member.CharacterId) is { } peer
                    && runs.Find(member.CharacterId)?.RunId == run.RunId) result.Add(peer);
        }
        else if (towns.Find(owner.CharacterId) is { } own && owner.Owns(own)
            && towns.Find(own.Area) is { } area)
        {
            foreach (var member in area.VisibleTo(owner.CharacterId))
                if (sessions.FindByCharacter(member.CharacterId) is { } peer && peer.Owns(member)) result.Add(peer);
        }
        return result.ToArray();
    }

}
