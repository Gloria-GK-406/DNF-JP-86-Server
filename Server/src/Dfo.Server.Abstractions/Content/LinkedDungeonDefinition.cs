namespace Dfo.Server.Abstractions.Content;

/// <summary>Archive-declared continuation candidates; conditions remain uninterpreted content.</summary>
public sealed record LinkedDungeonCandidate(int DungeonId, int Weight, int Condition);

public sealed record LinkedDungeonClearObject(int ObjectCode, int X, int Y);

/// <summary>Content alone never authorizes an entry or proves that the current UI exposes it.</summary>
public sealed record LinkedDungeonDefinition(
    IReadOnlyList<LinkedDungeonCandidate> Next,
    int NoSelectionWeight,
    IReadOnlyList<int> PreviousDungeonIds,
    LinkedDungeonClearObject? ClearObject);
