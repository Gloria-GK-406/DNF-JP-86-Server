using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.Dungeons;

/// <summary>Which dungeon owns a requester's session: one of the two towers, or neither.</summary>
/// <remarks>
/// Closed: the only cases are the nested records, so a <c>switch</c> over a place is exhaustive.
/// "Ordinary or none" carries no run; the ordinary flow still finds its run by character.
/// </remarks>
internal abstract record DungeonPlace
{
    private DungeonPlace(SelectedSessionIdentity session) => Session = session;

    public SelectedSessionIdentity Session { get; }

    internal sealed record InBossTower(SelectedSessionIdentity Session, BossTowerSnapshot Run) : DungeonPlace(Session);
    internal sealed record InIllusionTower(SelectedSessionIdentity Session, IllusionTowerSnapshot Run) : DungeonPlace(Session);
    internal sealed record OrdinaryOrNone(SelectedSessionIdentity Session) : DungeonPlace(Session);
}

/// <summary>Which dungeon owns a party-shaped request, judged over all of its candidates.</summary>
/// <remarks>Closed like <see cref="DungeonPlace"/>; tower cases carry every distinct run a candidate stands in.</remarks>
internal abstract record DungeonPartyPlace
{
    private DungeonPartyPlace() { }

    internal sealed record PartyInBossTower(IReadOnlyList<BossTowerSnapshot> Runs) : DungeonPartyPlace;
    internal sealed record PartyInIllusionTower(IReadOnlyList<IllusionTowerSnapshot> Runs) : DungeonPartyPlace;
    internal sealed record PartyOrdinaryOrNone : DungeonPartyPlace;
}

/// <summary>
/// The one answer to "which dungeon owns this request", read from the occupancy registry.
/// </summary>
/// <remarks>
/// The registry holds occupancy by character; a tower holds its seats by exact session. A tower
/// owns the session only when both agree: the registry names that tower and run, and the tower
/// still seats this very session in that run. Anything else, including a tower seat kept by an
/// older generation of the same character, is ordinary or none. Only the registered tower is
/// asked, so a lookup never touches the other tower. Read-only: it claims nothing and opens no
/// transaction.
/// </remarks>
internal static class DungeonLocator
{
    internal static DungeonPlace Locate(
        SelectedSessionIdentity session,
        IDungeonParticipationDomainService participation,
        IBossTowerDomainService bossTowers,
        IIllusionTowerDomainService illusionTowers)
    {
        ArgumentNullException.ThrowIfNull(session);
        return participation.Find(session.CharacterId) switch
        {
            { Mode: DungeonParticipationMode.BossTower, RunId: var runId }
                when bossTowers.Find(session) is { } run && run.RunId == runId
                => new DungeonPlace.InBossTower(session, run),
            { Mode: DungeonParticipationMode.IllusionTower, RunId: var runId }
                when illusionTowers.Find(session) is { } run && run.RunId == runId
                => new DungeonPlace.InIllusionTower(session, run),
            _ => new DungeonPlace.OrdinaryOrNone(session),
        };
    }

    /// <summary>
    /// Locates every distinct candidate. Any candidate in a Boss tower makes it a Boss tower
    /// request, else any in an illusion tower makes it an illusion tower request; runs keep
    /// candidate order, one per run.
    /// </summary>
    /// <remarks>Collecting the candidates (caller, target, online members) stays with the party code.</remarks>
    internal static DungeonPartyPlace LocateParty(
        IEnumerable<SelectedSessionIdentity> candidates,
        IDungeonParticipationDomainService participation,
        IBossTowerDomainService bossTowers,
        IIllusionTowerDomainService illusionTowers)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var places = candidates.Distinct()
            .Select(candidate => Locate(candidate, participation, bossTowers, illusionTowers))
            .ToArray();
        var bossRuns = places.OfType<DungeonPlace.InBossTower>()
            .Select(place => place.Run)
            .DistinctBy(run => run.RunId)
            .ToArray();
        if (bossRuns.Length > 0)
            return new DungeonPartyPlace.PartyInBossTower(bossRuns);
        var illusionRuns = places.OfType<DungeonPlace.InIllusionTower>()
            .Select(place => place.Run)
            .DistinctBy(run => run.RunId)
            .ToArray();
        if (illusionRuns.Length > 0)
            return new DungeonPartyPlace.PartyInIllusionTower(illusionRuns);
        return new DungeonPartyPlace.PartyOrdinaryOrNone();
    }
}
