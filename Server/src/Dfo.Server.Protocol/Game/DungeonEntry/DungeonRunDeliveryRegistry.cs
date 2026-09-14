using System.Collections.Immutable;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MonsterDeath;

namespace Dfo.Server.Protocol.Game.DungeonEntry;

/// <summary>
/// Freezes the selected-session generations that were attached when a dungeon run began.
/// </summary>
/// <remarks>
/// This is deliberately a protocol concern. The run owns stable character identities; this
/// registry only says which transport generation may still receive that run's notifications.
/// Every consumer must compare an entry with the selected-session directory immediately before
/// delivery, so a replacement connection never inherits an older run's traffic.
/// </remarks>
public sealed class DungeonRunDeliveryRegistry : IDungeonRunParticipantDepartureSink
{
    private readonly object gate = new();
    private readonly Dictionary<DungeonRunId, ImmutableArray<SelectedSessionIdentity>> entries = [];
    private readonly Dictionary<KillProjectionKey, ApplyMonsterKillResult> killResults = [];

    public void Register(
        DungeonRunId runId,
        IReadOnlyList<SelectedSessionIdentity> participants)
    {
        if (runId == default)
            throw new ArgumentException("A delivery registration requires a run identifier.", nameof(runId));
        ArgumentNullException.ThrowIfNull(participants);
        if (participants.Count == 0)
            throw new ArgumentException("A delivery registration requires a participant.", nameof(participants));

        var frozen = participants.ToImmutableArray();
        if (frozen.Select(value => value.CharacterId).Distinct().Count() != frozen.Length)
            throw new ArgumentException("A character may only have one frozen generation.", nameof(participants));
        if (frozen.Select(value => value.WireUserId).Distinct().Count() != frozen.Length)
            throw new ArgumentException("A wire user may only have one frozen generation.", nameof(participants));
        if (frozen.Select(value => value.ConnectionId).Distinct().Count() != frozen.Length)
            throw new ArgumentException("A connection may only be attached once.", nameof(participants));

        lock (gate)
            entries[runId] = frozen;
    }

    public IReadOnlyList<SelectedSessionIdentity> Find(DungeonRunId runId)
    {
        lock (gate)
            return entries.TryGetValue(runId, out var participants) ? participants : [];
    }

    public bool RemoveParticipant(DungeonRunId runId, CharacterId characterId)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(runId, out var participants))
                return false;

            var remaining = participants.RemoveAll(value => value.CharacterId == characterId);
            if (remaining.Length == participants.Length)
                return false;
            if (remaining.IsEmpty)
            {
                entries.Remove(runId);
                foreach (var key in killResults.Keys.Where(key => key.RunId == runId).ToArray())
                    killResults.Remove(key);
            }
            else
            {
                entries[runId] = remaining;
                foreach (var key in killResults.Keys
                             .Where(key => key.RunId == runId && key.CharacterId == characterId)
                             .ToArray())
                {
                    killResults.Remove(key);
                }
            }
            return true;
        }
    }

    public void RecordKillResult(
        DungeonRunId runId,
        RoomActorId actor,
        CharacterId characterId,
        ApplyMonsterKillResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (gate)
        {
            if (entries.TryGetValue(runId, out var participants)
                && participants.Any(value => value.CharacterId == characterId))
            {
                killResults[new KillProjectionKey(runId, actor, characterId)] = result;
            }
        }
    }

    public ApplyMonsterKillResult? FindKillResult(
        DungeonRunId runId,
        RoomActorId actor,
        CharacterId characterId)
    {
        lock (gate)
        {
            return killResults.TryGetValue(
                new KillProjectionKey(runId, actor, characterId), out var result)
                    ? result
                    : null;
        }
    }

    public void CompleteKillDelivery(DungeonRunId runId, RoomActorId actor)
    {
        lock (gate)
        {
            foreach (var key in killResults.Keys
                         .Where(key => key.RunId == runId && key.Actor == actor)
                         .ToArray())
            {
                killResults.Remove(key);
            }
        }
    }

    public void ParticipantDeparted(DungeonRunId runId, CharacterId characterId) =>
        RemoveParticipant(runId, characterId);

    private readonly record struct KillProjectionKey(
        DungeonRunId RunId,
        RoomActorId Actor,
        CharacterId CharacterId);
}
