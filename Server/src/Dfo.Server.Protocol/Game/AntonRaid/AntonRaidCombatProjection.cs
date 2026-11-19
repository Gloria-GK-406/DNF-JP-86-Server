using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Raids;

namespace Dfo.Server.Protocol.Game.AntonRaid;

internal sealed class AntonRaidCombatNotificationSubscriber(IEventService events, TimeProvider clock)
    : IDomainEventSubscriber<AntonRaidCombatChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<AntonRaidCombatChanged> message, CancellationToken ct)
    {
        var change = message.Payload;
        if (change.Audience.Count == 0) return;
        var notices = AntonRaidCombatFrames.Change(change, clock.GetUtcNow());
        if (notices.Count > 0)
            _ = await events.ProjectAsync(new NotificationAudience.TheseSessions(change.Audience),
                new AntonRaidRuntimeChanged(notices), ct).ConfigureAwait(false);
    }
}

internal static class AntonRaidCombatFrames
{
    internal static IReadOnlyList<IGameNotiMessage> Change(AntonRaidCombatChanged change, DateTimeOffset now)
    {
        if (change.Full)
            return Snapshot(change.Snapshot, now);
        var frames = new List<IGameNotiMessage>();
        foreach (var map in change.Maps)
            frames.Add(AntonRaidRuntimeNotice.Write(ServerMessageType.GameAntonRaidMapChanged, writer =>
            {
                writer.Write(checked((uint)map.Key));
                writer.Write((byte)0);
                writer.Write((byte)map.Value);
            }));
        if (change.InfectedMapChanged)
            frames.Add(MapSnapshot(change.Snapshot));
        // A repeated script pulse must occupy a separate frame.
        foreach (var symbol in change.Symbols)
            frames.Add(Symbols([symbol]));
        foreach (var timer in change.Timers)
        {
            frames.Add(Timer(timer));
            if (timer.Kind == AntonRaidCombatDeadlineKind.Attack)
                frames.Add(Remaining(timer.At, now));
        }
        if (change.BuffsChanged)
            frames.Add(Buffs(change.Snapshot.Buffs));
        if (change.Participation)
        {
            if (change.ParticipationGroups.Count > 0)
                frames.Add(Participation(change.ParticipationGroups));
            frames.Add(Status(change.Snapshot.Sorties));
        }
        if (change.LifeMember != 0)
            frames.Add(AntonRaidRuntimeNotice.Write(ServerMessageType.GameAntonRaidMemberLife, writer =>
            {
                writer.Write(change.LifeMember);
                writer.Write(change.LifeState);
            }));
        return frames;
    }

    internal static IReadOnlyList<IGameNotiMessage> Snapshot(AntonRaidCombatSnapshot snapshot, DateTimeOffset now)
    {
        var frames = new List<IGameNotiMessage> { MapSnapshot(snapshot), Symbols([new(0, 0)]) };
        if (snapshot.Symbols.Count > 0)
            frames.Add(Symbols(snapshot.Symbols.ToArray()));
        frames.AddRange(snapshot.Timers.Select(Timer));
        if (snapshot.Timers.SingleOrDefault(timer => timer.Kind is AntonRaidCombatDeadlineKind.Attack
            or AntonRaidCombatDeadlineKind.Intermission) is { Duration: > 0 } wholePhase)
            frames.Add(Remaining(wholePhase.At, now, wholePhase.Kind == AntonRaidCombatDeadlineKind.Intermission ? (byte)1 : (byte)0));
        var groups = snapshot.Sorties.Where(sortie => sortie.Participants.Count > 0).SelectMany(sortie =>
        {
            var users = sortie.Participants.Select(member => member.WireUserId).ToArray();
            return new AntonRaidParticipationGroup[] { new(sortie.DungeonId, 0, users), new(sortie.DungeonId, (byte)(sortie.Cleared ? 4 : 1), users) };
        }).ToArray();
        if (groups.Length > 0)
            frames.Add(Participation(groups));
        frames.Add(Buffs(snapshot.Buffs));
        frames.Add(Status(snapshot.Sorties));
        return frames;
    }

