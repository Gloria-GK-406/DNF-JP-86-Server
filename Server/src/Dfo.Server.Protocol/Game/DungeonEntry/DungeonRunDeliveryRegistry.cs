using System.Collections.Immutable;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.Dungeons;

/// <summary>
/// Freezes the selected-session generations that were attached when a dungeon run began.
/// </summary>
/// <remarks>
/// This is deliberately a protocol concern. The run owns stable character identities; this
/// registry only says which transport generation may still receive that run's notifications.
/// Every consumer must compare an entry with the selected-session directory immediately before
/// delivery, so a replacement connection never inherits an older run's traffic.
/// </remarks>
public sealed class DungeonRunDeliveryRegistry : IDungeonRunParticipantDepartureSink, Dfo.Server.Abstractions.Networking.IDungeonRunProjectionDomainService
{
    private readonly object gate = new();
    private readonly Dictionary<DungeonRunId, ImmutableArray<SelectedSessionIdentity>> entries = [];

    public void Register(
        DungeonRunId runId,
        IReadOnlyList<SelectedSessionIdentity> participants)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDeliveryRegistry) + "." + nameof(Register));
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
        GameplayWriteAssertion.Writing(nameof(DungeonRunDeliveryRegistry) + "." + nameof(RemoveParticipant));
        lock (gate)
        {
            if (!entries.TryGetValue(runId, out var participants))
                return false;

            var remaining = participants.RemoveAll(value => value.CharacterId == characterId);
            if (remaining.Length == participants.Length)
                return false;
            if (remaining.IsEmpty) entries.Remove(runId);
            else entries[runId] = remaining;
            return true;
        }
    }

    public void ParticipantDeparted(DungeonRunId runId, CharacterId characterId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDeliveryRegistry) + "." + nameof(ParticipantDeparted));
        RemoveParticipant(runId, characterId);
    }

}
