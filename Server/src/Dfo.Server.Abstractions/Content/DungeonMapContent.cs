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
    bool HasStartArea)
{
    /// <summary>
    /// Codes of the map's <c>[monster condition]</c> rows: the conditional monsters a dungeon's
    /// <c>[boss room entrance condition]</c> may place in this room.
    /// </summary>
    public IReadOnlyList<int> ConditionalMonsterCodes { get; init; } = [];
}

public interface IDungeonMapDefinitionSource
{
    ValueTask<DungeonMapDefinition?> GetAsync(int mapId, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<DungeonMapDefinition>> ListForDungeonAsync(
        int dungeonId, CancellationToken cancellationToken);
}
