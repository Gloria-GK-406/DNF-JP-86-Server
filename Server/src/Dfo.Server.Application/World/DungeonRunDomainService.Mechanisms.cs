using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunDomainService
{
    public IReadOnlyList<DungeonRoomMechanismView> AdvanceRoomMechanisms(DateTimeOffset now)
    {
        lock (this.gate)
        {
            var views = new List<DungeonRoomMechanismView>();
            foreach (var run in this.runsById.Values)
            {
                var cell = run.Cells[run.CurrentCell];
                if (cell.Mechanisms.Count == 0 || cell.Mechanisms.Any(state => state.StartedAt is null)) continue;
                cell.Mechanisms = cell.Mechanisms.Select(state => this.mechanismRules.Advance(state, now)).ToArray();
                var recipients = run.Participants.Where(member => run.IsActiveMember(member.CharacterId)
                    && run.ReadyParticipants.Contains(member.CharacterId)).Select(member => member.CharacterId).ToArray();
                if (recipients.Length > 0)
                    views.Add(new(run.RunId, run.CurrentCell, run.RoomVersion, cell.Mechanisms.ToArray(), recipients));
            }
            return views;
        }
    }
}
