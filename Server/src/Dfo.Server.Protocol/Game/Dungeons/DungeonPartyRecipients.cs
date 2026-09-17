using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.Dungeons;

/// <summary>
/// Resolves the current party projection at publication time. A dungeon run still owns which
/// characters are physically in it; party membership alone can never admit a town member.
/// </summary>
public sealed class DungeonPartyRecipients(
    IPartyService parties,
    ISelectedSessionDomainService sessions,
    IDungeonRunDomainService runs,
    DungeonRunDeliveryRegistry runGenerations)
{
    public IReadOnlyList<SelectedSessionIdentity> Find(
        SelectedSessionIdentity actor,
        DungeonRunId runId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var party = parties.FindParty(new PartyCharacterSnapshot(
            actor.AccountId, actor.CharacterId, actor.WireUserId, null));
        var admittedGenerations = runGenerations.Find(runId).ToHashSet();
        return party.Members
            .OrderBy(member => member.Slot)
            .Select(member => sessions.FindByCharacter(member.CharacterId))
            .Where(session => session is not null
                && admittedGenerations.Contains(session)
                && (session.CharacterId != actor.CharacterId || session == actor)
                && runs.Find(session.CharacterId)?.RunId == runId)
            .Select(session => session!)
            .ToArray();
    }
}