    private static AntonRaidRuntimeNotice MapSnapshot(AntonRaidCombatSnapshot snapshot) =>
        AntonRaidRuntimeNotice.Write(ServerMessageType.GameAntonRaidMapSnapshot, writer =>
        {
            writer.Write((byte)0);
            writer.Write(checked((byte)snapshot.Maps.Count));
            foreach (var map in snapshot.Maps)
            {
                writer.Write(checked((uint)map.Key));
                writer.Write((byte)map.Value);
            }
            writer.Write(snapshot.InfectedMap);
        });

    private static AntonRaidRuntimeNotice Symbols(IReadOnlyList<KeyValuePair<uint, uint>> symbols) =>
        AntonRaidRuntimeNotice.Write(ServerMessageType.GameAntonRaidSymbols, writer =>
        {
            writer.Write(checked((byte)symbols.Count));
            foreach (var symbol in symbols)
            {
                writer.Write(symbol.Key);
                writer.Write(symbol.Value);
            }
        });

    private static AntonRaidRuntimeNotice Timer(AntonRaidCombatDeadline timer) =>
        AntonRaidRuntimeNotice.Write(ServerMessageType.GameAntonRaidSetTimer, writer =>
        {
            writer.Write(timer.TimerType);
            writer.Write(timer.Key);
            writer.Write(checked((uint)timer.At.ToUnixTimeSeconds()));
            writer.Write(checked((uint)timer.Duration));
        });

    private static AntonRaidRuntimeNotice Remaining(DateTimeOffset at, DateTimeOffset now, byte kind = 0) =>
        AntonRaidRuntimeNotice.Write(ServerMessageType.GameAntonRaidRemaining, writer =>
        {
            writer.Write(kind);
            writer.Write(checked((uint)Math.Max(0, Math.Ceiling((at - now).TotalSeconds))));
        });

    private static AntonRaidRuntimeNotice Buffs(IReadOnlyList<AntonRaidBuffGroup> groups) =>
        AntonRaidRuntimeNotice.Write(ServerMessageType.GameAntonRaidBuffStatus, writer =>
        {
            writer.Write(checked((byte)groups.Count));
            foreach (var buff in groups)
            {
                writer.Write(buff.Type);
                writer.Write(checked((byte)buff.Targets.Count));
                foreach (var target in buff.Targets)
                {
                    writer.Write(target.Squad);
                    writer.Write(target.User);
                    writer.Write(checked((uint)target.ActiveUntil.ToUnixTimeSeconds()));
                    writer.Write(checked((uint)target.CooldownUntil.ToUnixTimeSeconds()));
                }
            }
        });

    private static AntonRaidRuntimeNotice Participation(IReadOnlyList<AntonRaidParticipationGroup> groups) =>
        AntonRaidRuntimeNotice.Write(ServerMessageType.GameAntonRaidParticipation, writer =>
        {
            writer.Write(checked((byte)groups.Count));
            foreach (var group in groups)
            {
                writer.Write(checked((uint)group.DungeonId));
                writer.Write(group.Operation);
                writer.Write(checked((byte)group.Members.Count));
                foreach (var member in group.Members) writer.Write(member);
            }
        });

    private static AntonRaidRuntimeNotice Status(IReadOnlyList<AntonRaidSortieSnapshot> sorties) =>
        AntonRaidRuntimeNotice.Write(ServerMessageType.GameAntonRaidMonsterStatus, writer =>
        {
            writer.Write(checked((byte)sorties.Count));
            foreach (var sortie in sorties)
            {
                writer.Write((ushort)sortie.Squad);
                writer.Write(checked((byte)sortie.Participants.Count));
                foreach (var member in sortie.Participants) writer.Write(member.WireUserId);
                writer.Write(checked((uint)sortie.UsedCoins));
                writer.Write(checked((byte)sortie.MonsterHitPoints.Count));
                foreach (var hp in sortie.MonsterHitPoints) writer.Write(hp);
            }
        });
}
