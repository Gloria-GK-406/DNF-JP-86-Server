namespace Dfo.Server.Abstractions.Content;

public enum DungeonMapKind : byte { Normal, Boss, Dummy }

/// <summary>The first MAP greed token is its pattern; the second is its allowed entrance mask.</summary>
public readonly record struct DungeonMapConnection(
    DungeonPathDirection Pattern, DungeonPathDirection Entrances);

public sealed record DungeonMapDefinition(
    int MapId,
    IReadOnlyList<int> DungeonIds,
    DungeonMapKind Kind,
    IReadOnlyList<DungeonMapConnection> Connections,
    bool HasStartArea);

public interface IDungeonMapDefinitionSource
{
    ValueTask<DungeonMapDefinition?> GetAsync(int mapId, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<DungeonMapDefinition>> ListForDungeonAsync(
        int dungeonId, CancellationToken cancellationToken);
}
