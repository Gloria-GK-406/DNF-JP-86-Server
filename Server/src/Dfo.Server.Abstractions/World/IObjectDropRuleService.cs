using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

public interface IObjectDropRuleService
{
    IReadOnlyList<DungeonRoomDrop> Draw(
        IReadOnlyList<ObjectDropDefinition> definitions, DungeonCellCreationContext context);
}
