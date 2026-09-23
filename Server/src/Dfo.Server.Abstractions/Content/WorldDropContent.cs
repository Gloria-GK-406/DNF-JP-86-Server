using System.Collections.Immutable;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Abstractions.Content;

public sealed record WorldDropEntry(int ItemId, int Weight, DropRarity Rarity, DropItemKind Kind, ushort Durability);
public sealed record WorldDropLevel(int Level, ImmutableArray<WorldDropEntry> Entries);
public interface IWorldDropTableSource
{
    IReadOnlyList<WorldDropLevel> Load();
}
