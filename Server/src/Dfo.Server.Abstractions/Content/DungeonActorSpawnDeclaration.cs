namespace Dfo.Server.Abstractions.Content;

public enum DungeonActorSpawnSource
{
    Monster,
    AiCharacter,
    ConditionalMonster,
    ConditionalSummon,
    SpecialPassiveObject,
}

public enum DungeonActorFaction { Character = 0, Monster = 100, Neutral = 200 }

public readonly record struct DungeonSpawnPosition(int X, int Y, int Z);

/// <summary>A source declaration, before a room decides whether and how it becomes a live actor.</summary>
public sealed record DungeonActorSpawnDeclaration(int Code, DungeonActorSpawnSource Source, int RecordIndex)
{
    public bool RelativeLevel { get; init; }
    public int LevelValue { get; init; }
    public DungeonSpawnPosition Position { get; init; }
    public string Placement { get; init; } = "[fixed]";
    public byte Rank { get; init; }
    public bool IsDummy { get; init; }
    public int? BoundNpcId { get; init; }
    public IReadOnlyList<string> ChampionFeatures { get; init; } = [];
    public bool HasChampionFeatureCount { get; init; }
    public DungeonActorFaction Faction { get; init; } = DungeonActorFaction.Monster;
    public int? MonsterTeam { get; init; }
    public int RandomDropCount { get; init; } = 1;
    public int FixedDropCount { get; init; }
    public int? ParentObjectIndex { get; init; }
    public int? ChildIndex { get; init; }
    public int? ParentObjectCode { get; init; }
    public IReadOnlyList<int> Parameters { get; init; } = [];

    /// <summary>Verified controller content, not an inference from arbitrary OBJ integer data.</summary>
    public bool IsSceneOwnedWave { get; init; }
}

/// <summary>Ancillary declaration whose script semantics are not executed by ordinary room generation.</summary>
public readonly record struct DungeonDeclarationValue(int? Integer, string? Text);

public sealed record DungeonActorDeclarationSupplement(
    string Section, IReadOnlyList<DungeonDeclarationValue> Values);

public sealed record DungeonMapActorDeclarations(
    IReadOnlyList<DungeonActorSpawnDeclaration> Actors,
    IReadOnlyList<DungeonActorDeclarationSupplement> Supplements);
