using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
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
        DungeonRunId runId) => FindCurrent(parties, sessions, runs, runGenerations, actor, runId);

    internal static IReadOnlyList<SelectedSessionIdentity> FindCurrent(
        IPartyService parties, ISelectedSessionDomainService sessions, IDungeonRunDomainService runs,
        Dfo.Server.Abstractions.Networking.IDungeonRunProjectionDomainService runGenerations,
        SelectedSessionIdentity actor, DungeonRunId runId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var run = runs.Find(runId);
        if (run is null || runs.Find(actor.CharacterId)?.RunId != runId)
            return [];
        var party = parties.FindByKey(run.PartyKey);
        if (party is null)
            return [];
        var admittedGenerations = runGenerations.Find(runId).ToHashSet();
        return party.Members
            .OrderBy(member => member.Slot)
            .Select(member => sessions.TryGetInWorld(member.CharacterId, out var inWorld) ? inWorld : null)
            .Where(session => session is not null
                && admittedGenerations.Contains(session)
                && (session.CharacterId != actor.CharacterId || session == actor)
                && runs.Find(session.CharacterId)?.RunId == runId)
            .Select(session => session!)
            .ToArray();
    }
}
