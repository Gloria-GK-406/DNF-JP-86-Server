namespace Dfo.Server.Abstractions.Relations;

/// <summary>The rules of character relations: which kinds exist and how many of each one owner may hold.</summary>
public interface ICharacterRelationRuleService
{
    /// <summary>How many relations of that kind one owner may hold; throws for a value that is not a relation kind.</summary>
    int Capacity(CharacterRelationKind kind);
}
