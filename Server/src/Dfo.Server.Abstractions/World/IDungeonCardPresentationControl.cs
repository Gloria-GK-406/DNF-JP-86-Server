using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
namespace Dfo.Server.Abstractions.World;
/// <summary>Requests only the two fixed presentation stages owned by the card producer.</summary>
public interface IDungeonCardPresentationControl
{
    void ScheduleAfterSettlement(SelectedSessionIdentity session, DungeonRunId runId);
    void ScheduleAutoFlip(SelectedSessionIdentity session, DungeonRunId runId);
    void Cancel(CharacterId characterId, DungeonRunId runId);
}
