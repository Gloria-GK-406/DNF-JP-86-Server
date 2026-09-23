using Dfo.Server.Abstractions.World;
namespace Dfo.Server.Abstractions.Content;

public enum ExtraDropDungeonType { Normal, Ancient, Dimension }

/// <summary>Only explicitly tagged items participate; absent masks never mean all.</summary>
public sealed record ExtraDropContent(
    IReadOnlyDictionary<(DropItemKind Kind, int Difficulty, ExtraDropDungeonType DungeonType), DropItemPool> Pools,
    IReadOnlyDictionary<int, ExtraDropDungeonType> Dungeons,
    int ItemCount);

public interface IExtraDropContentSource
{
    ExtraDropContent Load();
}

public interface IExtraDropRuleService
{
    int Initialize();
    IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request, int attempt);
}
