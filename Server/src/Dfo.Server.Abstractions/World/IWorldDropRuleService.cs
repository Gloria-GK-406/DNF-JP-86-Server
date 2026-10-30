namespace Dfo.Server.Abstractions.World;

public interface IWorldDropRuleService
{
    int Initialize();
    IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request);
}
